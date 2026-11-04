using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace SoftwareDock.App.Services;

/// <summary>A newer version of the dock, downloaded or not yet.</summary>
public sealed record AppUpdate(string Version, object Handle);

/// <summary>Self-update of the dock itself (not of the software it manages).</summary>
public interface IAppUpdater
{
    /// <summary>False when not installed through Setup.exe (running from source, portable copy, demo).</summary>
    bool IsSupported { get; }

    string CurrentVersion { get; }

    Task<AppUpdate?> CheckAsync(CancellationToken cancellationToken);

    Task DownloadAsync(AppUpdate update, IProgress<int>? progress, CancellationToken cancellationToken);

    /// <summary>Exits, swaps the files and starts the new version.</summary>
    void ApplyAndRestart(AppUpdate update);
}

/// <summary>
/// Velopack against this repository's GitHub releases: CI publishes them on every <c>v*</c> tag,
/// with deltas, so an update is usually a few hundred KB.
/// </summary>
public sealed partial class VelopackAppUpdater : IAppUpdater
{
    public const string RepositoryUrl = "https://github.com/molidestroyer/windows-software-mcp";

    private readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, null, false, null), null, null);
    private readonly ILogger<VelopackAppUpdater> _logger;

    public VelopackAppUpdater(ILogger<VelopackAppUpdater> logger) => _logger = logger;

    public bool IsSupported => _manager.IsInstalled;

    public string CurrentVersion => _manager.CurrentVersion?.ToString() ?? AssemblyVersion.Current;

    public async Task<AppUpdate?> CheckAsync(CancellationToken cancellationToken)
    {
        if (!IsSupported)
        {
            return null;
        }

        var info = await _manager.CheckForUpdatesAsync().WaitAsync(cancellationToken);
        if (info is null)
        {
            return null;
        }

        LogUpdateFound(info.TargetFullRelease.Version.ToString());
        return new AppUpdate(info.TargetFullRelease.Version.ToString(), info);
    }

    public Task DownloadAsync(AppUpdate update, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        return _manager.DownloadUpdatesAsync((UpdateInfo)update.Handle, p => progress?.Report(p), cancellationToken);
    }

    public void ApplyAndRestart(AppUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        _manager.ApplyUpdatesAndRestart(((UpdateInfo)update.Handle).TargetFullRelease, ["--background"]);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Software Dock {Version} is available")]
    private partial void LogUpdateFound(string version);
}

/// <summary>Running from source or in demo mode: there is nothing to update.</summary>
public sealed class NoAppUpdater : IAppUpdater
{
    public bool IsSupported => false;

    public string CurrentVersion => AssemblyVersion.Current;

    public Task<AppUpdate?> CheckAsync(CancellationToken cancellationToken) => Task.FromResult<AppUpdate?>(null);

    public Task DownloadAsync(AppUpdate update, IProgress<int>? progress, CancellationToken cancellationToken) => Task.CompletedTask;

    public void ApplyAndRestart(AppUpdate update)
    {
    }
}

internal static class AssemblyVersion
{
    public static string Current { get; } =
        typeof(AssemblyVersion).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
