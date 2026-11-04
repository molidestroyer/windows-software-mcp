using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using SoftwareDock.Mcp.Tools;

namespace SoftwareDock.Mcp;

public static class McpServerRegistration
{
    /// <summary>
    /// The MCP server minus its transport, so the stdio entry point and the in-process
    /// end-to-end tests run exactly the same server.
    /// </summary>
    /// <param name="services">Must already contain the application use cases.</param>
    /// <param name="readOnly">Leaves out the tools that change the machine.</param>
    public static IMcpServerBuilder AddSoftwareDockMcpServer(this IServiceCollection services, bool readOnly)
    {
        var builder = services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = McpClientConfig.ServerName,
                    Title = "Software Dock",
                    Version = typeof(McpServerRegistration).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
                    Description = "Installed software inventory, WinGet catalog search, install, upgrade and uninstall for this Windows PC.",
                };
                options.ServerInstructions = readOnly
                    ? ServerInstructions.Text + "\nThis server runs in read-only mode: install, upgrade and uninstall are not available."
                    : ServerInstructions.Text;
            })
            .WithTools<SoftwareInventoryTools>()
            .WithTools<SoftwareInsightTools>();

        return readOnly ? builder : builder.WithTools<SoftwareChangeTools>();
    }
}
