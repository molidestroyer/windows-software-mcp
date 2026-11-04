using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SoftwareDock.App.Demo;
using SoftwareDock.App.Services;
using SoftwareDock.App.Settings;
using SoftwareDock.Application.Inventory;

namespace SoftwareDock.App.Tests;

public sealed class SettingsAndBackupTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"software-dock-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void JsonSettings_SurviveARestart()
    {
        var path = Path.Combine(_folder, "settings.json");
        new JsonFileUserSettingsStore(path, NullLogger<JsonFileUserSettingsStore>.Instance)
            .Update(s => s with
            {
                ShowResourceUsage = false,
                SortOrder = SortOrder.Size,
                ScheduledUpdates = Domain.Updates.UpdateScheduleMode.Weekly,
                LastScheduledRun = new DateTime(2026, 11, 1, 3, 0, 0),
                PinnedApplications = [@"ARP\MACHINE\X64\GIT"],
                SkippedVersions = new Dictionary<string, string> { ["VLC"] = "3.1" },
            });

        var reloaded = new JsonFileUserSettingsStore(path, NullLogger<JsonFileUserSettingsStore>.Instance).Current;

        Assert.False(reloaded.ShowResourceUsage);
        Assert.Equal(SortOrder.Size, reloaded.SortOrder);
        Assert.Equal(Domain.Updates.UpdateScheduleMode.Weekly, reloaded.ScheduledUpdates);
        Assert.Equal(new DateTime(2026, 11, 1, 3, 0, 0), reloaded.LastScheduledRun);
        Assert.Equal([@"ARP\MACHINE\X64\GIT"], reloaded.PinnedApplications);
        Assert.Equal("3.1", reloaded.UpdateRules.SkippedVersions["VLC"]);
    }

    [Fact]
    public void JsonSettings_CorruptFile_FallsBackToDefaults()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, "{ not json");

        Assert.Equivalent(new UserSettings(), new JsonFileUserSettingsStore(path, NullLogger<JsonFileUserSettingsStore>.Instance).Current);
    }

    [Fact]
    public async Task AutoBackup_WritesBothFormats_OnlyWhenTheSoftwareChanges()
    {
        var machine = new DemoMachine(TimeSpan.Zero);
        using var inventory = new SoftwareInventory(machine, machine, TimeProvider.System, Options.Create(new InventoryOptions()), NullLogger<SoftwareInventory>.Instance);
        var settings = new InMemoryUserSettingsStore(new UserSettings { BackupFolder = _folder });
        using var backup = new AutoBackup(inventory, settings, TimeProvider.System, NullLogger<AutoBackup>.Instance);

        await inventory.GetSnapshotAsync(true, CancellationToken.None);
        var first = File.GetLastWriteTimeUtc(Path.Combine(_folder, AutoBackup.BackupFileName));
        Assert.True(File.Exists(Path.Combine(_folder, AutoBackup.WinGetFileName)));

        await Task.Delay(50);
        await inventory.GetSnapshotAsync(true, CancellationToken.None);
        Assert.Equal(first, File.GetLastWriteTimeUtc(Path.Combine(_folder, AutoBackup.BackupFileName)));

        await machine.UninstallAsync(Domain.Packages.PackageId.Parse("Git.Git"), true, null, CancellationToken.None);
        await inventory.GetSnapshotAsync(true, CancellationToken.None);
        Assert.DoesNotContain("Git.Git", await File.ReadAllTextAsync(Path.Combine(_folder, AutoBackup.BackupFileName)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AutoBackup_Disabled_WritesNothing()
    {
        var machine = new DemoMachine(TimeSpan.Zero);
        using var inventory = new SoftwareInventory(machine, machine, TimeProvider.System, Options.Create(new InventoryOptions()), NullLogger<SoftwareInventory>.Instance);
        using var backup = new AutoBackup(inventory, new InMemoryUserSettingsStore(new UserSettings { BackupFolder = _folder, AutomaticBackup = false }), TimeProvider.System, NullLogger<AutoBackup>.Instance);

        await inventory.GetSnapshotAsync(true, CancellationToken.None);

        Assert.False(Directory.Exists(_folder));
    }
}
