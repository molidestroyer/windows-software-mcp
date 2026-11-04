using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Updates;

namespace SoftwareDock.Domain.Tests;

public class UpdateRulesAndScheduleTests
{
    private static readonly InstalledApplication Git = new()
    {
        Key = ApplicationKey.Parse(@"ARP\Machine\X64\Git_is1"),
        Name = "Git",
        Origin = ApplicationOrigin.Registry,
        AvailableVersion = "2.47.1",
    };

    [Fact]
    public void Pinned_IsNeverActionable_UntilUnpinned()
    {
        var rules = UpdateRules.None.Pin(Git.Key);

        Assert.False(rules.IsActionable(Git));
        Assert.True(rules.Unpin(Git.Key).IsActionable(Git));
    }

    [Fact]
    public void SkippedVersion_HidesOnlyThatVersion()
    {
        var rules = UpdateRules.None.Skip(Git.Key, "2.47.1");

        Assert.False(rules.IsActionable(Git));
        Assert.True(rules.IsActionable(Git with { AvailableVersion = "2.48.0" }));
    }

    [Fact]
    public void Rules_AreCaseInsensitiveOnKeys()
    {
        var rules = UpdateRules.None.Pin(ApplicationKey.Parse(@"arp\machine\x64\git_is1"));

        Assert.True(rules.IsPinned(Git));
    }

    [Fact]
    public void Daily_NextRunIsTonightOrTomorrow()
    {
        var schedule = new UpdateSchedule(UpdateScheduleMode.Daily, Hour: 3);

        Assert.Equal(new DateTime(2026, 11, 5, 3, 0, 0), schedule.NextRun(null, new DateTime(2026, 11, 4, 22, 0, 0)));
        Assert.Equal(new DateTime(2026, 11, 4, 3, 0, 0), schedule.NextRun(null, new DateTime(2026, 11, 4, 1, 0, 0)));
    }

    [Fact]
    public void Weekly_LandsOnTheConfiguredDay()
    {
        var schedule = new UpdateSchedule(UpdateScheduleMode.Weekly, Hour: 3, Day: DayOfWeek.Sunday);

        // Wednesday 4 Nov 2026 → Sunday 8 Nov.
        Assert.Equal(new DateTime(2026, 11, 8, 3, 0, 0), schedule.NextRun(null, new DateTime(2026, 11, 4, 12, 0, 0)));
    }

    [Fact]
    public void ARunMissedWhileAsleep_IsDueOnWake()
    {
        var schedule = new UpdateSchedule(UpdateScheduleMode.Daily, Hour: 3);
        var lastRun = new DateTime(2026, 11, 3, 3, 0, 5);

        Assert.True(schedule.IsDue(lastRun, new DateTime(2026, 11, 4, 8, 30, 0)));
        Assert.False(schedule.IsDue(new DateTime(2026, 11, 4, 3, 0, 5), new DateTime(2026, 11, 4, 8, 30, 0)));
    }

    [Fact]
    public void Off_IsNeverDue() => Assert.False(UpdateSchedule.Off.IsDue(null, DateTime.Now));
}
