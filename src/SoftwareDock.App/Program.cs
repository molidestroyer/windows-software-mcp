using Avalonia;
using SoftwareDock.App.Platform;

namespace SoftwareDock.App;

internal static class Program
{
    /// <summary>Raised when a second launch asks this instance to show its panel.</summary>
    public static event EventHandler? ShowRequested;

    [STAThread]
    public static int Main(string[] args)
    {
        // Must run first: during install, update and uninstall Velopack starts the app with hook
        // arguments, and this handles them and exits before any window exists.
        Velopack.VelopackApp.Build().Run();

        // One elevated batch for the dock (UacBatchElevator): no window, no single-instance lock.
        if (OperatingSystem.IsWindows() && args.Length > 0 && args[0] == Services.UacBatchElevator.WorkerArgument)
        {
            return Services.ElevatedWorker.Run(args);
        }

        using var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            return 0;
        }

        instance.ListenForShowRequests(() => ShowRequested?.Invoke(null, EventArgs.Empty));
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Also used by the visual designer and the headless tests.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
