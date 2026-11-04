using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoftwareDock.Application.Abstractions;
using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Operations;
using SoftwareDock.Domain.Packages;
using SoftwareDock.Infrastructure.Processes;

namespace SoftwareDock.Infrastructure.WinGet;

/// <summary><see cref="IPackageManager"/> over the <c>winget</c> command line.</summary>
/// <remarks>
/// Every invocation passes <c>--disable-interactivity</c> and accepts agreements up front: there is
/// no console to answer a prompt, and a prompt nobody can see is a hang. Ids and queries go
/// through named options (<c>--id</c>, <c>--query</c>), never positionally.
/// </remarks>
public sealed partial class WinGetPackageManager : IPackageManager
{
    private static readonly string[] NonInteractive = ["--accept-source-agreements", "--disable-interactivity"];

    private readonly IProcessRunner _runner;
    private readonly IOptions<WinGetOptions> _options;
    private readonly ILogger<WinGetPackageManager> _logger;

    public WinGetPackageManager(IProcessRunner runner, IOptions<WinGetOptions> options, ILogger<WinGetPackageManager> logger)
    {
        _runner = runner;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<InstalledPackage>> ListInstalledAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync(["list", .. NonInteractive], _options.Value.QueryTimeout, null, cancellationToken);
        var rows = WinGetTableParser.Parse(result.StandardOutput);

        var packages = new List<InstalledPackage>(rows.Count);
        foreach (var row in rows)
        {
            // Name | Id | Version [| Available] [| Source] — Available only appears when some row has one.
            if (!PackageId.TryParse(row[1], out var id, out _))
            {
                continue;
            }

            var (available, source) = row.Length switch
            {
                >= 5 => (row[3], row[4]),
                4 => (null, row[3]),
                _ => ((string?)null, (string?)null),
            };
            packages.Add(new InstalledPackage(row[0], id, NullIfEmpty(row[2]), NullIfEmpty(available), NullIfEmpty(source)));
        }

        LogParsed("list", packages.Count);
        return packages;
    }

    public async Task<IReadOnlyList<PackageSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResults);

        var result = await RunAsync(
            ["search", "--query", query.Trim(), "--count", maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture), .. NonInteractive],
            _options.Value.QueryTimeout,
            null,
            cancellationToken);
        var rows = WinGetTableParser.Parse(result.StandardOutput);

        var packages = new List<PackageSearchResult>(rows.Count);
        foreach (var row in rows)
        {
            // Name | Id | Version [| Match] | Source — Match only appears when matching on a tag or moniker.
            if (!PackageId.TryParse(row[1], out var id, out _))
            {
                continue;
            }

            var source = row.Length >= 4 ? row[^1] : null;
            packages.Add(new PackageSearchResult(row[0], id, NullIfEmpty(row.Length > 2 ? row[2] : null), NullIfEmpty(source)));
        }

        LogParsed("search", packages.Count);
        return packages;
    }

    public async Task<OperationResult> InstallAsync(InstallRequest request, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<string> arguments = ["install", "--id", request.Id.Value, "--exact", "--silent", "--accept-package-agreements", .. NonInteractive];
        AddIfPresent(arguments, "--version", request.Version);
        AddIfPresent(arguments, "--source", request.Source);
        AddIfPresent(arguments, "--scope", request.Scope switch
        {
            InstallScope.User => "user",
            InstallScope.Machine => "machine",
            _ => null,
        });

        var result = await RunAsync(arguments, null, progress, cancellationToken);
        return WinGetExitCodes.ToResult("Install", request.Id.Value, result.ExitCode, result.StandardOutput);
    }

    public async Task<OperationResult> UpgradeAsync(PackageId id, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        var result = await RunAsync(
            ["upgrade", "--id", id.Value, "--exact", "--silent", "--accept-package-agreements", .. NonInteractive],
            null,
            progress,
            cancellationToken);
        return WinGetExitCodes.ToResult("Upgrade", id.Value, result.ExitCode, result.StandardOutput);
    }

    public async Task<OperationResult> UninstallAsync(PackageId id, bool silent, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        var result = await RunAsync(
            ["uninstall", "--id", id.Value, "--exact", silent ? "--silent" : "--interactive", .. NonInteractive],
            null,
            progress,
            cancellationToken);
        return WinGetExitCodes.ToResult("Uninstall", id.Value, result.ExitCode, result.StandardOutput);
    }

    private async Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan? timeout,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _runner.RunAsync(new ProcessRequest(_options.Value.ExecutablePath, arguments, timeout), progress, cancellationToken);
        }
        catch (Win32Exception ex)
        {
            throw new PackageManagerUnavailableException(
                "WinGet is not available. Install or update 'App Installer' from the Microsoft Store.", ex);
        }
    }

    private static void AddIfPresent(List<string> arguments, string option, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            if (value.TrimStart().StartsWith('-'))
            {
                throw new ArgumentException($"'{value}' is not a valid value for {option}.", nameof(value));
            }

            arguments.Add(option);
            arguments.Add(value.Trim());
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    [LoggerMessage(Level = LogLevel.Debug, Message = "winget {Command}: {Count} rows parsed")]
    private partial void LogParsed(string command, int count);
}
