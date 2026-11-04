using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Packages;

namespace SoftwareDock.Domain.Backup;

/// <summary>
/// What was installed on a machine, in a form that can rebuild it: packages a package manager can
/// reinstall, plus the applications it cannot, so the user at least knows what to fetch by hand.
/// </summary>
public sealed record SoftwareManifest(
    DateTimeOffset CreatedAt,
    string? MachineName,
    IReadOnlyList<ManifestPackage> Packages,
    IReadOnlyList<ManifestApplication> Unmanaged)
{
    /// <summary>
    /// Builds the manifest of <paramref name="applications"/>. A package is anything installed from
    /// a source (<c>winget</c>, <c>msstore</c>) under a usable id; everything else is unmanaged.
    /// </summary>
    public static SoftwareManifest From(IEnumerable<InstalledApplication> applications, DateTimeOffset createdAt, string? machineName)
    {
        ArgumentNullException.ThrowIfNull(applications);

        var packages = new List<ManifestPackage>();
        var unmanaged = new List<ManifestApplication>();
        foreach (var app in applications)
        {
            if (app is { IsManagedByPackageManager: true, PackageSource: not null } && !app.Key.Value.StartsWith(@"MSIX\", StringComparison.OrdinalIgnoreCase))
            {
                packages.Add(new ManifestPackage(app.PackageId!, app.Name, app.PackageSource, app.Version));
            }
            else
            {
                unmanaged.Add(new ManifestApplication(app.Name, app.Publisher, app.Version));
            }
        }

        return new SoftwareManifest(
            createdAt,
            machineName,
            packages.DistinctBy(p => p.Id).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            unmanaged.DistinctBy(u => u.Name, StringComparer.OrdinalIgnoreCase).OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>True when both describe the same software, whenever and wherever they were taken.</summary>
    public bool HasSameContentAs(SoftwareManifest? other) =>
        other is not null
        && Packages.Select(p => (p.Id, p.Source)).SequenceEqual(other.Packages.Select(p => (p.Id, p.Source)))
        && Unmanaged.Select(u => u.Name).SequenceEqual(other.Unmanaged.Select(u => u.Name), StringComparer.OrdinalIgnoreCase);
}

/// <summary>A reinstallable package. The version is informational: a restore installs the latest.</summary>
public sealed record ManifestPackage(PackageId Id, string Name, string? Source, string? Version);

/// <summary>Installed by hand; listed so nothing is forgotten.</summary>
public sealed record ManifestApplication(string Name, string? Publisher, string? Version);
