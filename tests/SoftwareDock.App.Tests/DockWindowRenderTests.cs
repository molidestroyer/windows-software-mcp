using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using SoftwareDock.App.Shell;
using SoftwareDock.App.ViewModels;

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

    [Theory]
    [InlineData(DockSection.Installed, null, "installed")]
    [InlineData(DockSection.Updates, null, "updates")]
    [InlineData(DockSection.Discover, "code", "discover")]
    public async Task Dock_Renders(DockSection section, string? search, string name)
    {
        var pixels = await _session.Session.Dispatch(
            async () =>
            {
                using var services = TestServices.Create();
                var viewModel = services.GetRequiredService<DockViewModel>();
                var window = services.GetRequiredService<DockWindow>();
                window.Width = 420;
                window.Height = 780;
                window.Show();

                await viewModel.LoadAsync();
                viewModel.SelectedSection = section;
                if (search is not null)
                {
                    viewModel.SearchText = search;
                    for (var i = 0; i < 100 && viewModel.CatalogResults.Count == 0; i++)
                    {
                        await Task.Delay(20);
                        Dispatcher.UIThread.RunJobs();
                    }
                }

                if (section == DockSection.Installed)
                {
                    viewModel.Applications.Single(a => a.Name == "Discord").RequestUninstallCommand.Execute(null);
                }

                Dispatcher.UIThread.RunJobs();
                var frame = window.CaptureRenderedFrame()!;
                Save(frame, name);
                window.Close();
                return frame.PixelSize.Width * frame.PixelSize.Height;
            },
            CancellationToken.None);

        Assert.True(pixels > 0);
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
