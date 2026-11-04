using SoftwareDock.Domain.Operations;
using SoftwareDock.Domain.Packages;

namespace SoftwareDock.Application.Abstractions;

/// <summary>
/// Port: a package manager able to list, find, install, upgrade and remove software.
/// </summary>
/// <remarks>
/// Implemented today over the WinGet CLI. The port is shaped so the WinGet COM API
/// (<c>Microsoft.Management.Deployment</c>) can replace it without touching a use case.
/// Implementations report failures as <see cref="OperationResult"/> values; they throw only for
/// cancellation and for a package manager that is not installed at all.
/// </remarks>
public interface IPackageManager
{
    Task<IReadOnlyList<InstalledPackage>> ListInstalledAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PackageSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken);

    Task<OperationResult> InstallAsync(InstallRequest request, IProgress<string>? progress, CancellationToken cancellationToken);

    Task<OperationResult> UpgradeAsync(PackageId id, IProgress<string>? progress, CancellationToken cancellationToken);

    Task<OperationResult> UninstallAsync(PackageId id, bool silent, IProgress<string>? progress, CancellationToken cancellationToken);
}
