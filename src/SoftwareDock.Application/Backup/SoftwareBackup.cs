using SoftwareDock.Application.Inventory;
using SoftwareDock.Domain.Backup;

namespace SoftwareDock.Application.Backup;

/// <summary>Use case: capture what is installed, and work out what a backup would restore.</summary>
public sealed class SoftwareBackup(ISoftwareInventory inventory, TimeProvider time)
{
    public async Task<SoftwareManifest> CaptureAsync(CancellationToken cancellationToken)
    {
        var snapshot = await inventory.GetSnapshotAsync(forceRefresh: false, cancellationToken);
        return SoftwareManifest.From(snapshot.Applications, time.GetUtcNow(), Environment.MachineName);
    }

    public async Task<RestorePlan> PlanRestoreAsync(SoftwareManifest manifest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var snapshot = await inventory.GetSnapshotAsync(forceRefresh: true, cancellationToken);
        return RestorePlan.Create(manifest, snapshot.Applications);
    }
}
