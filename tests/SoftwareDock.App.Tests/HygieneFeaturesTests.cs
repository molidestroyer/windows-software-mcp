using Microsoft.Extensions.DependencyInjection;
using SoftwareDock.App.Services;
using SoftwareDock.App.Settings;
using SoftwareDock.App.ViewModels;
using SoftwareDock.Domain.Updates;

namespace SoftwareDock.App.Tests;

public sealed class HygieneFeaturesTests : IDisposable
{
    private readonly ServiceProvider _services = TestServices.Create();
    private readonly DockViewModel _sut;

    public HygieneFeaturesTests() => _sut = _services.GetRequiredService<DockViewModel>();

    public void Dispose() => _services.Dispose();

    private ApplicationItemViewModel App(string name) => _sut.Applications.Single(a => a.Name == name);

    private async Task LoadWithInsightsAsync()
    {
        await _sut.LoadAsync();
        await _sut.RefreshInsightsAsync(_sut.Snapshot!);
    }

    [Fact]
    public async Task Pin_HidesTheUpdateEverywhere_UntilUnpinned()
    {
        await LoadWithInsightsAsync();

        App("Git").PinCommand.Execute(null);

        Assert.Equal(5, _sut.UpdateCount);
        Assert.True(App("Git").IsPinned);
        Assert.False(App("Git").ShowUpgrade);
        _sut.IsUpdatesSection = true;
        Assert.DoesNotContain(_sut.Applications, a => a.Name == "Git");
        Assert.Equal(["ARP\\MACHINE\\X64\\GIT.GIT_IS1"], _services.GetRequiredService<IUserSettingsStore>().Current.PinnedApplications);

        _sut.IsInstalledSection = true;
        App("Git").UnpinCommand.Execute(null);
        Assert.Equal(6, _sut.UpdateCount);
    }

    [Fact]
    public async Task SkipVersion_HidesOnlyThatVersion()
    {
        await LoadWithInsightsAsync();

        App("Git").SkipVersionCommand.Execute(null);

        Assert.Equal(5, _sut.UpdateCount);
        Assert.False(App("Git").HasUpdate);
        Assert.Equal("2.47.1", _services.GetRequiredService<IUserSettingsStore>().Current.UpdateRules.SkippedVersions.Single().Value);
    }

    [Fact]
    public async Task SecurityAdvisories_FlagExploitedAppsWithUpdates_AndGoFirstInUpdates()
    {
        await LoadWithInsightsAsync();

        Assert.Equal(2, _sut.SecurityUpdateCount);
        Assert.Equal("CVE-2026-31337 is being exploited in the wild", App("Notepad++ (64-bit x64)").SecurityText);
        Assert.Null(App("Microsoft PowerToys").SecurityText);
        Assert.Equal("6 updates · 2 under active attack", _sut.UpdateBanner);

        _sut.IsUpdatesSection = true;
        Assert.Equal(["Docker Desktop", "Notepad++ (64-bit x64)"], _sut.Applications.Take(2).Select(a => a.Name));
    }

    [Fact]
    public async Task SecurityAlertsOff_FlagsNothing()
    {
        _services.GetRequiredService<IUserSettingsStore>().Update(s => s with { SecurityAlerts = false });

        await LoadWithInsightsAsync();

        Assert.Equal(0, _sut.SecurityUpdateCount);
    }

    [Fact]
    public async Task UnusedApps_AreMarked_AndSortFirstByLeastUsed()
    {
        await LoadWithInsightsAsync();

        Assert.Equal("Not opened for 8 months", App("Steam").UnusedText);
        Assert.Equal("Not opened for a year", App("Zoom Workplace").UnusedText);
        Assert.Null(App("Google Chrome").UnusedText);

        _sut.SelectedSort = SortOption.All.Single(o => o.Order == SortOrder.LeastRecentlyUsed);
        Assert.Equal(["Zoom Workplace", "Steam", "OBS Studio"], _sut.Applications.Take(3).Select(a => a.Name));
    }

    [Fact]
    public async Task Uninstall_OffersToRecycleLeftovers()
    {
        await LoadWithInsightsAsync();

        App("Discord").RequestUninstallCommand.Execute(null);
        await App("Discord").ConfirmUninstallCommand.ExecuteAsync(null);

        Assert.True(_sut.HasLeftoverOffer);
        Assert.Equal("Discord left 2 folders behind (261 MB)", _sut.LeftoverOffer!.Text);

        await _sut.LeftoverOffer.CleanCommand.ExecuteAsync(null);
        Assert.False(_sut.HasLeftoverOffer);
        Assert.Equal("Moved 2 folders to the Recycle Bin.", _sut.StatusMessage);
    }

    [Fact]
    public async Task StartupTab_ListsEntriesWithTheirApp_AndTogglesThem()
    {
        await LoadWithInsightsAsync();

        _sut.IsStartupSection = true;
        await _sut.StartupPage.LoadAsync();
        var page = _sut.StartupPage;

        Assert.Equal("6 of 7 start with Windows", page.Summary);
        var steam = page.Items.Single(i => i.Name == "Steam");
        steam.IsEnabled = false;
        await Task.Delay(50);
        Assert.Equal("5 of 7 start with Windows", page.Summary);

        var machineWide = page.Items.Single(i => i.Name == "SecurityHealth");
        machineWide.IsEnabled = false;
        await Task.Delay(50);
        Assert.True(machineWide.IsEnabled);
        Assert.True(_sut.StatusIsError);
    }

    [Fact]
    public async Task ScheduledRun_UpgradesEverythingActionable_ExceptPinned()
    {
        await LoadWithInsightsAsync();
        App("Docker Desktop").PinCommand.Execute(null);

        var result = await _sut.RunScheduledUpdatesAsync(CancellationToken.None);

        Assert.Equal(5, result.Succeeded);
        Assert.Equal(0, _sut.UpdateCount);
        Assert.True(App("Docker Desktop").Model.HasUpdate);
    }
}
