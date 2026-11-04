using SoftwareDock.Domain.Operations;
using SoftwareDock.Domain.Startup;

namespace SoftwareDock.Application.Abstractions;

/// <summary>Port: the programs Windows starts at sign-in, and Task Manager's switch for each.</summary>
public interface IStartupItemSource
{
    Task<IReadOnlyList<StartupItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Enables or disables without deleting anything, exactly as Task Manager does.</summary>
    Task<OperationResult> SetEnabledAsync(StartupItem item, bool enabled, CancellationToken cancellationToken);
}
