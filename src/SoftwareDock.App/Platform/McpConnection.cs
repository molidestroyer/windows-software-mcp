using SoftwareDock.Mcp;

namespace SoftwareDock.App.Platform;

/// <summary>Builds the snippet that connects an MCP client to the server shipped next to the dock.</summary>
public static class McpConnection
{
    public static string ClientConfig()
    {
        var directory = AppContext.BaseDirectory;
        var executable = Path.Combine(directory, "SoftwareDock.Mcp.exe");
        return McpClientConfig.Build(File.Exists(executable) ? executable : Path.Combine(directory, "SoftwareDock.Mcp"), readOnly: false);
    }
}
