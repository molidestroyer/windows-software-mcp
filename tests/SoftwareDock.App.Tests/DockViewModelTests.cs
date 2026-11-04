using Microsoft.Extensions.DependencyInjection;
using SoftwareDock.App.ViewModels;

namespace SoftwareDock.App.Tests;

public sealed class DockViewModelTests : IDisposable
{
    private readonly ServiceProvider _services = TestServices.Create();
    private readonly DockViewModel _sut;

    public DockViewModelTests() => _sut = _services.GetRequiredService<DockViewModel>();

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task Load_ListsEverything_AndCountsUpdates()
    {
        await _sut.LoadAsync();

        Assert.Equal(21, _sut.InstalledCount);
        Assert.Equal(21, _sut.Applications.Count);
        Assert.Equal(6, _sut.UpdateCount);
        Assert.Equal("21 installed · 6 updates", _sut.Summary);
    }

    [Fact]
    public async Task Load_ReconcilesRegistryWithWinGet()
    {
        await _sut.LoadAsync();

        var git = _sut.Applications.Single(a => a.Name == "Git");
        Assert.True(git.CanUpgrade);
        Assert.Equal("→ 2.47.1", git.UpdateText);
        Assert.Equal("The Git Development Community  ·  v2.45.0", git.Subtitle);
    }

    [Fact]
    public async Task SearchText_FiltersInstalledApps()
    {
        await _sut.LoadAsync();

        _sut.SearchText = "microsoft";

        // Publisher matches too: Windows Terminal is published by Microsoft Corporation.
        Assert.Equal(["Microsoft PowerToys", "Microsoft Visual Studio Code", "Windows Terminal"], _sut.Applications.Select(a => a.Name));
    }

    [Fact]
    public async Task UpdatesSection_ShowsOnlyAppsWithUpdates()
    {
        await _sut.LoadAsync();

        _sut.IsUpdatesSection = true;

        Assert.Equal(6, _sut.Applications.Count);
        Assert.All(_sut.Applications, a => Assert.True(a.HasUpdate));
    }

    [Fact]
    public async Task Upgrade_UpdatesTheListAndTheCount()
    {
        await _sut.LoadAsync();
        var git = _sut.Applications.Single(a => a.Name == "Git");

        await git.UpgradeCommand.ExecuteAsync(null);

        Assert.Equal(5, _sut.UpdateCount);
        Assert.False(git.HasUpdate);
        Assert.Equal("Upgrade Git.Git: done.", _sut.StatusMessage);
        Assert.False(_sut.StatusIsError);
    }

    [Fact]
    public async Task Uninstall_AsksForConfirmationFirst()
    {
        await _sut.LoadAsync();
        var vlc = _sut.Applications.Single(a => a.Name == "VLC media player");

        vlc.RequestUninstallCommand.Execute(null);
        Assert.True(vlc.IsConfirmingUninstall);
        Assert.False(vlc.ShowUninstall);

        await vlc.ConfirmUninstallCommand.ExecuteAsync(null);

        Assert.DoesNotContain(_sut.Applications, a => a.Name == "VLC media player");
        Assert.Equal(20, _sut.InstalledCount);
    }

    [Fact]
    public async Task Discover_SearchesTheCatalog_AndMarksWhatIsInstalled()
    {
        await _sut.LoadAsync();
        _sut.IsDiscoverSection = true;

        _sut.SearchText = "code";
        await WaitUntil(() => _sut.CatalogResults.Count > 0);

        var vscode = _sut.CatalogResults.Single(c => c.Package.Id.Value == "Microsoft.VisualStudioCode");
        var insiders = _sut.CatalogResults.Single(c => c.Package.Id.Value == "Microsoft.VisualStudioCode.Insiders");
        Assert.True(vscode.IsInstalled);
        Assert.False(insiders.IsInstalled);

        await insiders.InstallCommand.ExecuteAsync(null);

        Assert.True(insiders.IsInstalled);
        Assert.Equal(22, _sut.InstalledCount);
    }

    [Fact]
    public async Task EmptyStates_ExplainThemselves()
    {
        await _sut.LoadAsync();

        _sut.SearchText = "zzz";
        Assert.True(_sut.IsEmpty);
        Assert.Equal("No installed app matches “zzz”.", _sut.EmptyMessage);

        _sut.SearchText = string.Empty;
        _sut.IsDiscoverSection = true;
        Assert.Equal("Type to search for something new to install.", _sut.EmptyMessage);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(20);
        }

        Assert.True(condition());
    }
}
