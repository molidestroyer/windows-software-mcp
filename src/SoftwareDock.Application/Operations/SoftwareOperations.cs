using Microsoft.Extensions.Logging;
using SoftwareDock.Application.Abstractions;
using SoftwareDock.Application.Inventory;
using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Operations;
using SoftwareDock.Domain.Packages;

namespace SoftwareDock.Application.Operations;

/// <summary>
/// Orchestrates install, upgrade and uninstall: picks the right mechanism, serializes the work
/// and keeps the inventory honest afterwards.
/// </summary>
public sealed partial class SoftwareOperations : ISoftwareOperations, IDisposable
{
    private readonly ISoftwareInventory _inventory;
    private readonly IPackageManager _packageManager;
    private readonly IUninstallerLauncher _uninstaller;
    private readonly IInstalledApplicationSource _registry;
    private readonly ISystemMonitor _system;
    private readonly TimeProvider _time;
    private readonly ILogger<SoftwareOperations> _logger;
    private static readonly TimeSpan HandOffPoll = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan HandOffLimit = TimeSpan.FromMinutes(10);
    private readonly SemaphoreSlim _queue = new(1, 1);

    public SoftwareOperations(
        ISoftwareInventory inventory,
        IPackageManager packageManager,
        IUninstallerLauncher uninstaller,
        IInstalledApplicationSource registry,
        ISystemMonitor system,
        TimeProvider time,
        ILogger<SoftwareOperations> logger)
    {
        _inventory = inventory;
        _packageManager = packageManager;
        _uninstaller = uninstaller;
        _registry = registry;
        _system = system;
        _time = time;
        _logger = logger;
    }

    public event EventHandler<OperationCompletedEventArgs>? OperationCompleted;

    public Task<OperationResult> InstallAsync(InstallRequest request, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RunExclusiveAsync(
            $"Install {request.Id}",
            ct => request.Id.IsTruncated
                ? Task.FromResult(TruncatedId(request.Id))
                : _packageManager.InstallAsync(request, progress, ct),
            cancellationToken);
    }

    public async Task<OperationResult> UpgradeAsync(ApplicationKey key, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        var application = await _inventory.FindAsync(key, cancellationToken);
        if (application is null)
        {
            return NotInstalled(key);
        }

        if (!application.CanUpgrade)
        {
            return OperationResult.Failure(application.HasUpdate
                ? $"{application.Name} has an update, but no package source it can be upgraded from."
                : $"{application.Name} is already up to date.");
        }

        return await RunExclusiveAsync(
            $"Upgrade {application.Name}",
            ct => _packageManager.UpgradeAsync(application.PackageId!, progress, ct),
            cancellationToken);
    }

    public async Task<OperationResult> UninstallAsync(
        ApplicationKey key,
        UninstallMode mode,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        var application = await _inventory.FindAsync(key, cancellationToken);
        return application is null
            ? NotInstalled(key)
            : await UninstallAsync(application, mode, progress, cancellationToken);
    }

    public async Task<BatchResult> UninstallManyAsync(
        IReadOnlyList<ApplicationKey> keys,
        UninstallMode mode,
        IProgress<BatchProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);

        // Resolve everything from one snapshot: each removal invalidates the inventory, and
        // rebuilding it between items would cost a full WinGet listing per application.
        var snapshot = await _inventory.GetSnapshotAsync(forceRefresh: false, cancellationToken);
        var items = keys.Distinct().Select(k => (Key: k, App: snapshot.Find(k))).ToList();

