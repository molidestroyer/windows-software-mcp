using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Operations;

namespace SoftwareDock.Application.Abstractions;

/// <summary>A folder an uninstalled application left behind.</summary>
public sealed record LeftoverFolder(string Path, long SizeBytes);

/// <summary>Port: finds what an uninstalled application left on disk and sends it to the Recycle Bin.</summary>
public interface ILeftoverCleaner
{
    Task<IReadOnlyList<LeftoverFolder>> FindAsync(InstalledApplication application, CancellationToken cancellationToken);

    /// <summary>Moves the folders to the Recycle Bin — never deletes outright, so any mistake can be undone.</summary>
    Task<OperationResult> RecycleAsync(IReadOnlyList<LeftoverFolder> folders, CancellationToken cancellationToken);
}
