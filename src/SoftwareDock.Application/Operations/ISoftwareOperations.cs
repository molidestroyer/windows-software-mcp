using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Operations;
using SoftwareDock.Domain.Packages;

namespace SoftwareDock.Application.Operations;

/// <summary>Use case: change what is installed.</summary>
/// <remarks>
/// Operations run one at a time. Installers share global state (the Windows Installer mutex,
/// WinGet's own lock) and two at once fail in ways that are hard to explain to anyone.
/// </remarks>
public interface ISoftwareOperations
{
    /// <summary>Raised when an operation finishes, successfully or not.</summary>
    event EventHandler<OperationCompletedEventArgs>? OperationCompleted;

    Task<OperationResult> InstallAsync(InstallRequest request, IProgress<string>? progress, CancellationToken cancellationToken);

    Task<OperationResult> UpgradeAsync(ApplicationKey key, IProgress<string>? progress, CancellationToken cancellationToken);

    Task<OperationResult> UninstallAsync(ApplicationKey key, UninstallMode mode, IProgress<string>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Removes several applications, one after another — something Windows itself cannot do.
    /// A failure moves on to the next item; a cancellation (the user dismissed a UAC prompt or an
    /// uninstaller) stops the batch, because that is how people say "stop".
    /// </summary>
    Task<BatchResult> UninstallManyAsync(IReadOnlyList<ApplicationKey> keys, UninstallMode mode, IProgress<BatchProgress>? progress, CancellationToken cancellationToken);

    /// <summary>Installs several packages one after another, with the same failure rules as <see cref="UninstallManyAsync"/>.</summary>
    Task<BatchResult> InstallManyAsync(IReadOnlyList<InstallRequest> requests, IProgress<BatchProgress>? progress, CancellationToken cancellationToken);
}

public sealed class OperationCompletedEventArgs(string description, OperationResult result) : EventArgs
{
    public string Description { get; } = description;

    public OperationResult Result { get; } = result;
}
