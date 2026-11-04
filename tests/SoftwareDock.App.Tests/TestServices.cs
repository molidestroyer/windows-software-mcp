using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SoftwareDock.App.Demo;
using SoftwareDock.App.Services;
using SoftwareDock.App.Settings;

namespace SoftwareDock.App.Tests;

internal static class TestServices
{
    /// <summary>The dock's real composition, in demo mode, with a machine that answers instantly.</summary>
    public static ServiceProvider Create(UserSettings? settings = null, IShellServices? shell = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new DemoMachine(TimeSpan.Zero));
        services.AddSingleton<IUserSettingsStore>(new InMemoryUserSettingsStore(settings ?? new UserSettings { AutomaticBackup = false }));
        if (shell is not null)
        {
            services.AddSingleton(shell);
        }

        AppHost.ConfigureServices(services, new ConfigurationBuilder().Build(), demo: true);
        return services.BuildServiceProvider();
    }
}

internal sealed class FakeShell : IShellServices
{
    public string? NextOpenPath { get; set; }

    public string? NextSavePath { get; set; }

    public string? Clipboard { get; private set; }

    public Task<string?> PickSaveFileAsync(string title, string suggestedFileName) => Task.FromResult(NextSavePath);

    public Task<string?> PickOpenFileAsync(string title) => Task.FromResult(NextOpenPath);

    public Task CopyTextAsync(string text)
    {
        Clipboard = text;
        return Task.CompletedTask;
    }

    public Task OpenFolderAsync(string path) => Task.CompletedTask;
}
