using SoftwareDock.Domain.Applications;

namespace SoftwareDock.Domain.Monitoring;

/// <summary>
/// Attributes running processes to installed applications by where their executable lives
/// (<see cref="ApplicationRoots"/>).
/// </summary>
/// <remarks>
/// Windows has no link between a process and the "Apps &amp; features" entry that installed it; the
/// install folder is the best evidence available. CPU is the processor time each process gained
/// since the previous sample, over wall-clock time and all cores. A process seen for the first time
/// contributes memory but no CPU yet.
/// </remarks>
public static class ResourceAttribution
{
    public static IReadOnlyDictionary<ApplicationKey, ResourceUsage> Attribute(
        IEnumerable<InstalledApplication> applications,
        IReadOnlyList<ProcessSample>? previous,
        IReadOnlyList<ProcessSample> current,
        TimeSpan elapsed,
        int processorCount)
    {
        ArgumentNullException.ThrowIfNull(applications);
        ArgumentNullException.ThrowIfNull(current);

        var ownerOf = ApplicationRoots.Lookup(applications);
        var before = previous?.ToDictionary(p => p.ProcessId) ?? [];
        var capacity = elapsed.TotalSeconds * Math.Max(1, processorCount);

        var totals = new Dictionary<ApplicationKey, (int Count, double Cpu, long Memory)>();
        foreach (var process in current)
        {
            if (ownerOf(process.ImagePath) is not { } owner)
            {
                continue;
            }

            var cpu = 0d;
            if (capacity > 0
                && before.TryGetValue(process.ProcessId, out var earlier)
                && string.Equals(ApplicationRoots.NormalizePath(earlier.ImagePath), ApplicationRoots.NormalizePath(process.ImagePath), StringComparison.Ordinal))
            {
                cpu = Math.Max(0, (process.ProcessorTime - earlier.ProcessorTime).TotalSeconds) / capacity * 100;
            }

            totals.TryGetValue(owner, out var sum);
            totals[owner] = (sum.Count + 1, sum.Cpu + cpu, sum.Memory + process.WorkingSetBytes);
        }

        return totals.ToDictionary(t => t.Key, t => new ResourceUsage(t.Value.Count, Math.Min(100, t.Value.Cpu), t.Value.Memory));
    }
}
