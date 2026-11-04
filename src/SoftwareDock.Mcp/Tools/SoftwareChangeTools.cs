using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SoftwareDock.Application.Abstractions;
using SoftwareDock.Application.Operations;
using SoftwareDock.Domain.Packages;

namespace SoftwareDock.Mcp.Tools;

/// <summary>Tools that change the machine. Not registered when the server runs with <c>--read-only</c>.</summary>
[McpServerToolType]
public sealed class SoftwareChangeTools(ISoftwareOperations operations)
{
    [McpServerTool(Name = "install_software", Title = "Install software", Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Installs a package from the catalog, silently. Confirm with the user first. Windows may ask them to approve elevation.")]
    public async Task<OperationResultDto> InstallSoftwareAsync(
        [Description("The exact `packageId` returned by search_software_catalog.")] string packageId,
        [Description("A specific version. Omit for the latest.")] string? version = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var request = new InstallRequest(ParsePackageId(packageId), string.IsNullOrWhiteSpace(version) ? null : version);
        return await RunAsync(() => operations.InstallAsync(request, Relay(progress), cancellationToken));
    }

    [McpServerTool(Name = "upgrade_software", Title = "Upgrade installed software", Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Upgrades an installed application to the latest available version. Only works when list_installed_software reports canUpgrade=true.")]
    public async Task<OperationResultDto> UpgradeSoftwareAsync(
        [Description("The `key` returned by list_installed_software.")] string key,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default) =>
        await RunAsync(() => operations.UpgradeAsync(SoftwareInventoryTools.ParseKey(key), Relay(progress), cancellationToken));

    [McpServerTool(Name = "uninstall_software", Title = "Uninstall software", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Removes an installed application. Destructive: name the exact application to the user and get their confirmation first.")]
    public async Task<OperationResultDto> UninstallSoftwareAsync(
        [Description("The `key` returned by list_installed_software.")] string key,
        [Description("Run without the uninstaller's UI when a silent method is known. When false, the user may have to click through the uninstaller.")] bool silent = true,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default) =>
        await RunAsync(() => operations.UninstallAsync(
            SoftwareInventoryTools.ParseKey(key),
            silent ? UninstallMode.Silent : UninstallMode.Interactive,
            Relay(progress),
            cancellationToken));

    private static async Task<OperationResultDto> RunAsync(Func<Task<Domain.Operations.OperationResult>> operation)
    {
        try
        {
            return OperationResultDto.From(await operation());
        }
        catch (PackageManagerUnavailableException ex)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    private static PackageId ParsePackageId(string value) =>
        PackageId.TryParse(value, out var id, out var error) ? id : throw new McpException(error);

    /// <summary>Forwards installer output lines as MCP progress notifications.</summary>
    private static Progress<string>? Relay(IProgress<ProgressNotificationValue>? progress)
    {
        if (progress is null)
        {
            return null;
        }

        var step = 0;
        return new Progress<string>(line => progress.Report(new ProgressNotificationValue { Progress = ++step, Message = line }));
    }
}
