using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Operations;

namespace SoftwareDock.Application.Abstractions;

/// <summary>
/// Port: runs an application's own registered uninstaller — the fallback for software no
/// package manager can remove.
/// </summary>
public interface IUninstallerLauncher
{
    Task<OperationResult> RunAsync(UninstallCommand command, bool silent, CancellationToken cancellationToken);
}
