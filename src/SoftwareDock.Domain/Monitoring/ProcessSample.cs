namespace SoftwareDock.Domain.Monitoring;

/// <summary>One running process at one instant.</summary>
/// <param name="ProcessId">Process id. Ids are reused, so samples are only compared within one tick.</param>
/// <param name="ImagePath">Full path of the executable, when the process could be opened.</param>
/// <param name="ProcessorTime">Total kernel + user CPU time consumed since the process started.</param>
/// <param name="WorkingSetBytes">Physical memory in use.</param>
public sealed record ProcessSample(int ProcessId, string? ImagePath, TimeSpan ProcessorTime, long WorkingSetBytes);

/// <summary>Physical memory of the machine.</summary>
public sealed record MemoryStatus(long TotalBytes, long AvailableBytes)
{
    public long UsedBytes => Math.Max(0, TotalBytes - AvailableBytes);

    public double UsedFraction => TotalBytes > 0 ? (double)UsedBytes / TotalBytes : 0;
}

/// <summary>What one application's processes use right now.</summary>
/// <param name="ProcessCount">Processes running from the application's folder.</param>
/// <param name="CpuPercent">Share of the whole machine (all cores = 100 %).</param>
/// <param name="MemoryBytes">Sum of working sets.</param>
public sealed record ResourceUsage(int ProcessCount, double CpuPercent, long MemoryBytes)
{
    public static readonly ResourceUsage None = new(0, 0, 0);

    public bool IsRunning => ProcessCount > 0;
}
