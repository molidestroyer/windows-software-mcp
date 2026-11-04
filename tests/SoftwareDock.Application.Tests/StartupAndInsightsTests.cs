using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SoftwareDock.Application.Abstractions;
using SoftwareDock.Application.Insights;
using SoftwareDock.Application.Inventory;
using SoftwareDock.Application.Startup;
using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Operations;
using SoftwareDock.Domain.Packages;
using SoftwareDock.Domain.Security;
using SoftwareDock.Domain.Startup;
using SoftwareDock.Domain.Usage;

namespace SoftwareDock.Application.Tests;

public sealed class StartupAndInsightsTests : IDisposable
{
    private readonly FakeRegistry _registry = new();
    private readonly FakePackageManager _packages = new();
    private readonly SoftwareInventory _inventory;

    public StartupAndInsightsTests()
    {
        _registry.Entries.Add(Apps.Registry("Discord", "Discord") with { InstallLocation = @"C:\Users\m\AppData\Local\Discord" });
        _registry.Entries.Add(Apps.Registry("Firefox", "Mozilla Firefox") with { InstallLocation = @"C:\Program Files\Mozilla Firefox", Publisher = "Mozilla" });
        _packages.Installed.Add(new InstalledPackage("Mozilla Firefox", PackageId.Parse("Mozilla.Firefox"), "1.0", "2.0", "winget"));
        _inventory = new SoftwareInventory(_registry, _packages, TimeProvider.System, Options.Create(new InventoryOptions()), NullLogger<SoftwareInventory>.Instance);
    }

    public void Dispose() => _inventory.Dispose();

    [Fact]
    public async Task Startup_EntriesAreLinkedToTheirApplication_EnabledFirst()
    {
        var source = new FakeStartup(
            new StartupItem("Updater", "x", StartupLocation.MachineRun, @"C:\Tools\updater.exe", false),
            new StartupItem("Discord", "\"C:\\Users\\m\\AppData\\Local\\Discord\\Update.exe\" --processStart Discord.exe", StartupLocation.UserRun, @"C:\Users\m\AppData\Local\Discord\Update.exe", true));
        var sut = new StartupManager(source, _inventory);

        var entries = await sut.ListAsync(CancellationToken.None);

        Assert.Equal(["Discord", "Updater"], entries.Select(e => e.DisplayName));
        Assert.Equal("Discord", entries[0].Application!.Name);
        Assert.Null(entries[1].Application);
    }

    [Fact]
    public async Task Startup_SettingTheCurrentState_DoesNothing()
    {
        var source = new FakeStartup();
        var item = new StartupItem("Discord", "x", StartupLocation.UserRun, null, true);

        await new StartupManager(source, _inventory).SetEnabledAsync(item, true, CancellationToken.None);
        await new StartupManager(source, _inventory).SetEnabledAsync(item, false, CancellationToken.None);

        Assert.Equal(["Discord=False"], source.Changes);
    }

