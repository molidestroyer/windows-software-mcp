using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Monitoring;

namespace SoftwareDock.Domain.Tests;

public class ResourceAttributionTests
{
    private static InstalledApplication App(string key, string? installLocation = null, string? icon = null) => new()
    {
        Key = ApplicationKey.Parse(key),
        Name = key,
        Origin = ApplicationOrigin.Registry,
        InstallLocation = installLocation,
        IconPath = icon,
    };

    private static ProcessSample P(int pid, string? path, double cpuSeconds = 0, long memory = 100) =>
        new(pid, path, TimeSpan.FromSeconds(cpuSeconds), memory);

    [Fact]
    public void ProcessesUnderTheInstallFolder_AreAttributedAndSummed()
    {
        var git = App("git", @"C:\Program Files\Git");

        var usage = ResourceAttribution.Attribute(
            [git], null,
            [P(1, @"C:\Program Files\Git\bin\bash.exe", memory: 300), P(2, @"c:\program files\git\usr\bin\ssh.exe", memory: 200), P(3, @"C:\Other\x.exe")],
            TimeSpan.FromSeconds(2), 4);

        Assert.Equal(new ResourceUsage(2, 0, 500), usage[git.Key]);
    }

    [Fact]
    public void Cpu_IsTheProcessorTimeGainedOverWallClockAndCores()
    {
        var app = App("app", @"C:\Apps\Tool\");

        var usage = ResourceAttribution.Attribute(
            [app],
            [P(7, @"C:\Apps\Tool\tool.exe", cpuSeconds: 10)],
            [P(7, @"C:\Apps\Tool\tool.exe", cpuSeconds: 12)],
            TimeSpan.FromSeconds(2), 4);

        // 2 s of CPU over 2 s × 4 cores = 25 % of the machine.
        Assert.Equal(25, usage[app.Key].CpuPercent, 3);
    }

    [Fact]
    public void ReusedProcessId_WithAnotherImage_DoesNotProduceCpu()
    {
        var app = App("app", @"C:\Apps\Tool");

        var usage = ResourceAttribution.Attribute(
            [app],
            [P(7, @"C:\Apps\Other\old.exe", cpuSeconds: 1)],
            [P(7, @"C:\Apps\Tool\tool.exe", cpuSeconds: 50)],
            TimeSpan.FromSeconds(2), 4);

        Assert.Equal(0, usage[app.Key].CpuPercent);
    }

    [Fact]
    public void DeepestInstallFolder_Wins()
    {
        var suite = App("suite", @"C:\Vendor\Suite");
        var plugin = App("plugin", @"C:\Vendor\Suite\Plugins\Thing");

        var usage = ResourceAttribution.Attribute(
            [suite, plugin], null,
            [P(1, @"C:\Vendor\Suite\suite.exe"), P(2, @"C:\Vendor\Suite\Plugins\Thing\thing.exe")],
            TimeSpan.FromSeconds(1), 1);

        Assert.Equal(1, usage[suite.Key].ProcessCount);
        Assert.Equal(1, usage[plugin.Key].ProcessCount);
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Program Files (x86)\")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\Users\miguel\AppData\Local")]
    public void TooGenericFolders_AreNeverRoots(string location)
    {
        var greedy = App("greedy", location);

        var usage = ResourceAttribution.Attribute([greedy], null, [P(1, @"C:\Program Files\Anything\a.exe")], TimeSpan.FromSeconds(1), 1);

        Assert.Empty(usage);
    }

    [Fact]
    public void WithoutInstallLocation_TheIconExecutableFolderIsUsed()
    {
        var app = App("code", icon: @"""C:\Users\m\AppData\Local\Programs\Microsoft VS Code\Code.exe"",0");

        var usage = ResourceAttribution.Attribute([app], null, [P(1, @"C:\Users\m\AppData\Local\Programs\Microsoft VS Code\Code.exe")], TimeSpan.FromSeconds(1), 1);

        Assert.True(usage[app.Key].IsRunning);
    }

    [Fact]
    public void IconOutsideAnExe_IsNotARoot()
    {
        Assert.Null(ApplicationRoots.RootOf(App("x", icon: @"C:\Windows\Installer\{GUID}\icon.ico")));
    }

    [Fact]
    public void PrefixOfAnotherFolderName_DoesNotMatch()
    {
        var app = App("app", @"C:\Apps\Tool");

        var usage = ResourceAttribution.Attribute([app], null, [P(1, @"C:\Apps\Toolbox\box.exe")], TimeSpan.FromSeconds(1), 1);

        Assert.Empty(usage);
    }
}
