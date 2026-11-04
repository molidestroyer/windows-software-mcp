using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SoftwareDock.Application.Backup;
using SoftwareDock.Application.Insights;
using SoftwareDock.Application.Inventory;
using SoftwareDock.Application.Monitoring;
using SoftwareDock.Application.Operations;
using SoftwareDock.Application.Startup;

namespace SoftwareDock.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the use cases. Both are singletons on purpose: the inventory cache and the
    /// operation queue only work if every caller — dock UI and MCP tools alike — shares them.
    /// </summary>
    public static IServiceCollection AddSoftwareDockApplication(this IServiceCollection services)
    {
        services.AddOptions<InventoryOptions>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISoftwareInventory, SoftwareInventory>();
        services.TryAddSingleton<ISoftwareOperations, SoftwareOperations>();
        services.TryAddSingleton<SoftwareBackup>();

        // Needs an ISystemMonitor; front ends that never show resource usage simply never resolve it.
        services.TryAddSingleton<ResourceMonitor>();
        services.TryAddSingleton<StartupManager>();
        services.TryAddSingleton<SoftwareInsights>();
        return services;
    }
}
