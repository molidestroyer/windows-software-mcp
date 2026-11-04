using SoftwareDock.Application.Abstractions;
using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Monitoring;

namespace SoftwareDock.Application.Monitoring;

/// <summary>
/// Samples running processes and attributes them to installed applications. CPU needs two samples,
/// so the first call reports memory only; call it on a steady interval (the dock uses 2 s while open).
/// </summary>
public sealed class ResourceMonitor(ISystemMonitor system, TimeProvider time)
{
    private readonly Lock _lock = new();
    private IReadOnlyList<ProcessSample>? _previous;
    private long _previousTimestamp;

    public ResourceSnapshot Sample(IReadOnlyList<InstalledApplication> applications)
    {
        ArgumentNullException.ThrowIfNull(applications);

        var current = system.GetProcesses();
        var now = time.GetTimestamp();

        IReadOnlyList<ProcessSample>? previous;
        TimeSpan elapsed;
        lock (_lock)
        {
            previous = _previous;
            elapsed = previous is null ? TimeSpan.Zero : time.GetElapsedTime(_previousTimestamp, now);
            _previous = current;
            _previousTimestamp = now;
        }

        var perApplication = ResourceAttribution.Attribute(applications, previous, current, elapsed, system.ProcessorCount);
        return new ResourceSnapshot(perApplication, SystemCpu(previous, current, elapsed), system.GetMemoryStatus());
    }

    /// <summary>Forgets the previous sample, e.g. when the panel closes, so a stale baseline never skews CPU.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _previous = null;
        }
    }

    private double SystemCpu(IReadOnlyList<ProcessSample>? previous, IReadOnlyList<ProcessSample> current, TimeSpan elapsed)
    {
        if (previous is null || elapsed <= TimeSpan.Zero)
        {
            return 0;
        }

        var before = previous.ToDictionary(p => p.ProcessId, p => p.ProcessorTime);
        var busy = current
            .Where(p => before.ContainsKey(p.ProcessId))
            .Sum(p => Math.Max(0, (p.ProcessorTime - before[p.ProcessId]).TotalSeconds));
        return Math.Clamp(busy / (elapsed.TotalSeconds * Math.Max(1, system.ProcessorCount)) * 100, 0, 100);
    }
}
