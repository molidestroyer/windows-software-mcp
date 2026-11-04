using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Usage;

namespace SoftwareDock.Domain.Tests;

public class UsageAttributionTests
{
    [Fact]
    public void LatestUseOfAnyExecutable_AndTotalRuns()
    {
        var code = new InstalledApplication
        {
            Key = ApplicationKey.Parse("code"),
            Name = "VS Code",
            Origin = ApplicationOrigin.Registry,
            InstallLocation = @"C:\Users\m\AppData\Local\Programs\Microsoft VS Code",
        };
        var older = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var newer = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        var usage = UsageAttribution.Attribute(
            [code],
            [
                new UsageRecord(@"C:\Users\m\AppData\Local\Programs\Microsoft VS Code\Code.exe", older, 10),
                new UsageRecord(@"C:\Users\m\AppData\Local\Programs\Microsoft VS Code\bin\code-tunnel.exe", newer, 2),
                new UsageRecord(@"C:\Windows\notepad.exe", newer, 99),
            ]);

        Assert.Equal(new ApplicationUsage(newer, 12), usage[code.Key]);
        Assert.Single(usage);
    }
}
