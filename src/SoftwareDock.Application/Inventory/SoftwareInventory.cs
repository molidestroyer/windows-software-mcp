using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoftwareDock.Application.Abstractions;
using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Inventory;
using SoftwareDock.Domain.Packages;

namespace SoftwareDock.Application.Inventory;

/// <summary>
/// Builds inventory snapshots from the registry and the package manager, and caches them.
/// </summary>
/// <remarks>
/// The two sources are read in parallel and fail independently: with WinGet missing the dock
/// still lists (and can uninstall) everything in the registry, and says why updates are absent.
/// </remarks>
public sealed partial class SoftwareInventory : ISoftwareInventory, IDisposable
{
    private readonly IInstalledApplicationSource _registry;
    private readonly IPackageManager _packageManager;
    private readonly TimeProvider _time;
    private readonly IOptions<InventoryOptions> _options;
    private readonly ILogger<SoftwareInventory> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private InventorySnapshot? _current;
    private bool _stale = true;
    private int _generation;

    public SoftwareInventory(
        IInstalledApplicationSource registry,
        IPackageManager packageManager,
        TimeProvider time,
        IOptions<InventoryOptions> options,
        ILogger<SoftwareInventory> logger)
    {
        _registry = registry;
        _packageManager = packageManager;
        _time = time;
        _options = options;
        _logger = logger;
    }

    public event EventHandler<InventorySnapshot>? SnapshotChanged;

    public async Task<InventorySnapshot> GetSnapshotAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && TryGetFresh(out var cached))
        {
            return cached;
        }

        var generationSeen = Volatile.Read(ref _generation);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Another caller rebuilt it while this one waited: theirs started after our request.
            if (_generation != generationSeen && _current is { } latest && !_stale)
            {
                return latest;
            }

            if (!forceRefresh && TryGetFresh(out cached))
            {
                return cached;
            }

            var snapshot = await BuildAsync(cancellationToken);
            _current = snapshot;
            _stale = false;
            Interlocked.Increment(ref _generation);
            SnapshotChanged?.Invoke(this, snapshot);
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate() => Volatile.Write(ref _stale, true);

    public async Task<InstalledApplication?> FindAsync(ApplicationKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        var snapshot = await GetSnapshotAsync(forceRefresh: false, cancellationToken);
        return snapshot.Find(key);
    }

    public void Dispose() => _gate.Dispose();

    private bool TryGetFresh(out InventorySnapshot snapshot)
    {
        snapshot = _current!;
        return _current is not null
            && !_stale
            && _time.GetUtcNow() - _current.TakenAt < _options.Value.CacheDuration;
    }

    private async Task<InventorySnapshot> BuildAsync(CancellationToken cancellationToken)
    {
        var started = _time.GetTimestamp();
        var registryTask = ReadAsync("registry", _registry.GetInstalledApplicationsAsync, cancellationToken);
        var packagesTask = ReadAsync("package manager", _packageManager.ListInstalledAsync, cancellationToken);
        await Task.WhenAll(registryTask, packagesTask);

        var (registryEntries, registryWarning) = await registryTask;
        var (packages, packageWarning) = await packagesTask;

        var applications = InventoryReconciler.Reconcile(
            registryEntries ?? [],
            packages ?? (IReadOnlyList<InstalledPackage>)[]);

        var warnings = new[] { registryWarning, packageWarning }.OfType<string>().ToList();
        LogSnapshotBuilt(applications.Count, warnings.Count, _time.GetElapsedTime(started).TotalMilliseconds);
        return new InventorySnapshot(applications, _time.GetUtcNow(), warnings);
    }

    private async Task<(T? Value, string? Warning)> ReadAsync<T>(
        string sourceName,
        Func<CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return (await read(cancellationToken), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSourceFailed(ex, sourceName);
            return (null, $"Could not read the {sourceName}: {ex.Message}");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Inventory built: {Count} applications, {WarningCount} warnings, {ElapsedMs:F0} ms")]
    private partial void LogSnapshotBuilt(int count, int warningCount, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inventory source '{Source}' failed; the snapshot will be partial")]
    private partial void LogSourceFailed(Exception exception, string source);
}
