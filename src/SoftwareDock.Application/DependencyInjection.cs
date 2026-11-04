using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SoftwareDock.Application.Inventory;
using SoftwareDock.Application.Operations;

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
        return services;
    }
}