        return await RunBatchAsync(
            items,
            i => i.App?.Name ?? i.Key.Value,
            (i, lines, ct) => i.App is null ? Task.FromResult(NotInstalled(i.Key)) : UninstallAsync(i.App, mode, lines, ct),
            progress,
            cancellationToken);
    }

    public async Task<BatchResult> UpgradeManyAsync(
        IReadOnlyList<ApplicationKey> keys,
        IProgress<BatchProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var snapshot = await _inventory.GetSnapshotAsync(forceRefresh: false, cancellationToken);
        var items = keys.Distinct().Select(k => (Key: k, App: snapshot.Find(k))).ToList();

        return await RunBatchAsync(
            items,
            i => i.App?.Name ?? i.Key.Value,
            (i, lines, ct) => i.App switch
            {
                null => Task.FromResult(NotInstalled(i.Key)),
                { CanUpgrade: false } app => Task.FromResult(OperationResult.Failure($"{app.Name} has no upgrade available.")),
                var app => RunExclusiveAsync($"Upgrade {app.Name}", c => _packageManager.UpgradeAsync(app.PackageId!, lines, c), ct),
            },
            progress,
            cancellationToken);
    }

    public Task<BatchResult> InstallManyAsync(
        IReadOnlyList<InstallRequest> requests,
        IProgress<BatchProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        return RunBatchAsync(
            requests.DistinctBy(r => r.Id).ToList(),
            r => r.Id.Value,
            (r, lines, ct) => InstallAsync(r, lines, ct),
            progress,
            cancellationToken);
    }

    public void Dispose() => _queue.Dispose();

    private async Task<OperationResult> UninstallAsync(
        InstalledApplication application,
        UninstallMode mode,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var plan = UninstallPlan.For(application, mode);
        if (plan is null)
        {
            return OperationResult.Failure($"{application.Name} has no registered uninstaller and no package manager knows it.");
        }

        LogUninstallPlan(application.Name, plan.Kind, plan.Silent);
        return await RunExclusiveAsync(
            $"Uninstall {application.Name}",
            async ct =>
            {
                var result = await (plan.Kind switch
                {
                    UninstallKind.PackageManager => _packageManager.UninstallAsync(application.PackageId!, plan.Silent, progress, ct),
                    _ => _uninstaller.RunAsync(plan.Command!, plan.Silent, ct),
                });
                return result.IsSuccess && application.Origin == ApplicationOrigin.Registry
                    ? await AwaitHandOffAsync(application, result, progress, ct)
                    : result;
            },
            cancellationToken);
    }

    /// <summary>
    /// Inno Setup and NSIS uninstallers copy themselves to %TEMP%, relaunch from there and exit at
    /// once (QA, 0.3.2): the app was still listed and the panel hid when the real uninstaller window
    /// took the focus. While the entry is still registered and something runs from %TEMP%, keep the
    /// operation open; the queue stays held, so nothing else starts meanwhile.
    /// </summary>
    private async Task<OperationResult> AwaitHandOffAsync(
        InstalledApplication application,
        OperationResult result,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        // ponytail: any process under %TEMP% counts as the hand-off, capped at 10 min; match the
        // uninstaller's own process tree if unrelated temp processes ever keep the queue waiting.
        var deadline = _time.GetUtcNow() + HandOffLimit;
        var waited = false;
        while (HandOffRunning() && _time.GetUtcNow() < deadline)
        {
            if (!await StillRegisteredAsync(application.Key, cancellationToken))
            {
                return result;
            }

            if (!waited)
            {
                progress?.Report("Waiting for the uninstaller window to finish…");
                waited = true;
            }

            await Task.Delay(HandOffPoll, _time, cancellationToken);
        }

        return waited && await StillRegisteredAsync(application.Key, cancellationToken)
            ? OperationResult.Failure($"{application.Name} is still installed: its uninstaller did not finish (closed, or still open after 10 minutes).")
            : result;
    }

    private async Task<bool> StillRegisteredAsync(ApplicationKey key, CancellationToken cancellationToken) =>
        (await _registry.GetInstalledApplicationsAsync(cancellationToken)).Any(a => a.Key == key);

    private bool HandOffRunning()
    {
        var temp = ApplicationRoots.NormalizePath(Path.GetTempPath(), isDirectory: true);
        return temp is not null && _system.GetProcesses()
            .Select(p => ApplicationRoots.NormalizePath(p.ImagePath))
            .Any(path => path is not null && path.StartsWith(temp, StringComparison.Ordinal));
    }

    private static async Task<BatchResult> RunBatchAsync<TItem>(
        IReadOnlyList<TItem> items,
        Func<TItem, string> nameOf,
        Func<TItem, IProgress<string>, CancellationToken, Task<OperationResult>> run,
        IProgress<BatchProgress>? progress,
        CancellationToken cancellationToken)
    {
        var results = new List<BatchItemResult>(items.Count);
        var stopped = false;
        for (var i = 0; i < items.Count; i++)
        {
            var name = nameOf(items[i]);
            if (stopped || cancellationToken.IsCancellationRequested)
            {
                results.Add(new BatchItemResult(name, null));
                continue;
            }

            var position = i + 1;
            progress?.Report(new BatchProgress(position, items.Count, name));
            var lines = new Progress<string>(line => progress?.Report(new BatchProgress(position, items.Count, name, line)));

            OperationResult result;
            try
            {
                result = await run(items[i], lines, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                results.Add(new BatchItemResult(name, null));
                stopped = true;
                continue;
            }
            catch (PackageManagerUnavailableException ex)
            {
                // Every remaining item would fail the same way.
                results.Add(new BatchItemResult(name, OperationResult.Failure(ex.Message)));
                stopped = true;
                continue;
            }

            results.Add(new BatchItemResult(name, result));
            stopped = result.Status == OperationStatus.Cancelled;
        }

        return new BatchResult(results);
    }

    private async Task<OperationResult> RunExclusiveAsync(
        string description,
        Func<CancellationToken, Task<OperationResult>> operation,
        CancellationToken cancellationToken)
    {
        await _queue.WaitAsync(cancellationToken);
        OperationResult result;
        try
        {
            LogStarting(description);
            result = await operation(cancellationToken);
        }
        finally
        {
            // Even a failed installer may have changed something; never trust the cache after one ran.
            _inventory.Invalidate();
            _queue.Release();
        }

        LogFinished(description, result.Status, result.ExitCode);
        OperationCompleted?.Invoke(this, new OperationCompletedEventArgs(description, result));
        return result;
    }

    private static OperationResult NotInstalled(ApplicationKey key) =>
        OperationResult.Failure($"No installed application has the key '{key}'. Refresh the inventory and try again.");

    private static OperationResult TruncatedId(PackageId id) =>
        OperationResult.Failure($"The package id '{id}' was truncated by the console and cannot be used as-is.");

    [LoggerMessage(Level = LogLevel.Information, Message = "{Description}: starting")]
    private partial void LogStarting(string description);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Description}: {Status} (exit code {ExitCode})")]
    private partial void LogFinished(string description, OperationStatus status, int? exitCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Uninstalling {Name} via {Kind}, silent: {Silent}")]
    private partial void LogUninstallPlan(string name, UninstallKind kind, bool silent);
}
