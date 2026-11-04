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
    private readonly ILogger<SoftwareOperations> _logger;
    private readonly SemaphoreSlim _queue = new(1, 1);

    public SoftwareOperations(
        ISoftwareInventory inventory,
        IPackageManager packageManager,
        IUninstallerLauncher uninstaller,
        ILogger<SoftwareOperations> logger)
    {
        _inventory = inventory;
        _packageManager = packageManager;
        _uninstaller = uninstaller;
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
        if (application is null)
        {
            return NotInstalled(key);
        }

        var plan = UninstallPlan.For(application, mode);
        if (plan is null)
        {
            return OperationResult.Failure($"{application.Name} has no registered uninstaller and no package manager knows it.");
        }

        LogUninstallPlan(application.Name, plan.Kind, plan.Silent);
        return await RunExclusiveAsync(
            $"Uninstall {application.Name}",
            ct => plan.Kind switch
            {
                UninstallKind.PackageManager => _packageManager.UninstallAsync(application.PackageId!, plan.Silent, progress, ct),
                _ => _uninstaller.RunAsync(plan.Command!, plan.Silent, ct),
            },
            cancellationToken);
    }

    public void Dispose() => _queue.Dispose();

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
