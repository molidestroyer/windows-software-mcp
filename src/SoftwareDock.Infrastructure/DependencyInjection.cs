using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SoftwareDock.Application.Abstractions;
using SoftwareDock.Infrastructure.Leftovers;
using SoftwareDock.Infrastructure.Monitoring;
using SoftwareDock.Infrastructure.Processes;
using SoftwareDock.Infrastructure.Registry;
using SoftwareDock.Infrastructure.Security;
using SoftwareDock.Infrastructure.Startup;
using SoftwareDock.Infrastructure.Usage;
using SoftwareDock.Infrastructure.WinGet;

namespace SoftwareDock.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Wires the ports to the real Windows adapters.</summary>
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddSoftwareDockWindowsInfrastructure(this IServiceCollection services)
    {
        services.AddOptions<WinGetOptions>();
        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.TryAddSingleton<IPackageManager, WinGetPackageManager>();
        services.TryAddSingleton<IInstalledApplicationSource, RegistryInstalledApplicationSource>();
        services.TryAddSingleton<IUninstallerLauncher, ShellUninstallerLauncher>();
        services.TryAddSingleton<ISystemMonitor, WindowsSystemMonitor>();
        services.TryAddSingleton<IStartupItemSource, RegistryStartupItemSource>();
        services.TryAddSingleton<IUsageHistorySource, UserAssistUsageSource>();
        services.TryAddSingleton<ILeftoverCleaner, RecycleBinLeftoverCleaner>();
        services.AddOptions<SecurityFeedOptions>();
        services.AddHttpClient<IVulnerabilityFeed, CisaKevFeed>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("SoftwareDock/1.0 (+https://github.com/molidestroyer/windows-software-mcp)");
        });
        return services;
    }
}
