using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SoftwareDock.App.Demo;

namespace SoftwareDock.App.Tests;

internal static class TestServices
{
    /// <summary>The dock's real composition, in demo mode, with a machine that answers instantly.</summary>
    public static ServiceProvider Create()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new DemoMachine(TimeSpan.Zero));
        AppHost.ConfigureServices(services, new ConfigurationBuilder().Build(), demo: true);
        return services.BuildServiceProvider();
    }
}
