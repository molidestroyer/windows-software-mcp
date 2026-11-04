using System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SoftwareDock.Application;
using SoftwareDock.Application.Abstractions;
using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Operations;
using SoftwareDock.Domain.Packages;

namespace SoftwareDock.Mcp.Tests;

/// <summary>
/// The real MCP server and a real MCP client talking over in-memory pipes — the same JSON-RPC an
/// agent sends over stdio, minus the process. Only the Windows adapters are faked.
/// </summary>
internal sealed class McpServerFixture : IAsyncDisposable
{
    private readonly IHost _host;

    private McpServerFixture(IHost host, McpClient client, FakeMachine machine)
    {
        _host = host;
        Client = client;
        Machine = machine;
    }

    public McpClient Client { get; }

    public FakeMachine Machine { get; }

    public static async Task<McpServerFixture> StartAsync(bool readOnly = false)
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var machine = new FakeMachine();

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IInstalledApplicationSource>(machine);
        builder.Services.AddSingleton<IPackageManager>(machine);
        builder.Services.AddSingleton<IUninstallerLauncher>(machine);
        builder.Services.AddSingleton<ISystemMonitor>(machine);
        builder.Services.AddSingleton<IStartupItemSource>(machine);
        builder.Services.AddSingleton<IUsageHistorySource>(machine);
        builder.Services.AddSingleton<IVulnerabilityFeed>(machine);
        builder.Services.AddSoftwareDockApplication();
        builder.Services
            .AddSoftwareDockMcpServer(readOnly)
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());

        var host = builder.Build();
        await host.StartAsync();

        var client = await McpClient.CreateAsync(
            new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream(), NullLoggerFactory.Instance),
            new McpClientOptions(),
            NullLoggerFactory.Instance,
            CancellationToken.None);
        return new McpServerFixture(host, client, machine);
    }

    public async Task<CallToolResult> CallAsync(string tool, Dictionary<string, object?> arguments) =>
        await Client.CallToolAsync(tool, arguments, null, null, CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await _host.StopAsync();
        _host.Dispose();
    }
}

internal sealed class FakeMachine : IInstalledApplicationSource, IPackageManager, IUninstallerLauncher, ISystemMonitor, IStartupItemSource, IUsageHistorySource, IVulnerabilityFeed
{
    public Task<IReadOnlyList<Domain.Startup.StartupItem>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Domain.Startup.StartupItem>>(
            [new("GitUpdater", @"""C:\Program Files\Git\updater.exe"" --quiet", Domain.Startup.StartupLocation.UserRun, @"C:\Program Files\Git\updater.exe", true)]);

    public Task<OperationResult> SetEnabledAsync(Domain.Startup.StartupItem item, bool enabled, CancellationToken cancellationToken) =>
        Record($"startup {item.Name}={enabled}");

    public IReadOnlyList<Domain.Usage.UsageRecord> GetUsage() =>
        [new(@"C:\Program Files\Git\git-bash.exe", DateTimeOffset.UtcNow.AddDays(-200), 3)];

    public Task<IReadOnlyList<Domain.Security.KnownExploitedVulnerability>> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Domain.Security.KnownExploitedVulnerability>>(
            [new("CVE-2026-4242", "Git", "Git", "Git remote code execution", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)), false)]);

    private int _ticks;

    public int ProcessorCount => 4;

    /// <summary>Git's bash.exe gains 0.4 s of CPU per sample.</summary>
    public IReadOnlyList<Domain.Monitoring.ProcessSample> GetProcesses() =>
        [new(42, @"C:\Program Files\Git\bin\bash.exe", TimeSpan.FromSeconds(0.4 * ++_ticks), 64L << 20)];

    public Domain.Monitoring.MemoryStatus GetMemoryStatus() => new(16L << 30, 6L << 30);

    public static readonly ApplicationKey GitKey = ApplicationKey.ForRegistry(InstallScope.Machine, RegistryView.Native64, "Git_is1");

    public List<string> Calls { get; } = [];

    public Task<IReadOnlyList<InstalledApplication>> GetInstalledApplicationsAsync(CancellationToken cancellationToken)
    {
        UninstallCommand.TryParse(@"""C:\Program Files\Git\unins000.exe""", out var uninstall);
        IReadOnlyList<InstalledApplication> apps =
        [
            new() { Key = GitKey, Name = "Git", Publisher = "The Git Development Community", Version = "2.45.0", Origin = ApplicationOrigin.Registry, Scope = InstallScope.Machine, Uninstall = uninstall, InstallLocation = @"C:\Program Files\Git", EstimatedSizeBytes = 300L << 20 },
        ];
        return Task.FromResult(apps);
    }

    public Task<IReadOnlyList<InstalledPackage>> ListInstalledAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<InstalledPackage>>([new("Git", PackageId.Parse("Git.Git"), "2.45.0", "2.47.1", "winget")]);

    public Task<IReadOnlyList<PackageSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PackageSearchResult>>([new("VLC media player", PackageId.Parse("VideoLAN.VLC"), "3.0.21", "winget")]);

    public Task<OperationResult> InstallAsync(InstallRequest request, IProgress<string>? progress, CancellationToken cancellationToken) =>
        Record($"install {request.Id}");

    public Task<OperationResult> UpgradeAsync(PackageId id, IProgress<string>? progress, CancellationToken cancellationToken) =>
        Record($"upgrade {id}");

    public Task<OperationResult> UninstallAsync(PackageId id, bool silent, IProgress<string>? progress, CancellationToken cancellationToken) =>
        Record($"uninstall {id} silent={silent}");

    public Task<OperationResult> RunAsync(UninstallCommand command, bool silent, CancellationToken cancellationToken) =>
        Record($"run {command.FileName}");

    private Task<OperationResult> Record(string call)
    {
        Calls.Add(call);
        return Task.FromResult(OperationResult.Success($"{call}: done."));
    }
}
