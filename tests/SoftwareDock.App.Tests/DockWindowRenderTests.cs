using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using SoftwareDock.App.Shell;
using SoftwareDock.App.ViewModels;
using SoftwareDock.Domain.Backup;
using SoftwareDock.Domain.Packages;

namespace SoftwareDock.App.Tests;

/// <summary>
/// Renders the real dock window with Skia, off-screen, in demo mode. Proves the XAML loads and
/// binds (compiled bindings fail at build time, but templates and styles fail at runtime), and
/// writes PNGs to <c>SOFTWAREDOCK_SCREENSHOTS</c> when set — the README screenshots come from here.
/// </summary>
public sealed class DockWindowRenderTests : IClassFixture<HeadlessSession>
{
    private readonly HeadlessSession _session;

    public DockWindowRenderTests(HeadlessSession session) => _session = session;

    public static TheoryData<string> Scenes => ["installed", "updates", "discover", "select", "settings", "restore", "startup", "leftovers"];

    [Theory]
    [MemberData(nameof(Scenes))]
    public async Task Dock_Renders(string scene)
    {
        var pixels = await _session.Session.Dispatch(
            async () =>
            {
                using var services = TestServices.Create();
                var viewModel = services.GetRequiredService<DockViewModel>();
                var window = services.GetRequiredService<DockWindow>();
                window.Width = 420;
                window.Height = 860;
                window.Show();
                viewModel.StopMonitoring();

                await viewModel.LoadAsync();
                await viewModel.RefreshInsightsAsync(viewModel.Snapshot!);
                await viewModel.SampleResourcesAsync();
                await Task.Delay(150);
                await viewModel.SampleResourcesAsync();
                await ArrangeAsync(scene, viewModel);

                Dispatcher.UIThread.RunJobs();
                var frame = window.CaptureRenderedFrame()!;
                Save(frame, scene);
                window.Close();
                return frame.PixelSize.Width * frame.PixelSize.Height;
            },
            CancellationToken.None);

        Assert.True(pixels > 0);
    }

    private static async Task ArrangeAsync(string scene, DockViewModel viewModel)
    {
        switch (scene)
        {
            case "installed":
                viewModel.Applications.Single(a => a.Name == "Discord").RequestUninstallCommand.Execute(null);
                break;
            case "updates":
                viewModel.SelectedSection = DockSection.Updates;
                break;
            case "discover":
                viewModel.SelectedSection = DockSection.Discover;
                viewModel.SearchText = "code";
                for (var i = 0; i < 100 && viewModel.CatalogResults.Count == 0; i++)
                {
                    await Task.Delay(20);
                    Dispatcher.UIThread.RunJobs();
                }

                break;
            case "select":
                viewModel.IsSelecting = true;
                foreach (var name in new[] { "Discord", "Docker Desktop", "Contoso Legacy Reporting" })
                {
                    viewModel.Applications.Single(a => a.Name == name).IsSelected = true;
                }

                viewModel.RequestBulkUninstallCommand.Execute(null);
                break;
            case "settings":
                viewModel.Page = DockPage.Settings;
                break;
            case "startup":
                viewModel.SelectedSection = DockSection.Startup;
                await viewModel.StartupPage.LoadAsync();
                break;
            case "leftovers":
                var discord = viewModel.Applications.Single(a => a.Name == "Discord");
                await discord.ConfirmUninstallCommand.ExecuteAsync(null);
                viewModel.Applications.Single(a => a.Name == "Git").SkipVersionCommand.Execute(null);
                viewModel.Applications.Single(a => a.Name == "Microsoft PowerToys").PinCommand.Execute(null);
                break;
            case "restore":
                await viewModel.OpenRestoreAsync(new SoftwareManifest(
                    new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero),
                    "MIGUEL-OLD-PC",
                    [
                        new ManifestPackage(PackageId.Parse("Git.Git"), "Git", "winget", null),
                        new ManifestPackage(PackageId.Parse("Brave.Brave"), "Brave", "winget", null),
                        new ManifestPackage(PackageId.Parse("Bitwarden.Bitwarden"), "Bitwarden", "winget", null),
                        new ManifestPackage(PackageId.Parse("Obsidian.Obsidian"), "Obsidian", "winget", null),
                        new ManifestPackage(PackageId.Parse("JetBrains.Rider"), "JetBrains Rider", "winget", null),
                    ],
                    [new ManifestApplication("Logitech G HUB", "Logitech", "2024.9"), new ManifestApplication("Contoso VPN Client", "Contoso", "5.2")]));
                viewModel.Restore!.Packages[3].IsSelected = false;
                break;
        }
    }

    private static void Save(Avalonia.Media.Imaging.WriteableBitmap frame, string name)
    {
        var directory = Environment.GetEnvironmentVariable("SOFTWAREDOCK_SCREENSHOTS");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        frame.Save(Path.Combine(directory, $"dock-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
}

public sealed class HeadlessSession : IDisposable
{
    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(HeadlessApp));

    public void Dispose() => Session.Dispose();
}

public static class HeadlessApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
}
