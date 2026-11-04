using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SoftwareDock.Application;
using SoftwareDock.Application.Inventory;
using SoftwareDock.Infrastructure;
using SoftwareDock.Infrastructure.WinGet;
using SoftwareDock.Mcp;

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
builder.Services.AddSoftwareDockMcpServer(readOnly).WithStdioServerTransport();

await builder.Build().RunAsync();
return 0;
