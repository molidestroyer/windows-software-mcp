using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Inventory;

namespace SoftwareDock.Domain.Backup;

/// <summary>What a backup would change on this machine.</summary>
/// <param name="ToInstall">Packages in the backup that are not installed here.</param>
/// <param name="AlreadyInstalled">Packages already present — nothing to do.</param>
/// <param name="InstallManually">Unmanaged applications in the backup that are not installed here.</param>
public sealed record RestorePlan(
    IReadOnlyList<ManifestPackage> ToInstall,
    IReadOnlyList<ManifestPackage> AlreadyInstalled,
    IReadOnlyList<ManifestApplication> InstallManually)
{
    public static RestorePlan Create(SoftwareManifest manifest, IReadOnlyList<InstalledApplication> installed)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(installed);

        var installedIds = installed.Select(a => a.PackageId).OfType<Packages.PackageId>().ToHashSet();
        var toInstall = manifest.Packages.Where(p => !installedIds.Contains(p.Id)).ToList();
        var present = manifest.Packages.Where(p => installedIds.Contains(p.Id)).ToList();
        var manual = manifest.Unmanaged
            .Where(u => !installed.Any(a => InventoryReconciler.NamesMatch(u.Name, a.Name)))
            .ToList();

        return new RestorePlan(toInstall, present, manual);
    }
}
