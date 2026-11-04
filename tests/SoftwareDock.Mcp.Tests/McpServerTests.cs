using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace SoftwareDock.Mcp.Tests;

public class McpServerTests
{
    private static readonly string[] AllTools =
    [
        "export_software_backup", "get_security_advisories", "get_software_details", "install_software",
        "list_installed_software", "list_running_software", "list_startup_apps", "list_unused_software",
        "search_software_catalog", "set_startup_app_enabled", "uninstall_software", "upgrade_software",
    ];

    [Fact]
    public async Task Initialize_GivesTheAgentInstructionsToDiscoverHowToUseIt()
    {
        await using var server = await McpServerFixture.StartAsync();

        Assert.Equal("software-dock", server.Client.ServerInfo!.Name);
        var instructions = server.Client.ServerInstructions;
        Assert.NotNull(instructions);
        Assert.All(AllTools, tool => Assert.Contains(tool, instructions, StringComparison.Ordinal));
        Assert.Contains("confirmation", instructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListTools_DescribesEveryToolWithAnnotations()
    {
        await using var server = await McpServerFixture.StartAsync();

        var tools = await server.Client.ListToolsAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(AllTools, tools.Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.All(tools, t => Assert.False(string.IsNullOrWhiteSpace(t.Description)));
        Assert.True(tools.Single(t => t.Name == "uninstall_software").ProtocolTool.Annotations!.DestructiveHint);
        Assert.True(tools.Single(t => t.Name == "list_installed_software").ProtocolTool.Annotations!.ReadOnlyHint);
    }

    [Fact]
    public async Task ReadOnlyMode_ExposesNoToolThatChangesTheMachine()
    {
        await using var server = await McpServerFixture.StartAsync(readOnly: true);

        var tools = await server.Client.ListToolsAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(
            ["export_software_backup", "get_security_advisories", "get_software_details", "list_installed_software",
             "list_running_software", "list_startup_apps", "list_unused_software", "search_software_catalog"],
            tools.Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.Contains("read-only", server.Client.ServerInstructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListInstalledSoftware_ReturnsKeysAndUpdates()
    {
        await using var server = await McpServerFixture.StartAsync();

        var result = await server.CallAsync("list_installed_software", new() { ["onlyUpdates"] = true });

        Assert.NotEqual(true, result.IsError);
        using var json = JsonDocument.Parse(Text(result));
        var item = json.RootElement.GetProperty("items")[0];
        Assert.Equal(FakeMachine.GitKey.Value, item.GetProperty("key").GetString());
        Assert.Equal("2.47.1", item.GetProperty("availableVersion").GetString());
        Assert.True(item.GetProperty("canUpgrade").GetBoolean());
    }

    [Fact]
    public async Task UpgradeAndUninstall_TakeTheKeyFromTheListing()
    {
        await using var server = await McpServerFixture.StartAsync();

        var upgrade = await server.CallAsync("upgrade_software", new() { ["key"] = FakeMachine.GitKey.Value });
        var uninstall = await server.CallAsync("uninstall_software", new() { ["key"] = FakeMachine.GitKey.Value });

        Assert.Contains("\"succeeded\":true", Text(upgrade), StringComparison.Ordinal);
        Assert.Contains("\"succeeded\":true", Text(uninstall), StringComparison.Ordinal);
        Assert.Equal(["upgrade Git.Git", "uninstall Git.Git silent=True"], server.Machine.Calls);
    }

    [Fact]
    public async Task SearchThenInstall_UsesThePackageId()
    {
        await using var server = await McpServerFixture.StartAsync();

        var search = await server.CallAsync("search_software_catalog", new() { ["query"] = "vlc" });
        using var json = JsonDocument.Parse(Text(search));
        var packageId = json.RootElement[0].GetProperty("packageId").GetString();

        await server.CallAsync("install_software", new() { ["packageId"] = packageId });

        Assert.Equal(["install VideoLAN.VLC"], server.Machine.Calls);
    }

    [Theory]
    [InlineData("--override")]
    [InlineData("VideoLAN.VLC\" --override \"x")]
    public async Task Install_RejectsArgumentInjection_WithoutRunningAnything(string packageId)
    {
        await using var server = await McpServerFixture.StartAsync();

        var result = await server.CallAsync("install_software", new() { ["packageId"] = packageId });

        Assert.True(result.IsError);
        Assert.Empty(server.Machine.Calls);
    }

    [Fact]
    public async Task UnknownKey_IsAnErrorThatTellsTheAgentWhatToDo()
    {
        await using var server = await McpServerFixture.StartAsync();

        var result = await server.CallAsync("get_software_details", new() { ["key"] = @"ARP\Machine\X64\{nope}" });

        Assert.True(result.IsError);
        Assert.Contains("list_installed_software", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListRunningSoftware_ReportsCpuAndMemoryPerApplication()
    {
        await using var server = await McpServerFixture.StartAsync();

        var result = await server.CallAsync("list_running_software", new() { ["sortBy"] = "cpu" });

        using var json = JsonDocument.Parse(Text(result));
        var git = json.RootElement.GetProperty("items")[0];
        Assert.Equal("Git", git.GetProperty("name").GetString());
        Assert.Equal(64, git.GetProperty("memoryMb").GetInt64());
        Assert.True(git.GetProperty("cpuPercent").GetDouble() > 0);
        Assert.Equal(16384, json.RootElement.GetProperty("memoryTotalMb").GetInt64());
    }

    [Theory]
    [InlineData("winget", "Microsoft.Winget.Source_8wekyb3d8bbwe")]
    [InlineData("softwaredock", "software-dock-backup")]
    public async Task ExportSoftwareBackup_ReturnsARestorableFile(string format, string marker)
    {
        await using var server = await McpServerFixture.StartAsync();

        var result = await server.CallAsync("export_software_backup", new() { ["format"] = format });

        Assert.NotEqual(true, result.IsError);
        Assert.Contains(marker, Text(result), StringComparison.Ordinal);
        Assert.Contains("Git.Git", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartupApps_AreListedWithTheirAppAndCanBeDisabled()
    {
        await using var server = await McpServerFixture.StartAsync();

        var list = await server.CallAsync("list_startup_apps", []);
        using var json = JsonDocument.Parse(Text(list));
        var entry = json.RootElement[0];
        Assert.Equal("Git", entry.GetProperty("name").GetString());

        await server.CallAsync("set_startup_app_enabled", new() { ["id"] = entry.GetProperty("id").GetString(), ["enabled"] = false });

        Assert.Equal(["startup GitUpdater=False"], server.Machine.Calls);
    }

    [Fact]
    public async Task SecurityAdvisories_FlagExploitedAppsWithUpdates()
    {
        await using var server = await McpServerFixture.StartAsync();

        var result = await server.CallAsync("get_security_advisories", []);

        Assert.Contains("CVE-2026-4242", Text(result), StringComparison.Ordinal);
        Assert.Contains(FakeMachine.GitKey.Value.Replace(@"\", @"\\", StringComparison.Ordinal), Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnusedSoftware_NeverListsAnAppThatIsRunning()
    {
        // Git was last opened 200 days ago, but its bash.exe is running right now (QA, 0.3.0).
        await using var server = await McpServerFixture.StartAsync();

        var result = await server.CallAsync("list_unused_software", new() { ["days"] = 90 });

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("[]", Text(result).Replace(" ", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void PrintConfig_IsAValidMcpJsonFragment()
    {
        using var json = JsonDocument.Parse(McpClientConfig.Build(@"C:\Tools\SoftwareDock\SoftwareDock.Mcp.exe", readOnly: true));

        var server = json.RootElement.GetProperty("mcpServers").GetProperty("software-dock");
        Assert.Equal("stdio", server.GetProperty("type").GetString());
        Assert.Equal(@"C:\Tools\SoftwareDock\SoftwareDock.Mcp.exe", server.GetProperty("command").GetString());
        Assert.Equal("--read-only", server.GetProperty("args")[0].GetString());
    }

    private static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
}
