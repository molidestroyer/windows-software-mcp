using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SoftwareDock.App.Shell;

namespace SoftwareDock.App;

public partial class App : Avalonia.Application
{
    private IHost? _host;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The dock lives in the tray: closing or hiding the panel must never end the process.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var args = desktop.Args ?? [];
            _host = AppHost.Build(args);
            _host.Start();

            var shell = _host.Services.GetRequiredService<DockShell>();
            shell.Start(showPanel: !args.Contains("--background", StringComparer.OrdinalIgnoreCase));
            Program.ShowRequested += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(shell.ShowDock);

            desktop.Exit += (_, _) =>
            {
                _host.StopAsync().GetAwaiter().GetResult();
                _host.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
