using System.Text.Json;
using System.Text.Json.Nodes;

namespace SoftwareDock.Mcp;

/// <summary>
/// The <c>.mcp.json</c> fragment that connects a client to this server — what
/// <c>SoftwareDock.Mcp --print-config</c> prints and the dock's "Copy MCP config" copies.
/// </summary>
public static class McpClientConfig
{
    public const string ServerName = "software-dock";

    public static string Build(string executablePath, bool readOnly)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        var args = new JsonArray();
        if (readOnly)
        {
            args.Add("--read-only");
        }

        var root = new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                [ServerName] = new JsonObject
                {
                    ["type"] = "stdio",
                    ["command"] = executablePath,
                    ["args"] = args,
                },
            },
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
