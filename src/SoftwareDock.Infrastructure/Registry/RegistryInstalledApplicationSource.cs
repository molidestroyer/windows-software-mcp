using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SoftwareDock.Application.Abstractions;
using SoftwareDock.Domain.Applications;
using RegistryView = SoftwareDock.Domain.Applications.RegistryView;
using Win32RegistryView = Microsoft.Win32.RegistryView;

namespace SoftwareDock.Infrastructure.Registry;

/// <summary>
/// Reads the same <c>Uninstall</c> keys that "Installed apps" in Settings reads, with the same
/// filtering, so both show the same list.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class RegistryInstalledApplicationSource(ILogger<RegistryInstalledApplicationSource> logger)
    : IInstalledApplicationSource
{
    private const string UninstallPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    // Patches and hotfixes registered as children of a product, not products themselves.
    private static readonly HashSet<string> HiddenReleaseTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Hotfix", "Security Update", "Update Rollup", "Update",
    };

    private static readonly (RegistryHive Hive, Win32RegistryView View, InstallScope Scope, RegistryView Arch)[] Locations =
    [
        (RegistryHive.LocalMachine, Win32RegistryView.Registry64, InstallScope.Machine, RegistryView.Native64),
        (RegistryHive.LocalMachine, Win32RegistryView.Registry32, InstallScope.Machine, RegistryView.Wow32),

        // HKCU\Software is shared between views: reading it twice would list every per-user app twice.
        (RegistryHive.CurrentUser, Win32RegistryView.Registry64, InstallScope.User, RegistryView.Native64),
    ];

    public Task<IReadOnlyList<InstalledApplication>> GetInstalledApplicationsAsync(CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<InstalledApplication>>(() => ReadAll(cancellationToken), cancellationToken);

    private List<InstalledApplication> ReadAll(CancellationToken cancellationToken)
    {
        var applications = new List<InstalledApplication>();
        foreach (var (hive, view, scope, arch) in Locations)
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = root.OpenSubKey(UninstallPath);
            if (uninstall is null)
            {
                continue;
            }

            foreach (var name in uninstall.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var entry = uninstall.OpenSubKey(name);
                    if (entry is not null && Read(entry, name, scope, arch) is { } application)
                    {
                        applications.Add(application);
                    }
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                    LogEntrySkipped(ex, name);
                }
            }
        }

        return applications;
    }

    private static InstalledApplication? Read(RegistryKey entry, string subKeyName, InstallScope scope, RegistryView arch)
    {
        var name = GetString(entry, "DisplayName");
        if (name is null
            || GetInt(entry, "SystemComponent") == 1
            || GetString(entry, "ParentKeyName") is not null
            || HiddenReleaseTypes.Contains(GetString(entry, "ReleaseType") ?? string.Empty))
        {
            return null;
        }

        UninstallCommand.TryParse(Expand(GetString(entry, "UninstallString")), out var uninstall);
        UninstallCommand.TryParse(Expand(GetString(entry, "QuietUninstallString")), out var quiet);

        return new InstalledApplication
        {
            Key = ApplicationKey.ForRegistry(scope, arch, subKeyName),
            Name = name,
            Origin = ApplicationOrigin.Registry,
            Publisher = GetString(entry, "Publisher"),
            Version = GetString(entry, "DisplayVersion"),
            Scope = scope,
            InstallLocation = GetString(entry, "InstallLocation"),
            InstallDate = ParseInstallDate(GetString(entry, "InstallDate")),
            EstimatedSizeBytes = GetInt(entry, "EstimatedSize") is { } kb and > 0 ? kb * 1024L : null,
            IconPath = Expand(GetString(entry, "DisplayIcon")),
            Uninstall = uninstall,
            QuietUninstall = quiet,
        };
    }

    private static string? GetString(RegistryKey key, string name) =>
        key.GetValue(name) is string { Length: > 0 } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static int? GetInt(RegistryKey key, string name) => key.GetValue(name) switch
    {
        int i => i,
        string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null,
    };

    private static string? Expand(string? value) => value is null ? null : Environment.ExpandEnvironmentVariables(value);

    internal static DateOnly? ParseInstallDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipped uninstall entry {SubKey}: access denied")]
    private partial void LogEntrySkipped(Exception exception, string subKey);
}
