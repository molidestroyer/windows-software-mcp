using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SoftwareDock.App.Demo;
using SoftwareDock.App.Platform;
using SoftwareDock.App.Services;
using SoftwareDock.App.Settings;
using SoftwareDock.App.Shell;
using SoftwareDock.App.ViewModels;
using SoftwareDock.Application;
using SoftwareDock.Application.Inventory;
using SoftwareDock.Infrastructure;
using SoftwareDock.Infrastructure.WinGet;

namespace SoftwareDock.App;

/// <summary>Composition root of the dock.</summary>
public static class AppHost
{
    public static IHost Build(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        var demo = args.Contains("--demo", StringComparer.OrdinalIgnoreCase) || !OperatingSystem.IsWindows();
        ConfigureServices(builder.Services, builder.Configuration, demo);
        return builder.Build();
    }

    /// <summary>
    /// Registers everything the dock needs. <paramref name="demo"/> swaps the Windows adapters for
    /// an in-memory machine — same use cases, same UI, nothing touched.
    /// </summary>
    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration, bool demo)
    {
        services.Configure<DockOptions>(configuration.GetSection(DockOptions.SectionName));
        services.Configure<InventoryOptions>(configuration.GetSection(InventoryOptions.SectionName));
        services.Configure<WinGetOptions>(configuration.GetSection(WinGetOptions.SectionName));

        services.AddSoftwareDockApplication();

        if (demo || !OperatingSystem.IsWindows())
        {
            services.AddSoftwareDockDemoAdapters();
            services.AddSingleton<IIconProvider, InitialsOnlyIconProvider>();
            services.AddSingleton<IStartupRegistration, UnsupportedStartupRegistration>();
            services.TryAddSingleton<IUserSettingsStore>(new InMemoryUserSettingsStore(new UserSettings { AutomaticBackup = false }));
            services.AddSingleton<IAppUpdater, NoAppUpdater>();
        }
        else
        {
            services.AddSoftwareDockWindowsInfrastructure();
            services.AddSingleton<IIconProvider, ShellIconProvider>();
            services.AddSingleton<IStartupRegistration, RunKeyStartupRegistration>();
            services.TryAddSingleton<IUserSettingsStore, JsonFileUserSettingsStore>();
            services.AddSingleton<IAppUpdater, VelopackAppUpdater>();
        }

        services.AddSingleton<AutoBackup>();
        services.TryAddSingleton<IShellServices>(sp => new AvaloniaShellServices(new Lazy<DockWindow>(sp.GetRequiredService<DockWindow>)));
        services.AddSingleton<SettingsViewModel>();

        services.AddSingleton<DockViewModel>();
        services.AddSingleton<DockWindow>();
        services.AddSingleton<DockShell>();
    }
}
