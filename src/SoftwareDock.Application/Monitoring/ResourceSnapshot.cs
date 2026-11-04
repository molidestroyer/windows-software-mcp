using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Monitoring;

namespace SoftwareDock.Application.Monitoring;

/// <summary>Resource usage of installed applications and of the machine, at one tick.</summary>
/// <param name="PerApplication">Only applications with at least one running process.</param>
/// <param name="SystemCpuPercent">CPU used by every process, all cores = 100 %.</param>
/// <param name="Memory">Physical memory of the machine.</param>
public sealed record ResourceSnapshot(
    IReadOnlyDictionary<ApplicationKey, ResourceUsage> PerApplication,
    double SystemCpuPercent,
    MemoryStatus Memory)
{
    public static readonly ResourceSnapshot Empty = new(new Dictionary<ApplicationKey, ResourceUsage>(), 0, new MemoryStatus(0, 0));

    public double ApplicationsCpuPercent => PerApplication.Values.Sum(u => u.CpuPercent);

    public long ApplicationsMemoryBytes => PerApplication.Values.Sum(u => u.MemoryBytes);

    public int RunningApplications => PerApplication.Count;

    public ResourceUsage For(ApplicationKey key) => PerApplication.GetValueOrDefault(key, ResourceUsage.None);
}
