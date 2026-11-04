using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Usage;

namespace SoftwareDock.Domain.Tests;

public class UnusedApplicationsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private static InstalledApplication App(string name) => new() { Key = ApplicationKey.Parse(name), Name = name, Origin = ApplicationOrigin.Registry };

    private static ApplicationUsage DaysAgo(int days) => new(Now.AddDays(-days), 1);

    [Fact]
    public void OldLaunch_IsUnused_RecentIsNot()
    {
        var steam = App("Steam");
        var code = App("VS Code");

        var unused = UnusedApplications.Find([steam, code], new Dictionary<ApplicationKey, ApplicationUsage> { [steam.Key] = DaysAgo(240), [code.Key] = DaysAgo(1) }, Now, new HashSet<ApplicationKey>());

        Assert.Equal([steam.Key], unused.Keys);
    }

    // QA, 0.3.0: Ollama was "170 days unused" while running from a Startup shortcut.
    [Fact]
    public void RunningOrStartupApps_AreNeverUnused()
    {
        var ollama = App("Ollama");

        var unused = UnusedApplications.Find([ollama], new Dictionary<ApplicationKey, ApplicationUsage> { [ollama.Key] = DaysAgo(170) }, Now, new HashSet<ApplicationKey> { ollama.Key });

        Assert.Empty(unused);
    }

    [Theory]
    [InlineData("Microsoft Edge")]
    [InlineData("Microsoft Edge WebView2 Runtime")]
    [InlineData("Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.44.35211")]
    public void PartsOfWindows_AreNeverUnused(string name)
    {
        var app = App(name);

        Assert.Empty(UnusedApplications.Find([app], new Dictionary<ApplicationKey, ApplicationUsage> { [app.Key] = DaysAgo(278) }, Now, new HashSet<ApplicationKey>()));
    }

    [Fact]
    public void NoLaunchHistory_IsUnknownNotUnused()
    {
        Assert.Empty(UnusedApplications.Find([App("Tool")], new Dictionary<ApplicationKey, ApplicationUsage>(), Now, new HashSet<ApplicationKey>()));
    }
}
