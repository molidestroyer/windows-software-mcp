using System.Runtime.Versioning;
using Microsoft.Win32;
using SoftwareDock.Application.Abstractions;
using UninstallCommand = SoftwareDock.Domain.Applications.UninstallCommand;
using SoftwareDock.Domain.Operations;
using SoftwareDock.Domain.Startup;

namespace SoftwareDock.Infrastructure.Startup;

/// <summary>
/// The same startup sources Task Manager shows — Run keys and Startup folders — and the same
/// <c>StartupApproved</c> switch it flips, so a change made here shows up there and vice versa.
/// </summary>
/// <remarks>Scheduled tasks and services are out of scope: Task Manager does not list them either.</remarks>
[SupportedOSPlatform("windows")]
public sealed class RegistryStartupItemSource(TimeProvider time) : IStartupItemSource
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    public Task<IReadOnlyList<StartupItem>> ListAsync(CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<StartupItem>>(
            () =>
            [
                .. ReadRun(RegistryHive.CurrentUser, RegistryView.Default, StartupLocation.UserRun),
                .. ReadRun(RegistryHive.LocalMachine, RegistryView.Registry64, StartupLocation.MachineRun),
                .. ReadRun(RegistryHive.LocalMachine, RegistryView.Registry32, StartupLocation.MachineRun32),
                .. ReadFolder(Environment.SpecialFolder.Startup, StartupLocation.UserStartupFolder),
                .. ReadFolder(Environment.SpecialFolder.CommonStartup, StartupLocation.CommonStartupFolder),
            ],
            cancellationToken);

    public Task<OperationResult> SetEnabledAsync(StartupItem item, bool enabled, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var (hive, subKey) = Approval(item.Location);
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = root.CreateSubKey($@"{ApprovedKey}\{subKey}", writable: true);
            key.SetValue(item.Name, enabled ? StartupApproval.Enabled() : StartupApproval.Disabled(time.GetUtcNow()), RegistryValueKind.Binary);
            return Task.FromResult(OperationResult.Success($"{item.Name} will {(enabled ? string.Empty : "no longer ")}start with Windows."));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Task.FromResult(OperationResult.Failure(
                $"{item.Name} starts for every user; changing it needs administrator rights (Task Manager › Startup apps, run as administrator)."));
        }
    }

    private static (RegistryHive Hive, string SubKey) Approval(StartupLocation location) => location switch
    {
        StartupLocation.UserRun => (RegistryHive.CurrentUser, "Run"),
        StartupLocation.MachineRun => (RegistryHive.LocalMachine, "Run"),
        StartupLocation.MachineRun32 => (RegistryHive.LocalMachine, "Run32"),
        StartupLocation.UserStartupFolder => (RegistryHive.CurrentUser, "StartupFolder"),
        StartupLocation.CommonStartupFolder => (RegistryHive.LocalMachine, "StartupFolder"),
        _ => throw new ArgumentOutOfRangeException(nameof(location), location, null),
    };

    private static IEnumerable<StartupItem> ReadRun(RegistryHive hive, RegistryView view, StartupLocation location)
    {
        using var root = RegistryKey.OpenBaseKey(hive, view);
        using var run = root.OpenSubKey(RunKey);
        if (run is null)
        {
            return [];
        }

        var approvals = ReadApprovals(location);
        var items = new List<StartupItem>();
        foreach (var name in run.GetValueNames().Where(n => n.Length > 0))
        {
            if (run.GetValue(name) is not string command || string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            UninstallCommand.TryParse(Environment.ExpandEnvironmentVariables(command), out var parsed);
            items.Add(new StartupItem(name, command, location, parsed?.FileName, StartupApproval.IsEnabled(approvals.GetValueOrDefault(name))));
        }

        return items;
    }

    private static IEnumerable<StartupItem> ReadFolder(Environment.SpecialFolder folder, StartupLocation location)
    {
        var path = Environment.GetFolderPath(folder);
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            return [];
        }

        var approvals = ReadApprovals(location);
        return Directory.EnumerateFiles(path)
            .Where(f => !Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            .Select(f =>
            {
                var name = Path.GetFileName(f);
                var target = f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ReadShortcut(f) : f;
                return new StartupItem(name, f, location, target, StartupApproval.IsEnabled(approvals.GetValueOrDefault(name)));
            })
            .ToList();
    }

    private static string? ReadShortcut(string path)
    {
        try
        {
            return ShellLinkReader.TryReadTarget(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Dictionary<string, byte[]> ReadApprovals(StartupLocation location)
    {
        var (hive, subKey) = Approval(location);
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = root.OpenSubKey($@"{ApprovedKey}\{subKey}");
        return key is null
            ? new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            : key.GetValueNames()
                .Select(n => (Name: n, Value: key.GetValue(n) as byte[]))
                .Where(v => v.Value is not null)
                .ToDictionary(v => v.Name, v => v.Value!, StringComparer.OrdinalIgnoreCase);
    }
}
