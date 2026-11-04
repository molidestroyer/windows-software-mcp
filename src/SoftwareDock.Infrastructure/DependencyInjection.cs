using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SoftwareDock.Application.Abstractions;
using SoftwareDock.Infrastructure.Processes;
using SoftwareDock.Infrastructure.Registry;
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
        return services;
    }
}
