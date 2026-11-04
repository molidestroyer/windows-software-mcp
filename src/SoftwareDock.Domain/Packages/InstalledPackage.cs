namespace SoftwareDock.Domain.Packages;

/// <summary>One installed package as the package manager reports it.</summary>
/// <param name="Name">Display name; may end in <see cref="PackageId.TruncationMarker"/>.</param>
/// <param name="Id">Package id. Local-only packages carry their ARP/MSIX id.</param>
/// <param name="Version">Installed version, verbatim.</param>
/// <param name="AvailableVersion">A newer version in a source, if any.</param>
/// <param name="Source">The source that can update it (<c>winget</c>, <c>msstore</c>), or <see langword="null"/> for local-only packages.</param>
public sealed record InstalledPackage(
    string Name,
    PackageId Id,
    string? Version,
    string? AvailableVersion,
    string? Source);
