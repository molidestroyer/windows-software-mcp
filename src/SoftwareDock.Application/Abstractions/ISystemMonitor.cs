using SoftwareDock.Domain.Monitoring;

namespace SoftwareDock.Application.Abstractions;

/// <summary>Port: what is running on this machine and how much memory it has.</summary>
public interface ISystemMonitor
{
    int ProcessorCount { get; }

    /// <summary>All processes visible to the current user. Ones that cannot be opened have no image path.</summary>
    IReadOnlyList<ProcessSample> GetProcesses();

    MemoryStatus GetMemoryStatus();
}
