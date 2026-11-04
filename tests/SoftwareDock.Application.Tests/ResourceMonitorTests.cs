using Microsoft.Extensions.Time.Testing;
using SoftwareDock.Application.Abstractions;
using SoftwareDock.Application.Monitoring;
using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Monitoring;

namespace SoftwareDock.Application.Tests;

public class ResourceMonitorTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly FakeSystem _system = new();

    private static readonly InstalledApplication Tool = new()
    {
        Key = ApplicationKey.Parse("tool"),
        Name = "Tool",
        Origin = ApplicationOrigin.Registry,
        InstallLocation = @"C:\Apps\Tool",
    };

    [Fact]
    public void FirstSample_HasMemoryButNoCpu_SecondHasBoth()
    {
        var sut = new ResourceMonitor(_system, _time);
        _system.Processes = [new(1, @"C:\Apps\Tool\tool.exe", TimeSpan.FromSeconds(10), 512), new(2, @"C:\Windows\x.exe", TimeSpan.FromSeconds(5), 1)];

        var first = sut.Sample([Tool]);
        _time.Advance(TimeSpan.FromSeconds(2));
        _system.Processes = [new(1, @"C:\Apps\Tool\tool.exe", TimeSpan.FromSeconds(11), 600), new(2, @"C:\Windows\x.exe", TimeSpan.FromSeconds(8), 1)];
        var second = sut.Sample([Tool]);

        Assert.Equal(new ResourceUsage(1, 0, 512), first.For(Tool.Key));
        Assert.Equal(12.5, second.For(Tool.Key).CpuPercent, 3);       // 1 s over 2 s × 4 cores
        Assert.Equal(50, second.SystemCpuPercent, 3);                  // 4 s over 2 s × 4 cores
        Assert.Equal(600, second.ApplicationsMemoryBytes);
        Assert.Equal(1, second.RunningApplications);
    }

    [Fact]
    public void Reset_DropsTheBaseline()
    {
        var sut = new ResourceMonitor(_system, _time);
        _system.Processes = [new(1, @"C:\Apps\Tool\tool.exe", TimeSpan.FromSeconds(10), 512)];
        sut.Sample([Tool]);
        sut.Reset();
        _time.Advance(TimeSpan.FromSeconds(2));
        _system.Processes = [new(1, @"C:\Apps\Tool\tool.exe", TimeSpan.FromSeconds(20), 512)];

        Assert.Equal(0, sut.Sample([Tool]).For(Tool.Key).CpuPercent);
    }

    private sealed class FakeSystem : ISystemMonitor
    {
        public IReadOnlyList<ProcessSample> Processes { get; set; } = [];

        public int ProcessorCount => 4;

        public IReadOnlyList<ProcessSample> GetProcesses() => Processes;

        public MemoryStatus GetMemoryStatus() => new(16L << 30, 8L << 30);
    }
}
