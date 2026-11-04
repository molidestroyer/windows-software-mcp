using SoftwareDock.App.Services;
using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Security;
using SoftwareDock.Domain.Updates;

namespace SoftwareDock.App.Tests;

public class UpdateAnnouncerTests
{
    private static InstalledApplication App(string name, string? available) => new()
    {
        Key = ApplicationKey.Parse(name),
        Name = name,
        Origin = ApplicationOrigin.Registry,
        AvailableVersion = available,
    };

    private static readonly IReadOnlyDictionary<ApplicationKey, SecurityAdvisory> NoAdvisories = new Dictionary<ApplicationKey, SecurityAdvisory>();

    [Fact]
    public void EachVersionIsAnnouncedOnce()
    {
        var apps = new[] { App("Git", "2.47"), App("VLC", "3.1") };

        var (first, announced) = UpdateAnnouncer.Announce(apps, UpdateRules.None, NoAdvisories, new Dictionary<string, string>());
        var (second, _) = UpdateAnnouncer.Announce(apps, UpdateRules.None, NoAdvisories, announced);
        var (third, _) = UpdateAnnouncer.Announce([App("Git", "2.48"), App("VLC", "3.1")], UpdateRules.None, NoAdvisories, announced);

        Assert.Equal("2 updates available", first!.Title);
        Assert.Null(second);
        Assert.Equal("Update available: Git", third!.Title);
    }

    [Fact]
    public void SecurityUpdatesLeadTheNotification()
    {
        var chrome = App("Google Chrome", "131");
        var advisories = new Dictionary<ApplicationKey, SecurityAdvisory>
        {
            [chrome.Key] = new([new("CVE-2026-1", "Google", "Chromium V8", "Type confusion", new DateOnly(2026, 10, 1), false)]),
        };

        var (notification, _) = UpdateAnnouncer.Announce([App("Git", "2.47"), chrome], UpdateRules.None, advisories, new Dictionary<string, string>());

        Assert.Equal("Security update: Google Chrome", notification!.Title);
        Assert.Equal("CVE-2026-1 is being exploited in the wild. Update to 131.", notification.Body);
        Assert.Equal(NotificationTarget.Updates, notification.Target);
    }

    [Fact]
    public void PinnedAndSkippedAreNeverAnnounced()
    {
        var git = App("Git", "2.47");
        var vlc = App("VLC", "3.1");
        var rules = UpdateRules.None.Pin(git.Key).Skip(vlc.Key, "3.1");

        var (notification, announced) = UpdateAnnouncer.Announce([git, vlc], rules, NoAdvisories, new Dictionary<string, string>());

        Assert.Null(notification);
        Assert.Empty(announced);
    }

    [Fact]
    public void ManyUpdates_AreSummarized()
    {
        var apps = Enumerable.Range(1, 5).Select(i => App($"App{i}", "2.0")).ToArray();

        var (notification, _) = UpdateAnnouncer.Announce(apps, UpdateRules.None, NoAdvisories, new Dictionary<string, string>());

        Assert.Equal("App1, App2, App3 and 2 more", notification!.Body);
    }
}