    [Fact]
    public async Task Insights_FlagExploitedAppsWithUpdates_AndSurviveAFeedOutage()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero));
        var feed = new FakeFeed([new("CVE-2026-1111", "Mozilla", "Firefox", "Use-after-free", new DateOnly(2026, 10, 20), true)]);
        var sut = new SoftwareInsights(feed, new FakeUsage(), new FakeStartup(), new FakeSystem(), time, NullLogger<SoftwareInsights>.Instance);
        var snapshot = await _inventory.GetSnapshotAsync(false, CancellationToken.None);

        var advisories = await sut.GetSecurityAdvisoriesAsync(snapshot, CancellationToken.None);
        Assert.True(Assert.Single(advisories).Value.UsedByRansomware);

        feed.Throw = true;
        Assert.Empty(await sut.GetSecurityAdvisoriesAsync(snapshot, CancellationToken.None));
    }

    [Fact]
    public async Task Insights_UsageIsAttributedByInstallFolder()
    {
        var lastWeek = new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);
        var usage = new FakeUsage(new UsageRecord(@"C:\Users\m\AppData\Local\Discord\app-1.0\Discord.exe", lastWeek, 40));
        var sut = new SoftwareInsights(new FakeFeed([]), usage, new FakeStartup(), new FakeSystem(), TimeProvider.System, NullLogger<SoftwareInsights>.Instance);

        var result = sut.GetUsage(await _inventory.GetSnapshotAsync(false, CancellationToken.None));

        Assert.Equal(lastWeek, Assert.Single(result).Value.LastUsed);
    }

    [Fact]
    public async Task Unused_ExcludesAppsEnabledAtStartupOrRunning()
    {
        var longAgo = DateTimeOffset.UtcNow.AddDays(-200);
        var usage = new FakeUsage(
            new UsageRecord(@"C:\Users\m\AppData\Local\Discord\Discord.exe", longAgo, 3),
            new UsageRecord(@"C:\Program Files\Mozilla Firefox\firefox.exe", longAgo, 3));
        var snapshot = await _inventory.GetSnapshotAsync(false, CancellationToken.None);

        var idle = new SoftwareInsights(new FakeFeed([]), usage, new FakeStartup(), new FakeSystem(), TimeProvider.System, NullLogger<SoftwareInsights>.Instance);
        Assert.Equal(2, (await idle.GetUnusedAsync(snapshot, null, CancellationToken.None)).Count);

        var startsWithWindows = new FakeStartup(new StartupItem("Discord", "x", StartupLocation.UserRun, @"C:\Users\m\AppData\Local\Discord\Update.exe", true));
        var running = new FakeSystem(new Domain.Monitoring.ProcessSample(7, @"C:\Program Files\Mozilla Firefox\firefox.exe", TimeSpan.Zero, 1));
        var busy = new SoftwareInsights(new FakeFeed([]), usage, startsWithWindows, running, TimeProvider.System, NullLogger<SoftwareInsights>.Instance);

        Assert.Empty(await busy.GetUnusedAsync(snapshot, null, CancellationToken.None));
    }

    [Fact]
    public async Task Unused_ADisabledStartupEntryDoesNotCountAsUse()
    {
        var usage = new FakeUsage(new UsageRecord(@"C:\Users\m\AppData\Local\Discord\Discord.exe", DateTimeOffset.UtcNow.AddDays(-200), 3));
        var disabled = new FakeStartup(new StartupItem("Discord", "x", StartupLocation.UserRun, @"C:\Users\m\AppData\Local\Discord\Update.exe", false));
        var sut = new SoftwareInsights(new FakeFeed([]), usage, disabled, new FakeSystem(), TimeProvider.System, NullLogger<SoftwareInsights>.Instance);

        Assert.Single(await sut.GetUnusedAsync(await _inventory.GetSnapshotAsync(false, CancellationToken.None), null, CancellationToken.None));
    }

    [Fact]
    public async Task UpgradeMany_SkipsAppsWithoutAnUpgrade()
    {
        using var operations = new SoftwareDock.Application.Operations.SoftwareOperations(_inventory, _packages, new FakeUninstaller(), NullLogger<SoftwareDock.Application.Operations.SoftwareOperations>.Instance);
        var discord = ApplicationKey.ForRegistry(InstallScope.Machine, RegistryView.Native64, "Discord");
        var firefox = ApplicationKey.ForRegistry(InstallScope.Machine, RegistryView.Native64, "Firefox");

        var result = await operations.UpgradeManyAsync([firefox, discord], null, CancellationToken.None);

        Assert.Equal(["upgrade Mozilla.Firefox"], _packages.Calls);
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Failed);
    }

    private sealed class FakeStartup(params StartupItem[] items) : IStartupItemSource
    {
        public List<string> Changes { get; } = [];

        public Task<IReadOnlyList<StartupItem>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<StartupItem>>(items);

        public Task<OperationResult> SetEnabledAsync(StartupItem item, bool enabled, CancellationToken cancellationToken)
        {
            Changes.Add($"{item.Name}={enabled}");
            return Task.FromResult(OperationResult.Success("ok"));
        }
    }

    private sealed class FakeFeed(IReadOnlyList<KnownExploitedVulnerability> catalog) : IVulnerabilityFeed
    {
        public bool Throw { get; set; }

        public Task<IReadOnlyList<KnownExploitedVulnerability>> GetAsync(CancellationToken cancellationToken) =>
            Throw ? throw new HttpRequestException("offline") : Task.FromResult(catalog);
    }

    private sealed class FakeSystem(params Domain.Monitoring.ProcessSample[] processes) : ISystemMonitor
    {
        public int ProcessorCount => 4;

        public IReadOnlyList<Domain.Monitoring.ProcessSample> GetProcesses() => processes;

        public Domain.Monitoring.MemoryStatus GetMemoryStatus() => new(1, 1);
    }

    private sealed class FakeUsage(params UsageRecord[] records) : IUsageHistorySource
    {
        public IReadOnlyList<UsageRecord> GetUsage() => records;
    }
}
