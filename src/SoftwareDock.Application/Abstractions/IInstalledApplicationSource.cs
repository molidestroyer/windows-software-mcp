using SoftwareDock.Domain.Applications;

namespace SoftwareDock.Application.Abstractions;

/// <summary>Port: reads the "Apps &amp; features" entries of this machine.</summary>
public interface IInstalledApplicationSource
{
    Task<IReadOnlyList<InstalledApplication>> GetInstalledApplicationsAsync(CancellationToken cancellationToken);
}
