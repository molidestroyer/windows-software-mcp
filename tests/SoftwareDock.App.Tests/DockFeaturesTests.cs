using Microsoft.Extensions.DependencyInjection;
using SoftwareDock.App.Settings;
using SoftwareDock.App.ViewModels;
using SoftwareDock.Domain.Backup;
using SoftwareDock.Domain.Packages;

namespace SoftwareDock.App.Tests;

public sealed class DockFeaturesTests : IDisposable
{
    private readonly FakeShell _shell = new();
    private readonly ServiceProvider _services;
    private readonly DockViewModel _sut;

    public DockFeaturesTests()
    {
        _services = TestServices.Create(shell: _shell);
        _sut = _services.GetRequiredService<DockViewModel>();
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task BulkUninstall_RemovesEverySelectedApp()
    {
        await _sut.LoadAsync();
        _sut.IsSelecting = true;
        foreach (var name in new[] { "Discord", "Steam", "Zoom Workplace" })
        {
            _sut.Applications.Single(a => a.Name == name).IsSelected = true;
        }

        Assert.Equal(3, _sut.SelectedCount);
        Assert.True(_sut.ShowBulkBar);
        Assert.Equal("Remove 3 apps?", _sut.BulkConfirmLabel);

        _sut.RequestBulkUninstallCommand.Execute(null);
        await _sut.ConfirmBulkUninstallCommand.ExecuteAsync(null);

        Assert.Equal(18, _sut.InstalledCount);
        Assert.DoesNotContain(_sut.Applications, a => a.Name is "Discord" or "Steam" or "Zoom Workplace");
        Assert.Equal("Bulk uninstall: 3 of 3 done.", _sut.StatusMessage);
        Assert.False(_sut.IsSelecting);
    }

    [Fact]
    public async Task SelectionMode_HidesRowActions_AndSelectAllToggles()
    {
        await _sut.LoadAsync();
        var git = _sut.Applications.Single(a => a.Name == "Git");
        Assert.True(git.ShowActions);

        _sut.IsSelecting = true;
        Assert.False(git.ShowActions);

        _sut.SelectAllCommand.Execute(null);
        Assert.Equal(21, _sut.SelectedCount);
        _sut.SelectAllCommand.Execute(null);
        Assert.Equal(0, _sut.SelectedCount);
    }

    [Fact]
    public async Task LeavingSelectionMode_ClearsTheSelection()
    {
        await _sut.LoadAsync();
        _sut.IsSelecting = true;
        _sut.Applications[0].IsSelected = true;

        _sut.IsSelecting = false;

        Assert.Equal(0, _sut.SelectedCount);
    }

    [Fact]
    public async Task ResourceUsage_IsAttributedToRunningApps()
    {
        await _sut.LoadAsync();

        await _sut.SampleResourcesAsync();
        await Task.Delay(200);
        await _sut.SampleResourcesAsync();

        Assert.True(_sut.HasResourceData);
        var chrome = _sut.Applications.Single(a => a.Name == "Google Chrome");
        Assert.True(chrome.IsRunning);
        Assert.InRange(chrome.Usage.CpuPercent, 5, 20);    // 0.9 cores of 8
        Assert.False(_sut.Applications.Single(a => a.Name == "Git").IsRunning);
        Assert.Equal("6 apps running · 8.9 GB", _sut.AppsMemoryText);
    }

    [Fact]
    public async Task ResourceUsage_Off_ShowsNoGauges()
    {
        _services.GetRequiredService<IUserSettingsStore>().Update(s => s with { ShowResourceUsage = false });
        await _sut.LoadAsync();

        await _sut.SampleResourcesAsync();

        Assert.False(_sut.HasResourceData);
    }

    [Fact]
    public async Task SortByMemory_PutsTheHeaviestFirst_AndIsRemembered()
    {
        await _sut.LoadAsync();
        await _sut.SampleResourcesAsync();

        _sut.SelectedSort = SortOption.All.Single(o => o.Order == SortOrder.MemoryUse);

        Assert.Equal("Ollama", _sut.Applications[0].Name);
        Assert.Equal(SortOrder.MemoryUse, _services.GetRequiredService<IUserSettingsStore>().Current.SortOrder);
    }

    [Fact]
    public async Task Restore_ProposesOnlyMissingPackages_AndInstallsTheSelectedOnes()
    {
        await _sut.LoadAsync();
        var backup = new SoftwareManifest(
            DateTimeOffset.UtcNow,
            "OLD-PC",
            [
                new ManifestPackage(PackageId.Parse("Git.Git"), "Git", "winget", "2.47.1"),
                new ManifestPackage(PackageId.Parse("Brave.Brave"), "Brave", "winget", null),
                new ManifestPackage(PackageId.Parse("Obsidian.Obsidian"), "Obsidian", "winget", null),
            ],
            [new ManifestApplication("Some Driver Utility", "Contoso", "1.0")]);

        await _sut.OpenRestoreAsync(backup);

        Assert.True(_sut.IsRestorePage);
        var restore = _sut.Restore!;
        Assert.Equal(["Brave", "Obsidian"], restore.Packages.Select(p => p.Name));
        Assert.Equal("1 already installed on this PC", restore.AlreadyInstalledText);
        Assert.Equal(["Some Driver Utility  ·  Contoso"], restore.Manual);

        restore.Packages[1].IsSelected = false;
        await restore.InstallCommand.ExecuteAsync(null);

        Assert.Equal("Installed", restore.Packages[0].Outcome);
        Assert.Null(restore.Packages[1].Outcome);
        Assert.Equal("Restore: 1 of 1 done.", _sut.StatusMessage);
    }

    [Fact]
    public async Task Settings_ImportReadsTheFileAndOpensRestore()
    {
        await _sut.LoadAsync();
        var path = Path.Combine(Path.GetTempPath(), $"sd-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, """{ "format": "software-dock-backup", "packages": [ { "id": "ShareX.ShareX", "name": "ShareX", "source": "winget" } ] }""");
        _shell.NextOpenPath = path;

        try
        {
            await _sut.SettingsPage.ImportBackupCommand.ExecuteAsync(null);
            for (var i = 0; i < 50 && !_sut.IsRestorePage; i++)
            {
                await Task.Delay(10);
            }

            Assert.True(_sut.IsRestorePage);
            Assert.Equal("ShareX", Assert.Single(_sut.Restore!.Packages).Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Settings_ExportForWinGet_WritesAnImportableFile()
    {
        await _sut.LoadAsync();
        var path = Path.Combine(Path.GetTempPath(), $"sd-{Guid.NewGuid():N}.json");
        _shell.NextSavePath = path;

        try
        {
            await _sut.SettingsPage.ExportWinGetCommand.ExecuteAsync(null);

            var json = await File.ReadAllTextAsync(path);
            Assert.Contains("\"PackageIdentifier\": \"Git.Git\"", json, StringComparison.Ordinal);
            Assert.StartsWith("Saved 20 packages.", _sut.SettingsPage.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Settings_CopyMcpConfig_PutsTheSnippetOnTheClipboard()
    {
        await _sut.SettingsPage.CopyMcpConfigCommand.ExecuteAsync(null);

        Assert.Contains("\"software-dock\"", _shell.Clipboard, StringComparison.Ordinal);
    }
}
