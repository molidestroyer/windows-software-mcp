using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using SoftwareDock.Application;
using SoftwareDock.Application.Inventory;
using SoftwareDock.Infrastructure;
using SoftwareDock.Infrastructure.WinGet;
using SoftwareDock.Mcp;
using SoftwareDock.Mcp.Tools;

var readOnly = args.Contains("--read-only", StringComparer.OrdinalIgnoreCase);

if (args.Contains("--print-config", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine(McpClientConfig.Build(Environment.ProcessPath ?? "SoftwareDock.Mcp.exe", readOnly));
    return 0;
}

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// stdout carries the protocol. A single log line written there corrupts the stream, so every
// log level goes to stderr, which MCP clients surface as server logs.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.Configure<InventoryOptions>(builder.Configuration.GetSection(InventoryOptions.SectionName));
builder.Services.Configure<WinGetOptions>(builder.Configuration.GetSection(WinGetOptions.SectionName));
builder.Services.AddSoftwareDockApplication();
builder.Services.AddSoftwareDockWindowsInfrastructure();

var mcp = builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation
        {
            Name = McpClientConfig.ServerName,
            Title = "Software Dock",
            Version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            Description = "Installed software inventory, WinGet catalog search, install, upgrade and uninstall for this Windows PC.",
        };
        options.ServerInstructions = readOnly
            ? ServerInstructions.Text + "\nThis server runs in read-only mode: install, upgrade and uninstall are not available."
            : ServerInstructions.Text;
    })
    .WithStdioServerTransport()
    .WithTools<SoftwareInventoryTools>();

if (!readOnly)
{
    mcp.WithTools<SoftwareChangeTools>();
}

await builder.Build().RunAsync();
return 0;
