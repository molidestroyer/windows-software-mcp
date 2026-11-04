using SoftwareDock.Domain.Applications;

namespace SoftwareDock.Application.Inventory;

/// <summary>Use case: what is installed, and what can be updated.</summary>
public interface ISoftwareInventory
{
    /// <summary>Raised after a new snapshot replaces the previous one.</summary>
    event EventHandler<InventorySnapshot>? SnapshotChanged;

    /// <summary>
    /// The current snapshot. A cached one is returned while it is fresh; <paramref name="forceRefresh"/>
    /// rebuilds it. Concurrent callers share one rebuild.
    /// </summary>
    Task<InventorySnapshot> GetSnapshotAsync(bool forceRefresh, CancellationToken cancellationToken);

    /// <summary>Marks the cache stale, e.g. after something was installed.</summary>
    void Invalidate();

    Task<InstalledApplication?> FindAsync(ApplicationKey key, CancellationToken cancellationToken);
}
