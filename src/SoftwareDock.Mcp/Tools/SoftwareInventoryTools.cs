using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SoftwareDock.Application.Abstractions;
using SoftwareDock.Application.Inventory;
using SoftwareDock.Domain.Applications;

namespace SoftwareDock.Mcp.Tools;

/// <summary>Read-only tools: always registered, safe to call without asking.</summary>
[McpServerToolType]
public sealed class SoftwareInventoryTools(ISoftwareInventory inventory, IPackageManager packageManager)
{
    private const int MaxPageSize = 200;

    [McpServerTool(Name = "list_installed_software", Title = "List installed software", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists the applications installed on this PC (the same list as Settings › Installed apps, plus Store apps), with their version and whether an update is available. Returns `key` for each one.")]
    public async Task<InventoryPageDto> ListInstalledSoftwareAsync(
        [Description("Case-insensitive text matched against name, publisher and package id. Omit to list everything.")] string? filter = null,
        [Description("Only applications with an update available.")] bool onlyUpdates = false,
        [Description("Maximum number of items to return (1-200).")] int limit = 50,
        [Description("Rebuild the inventory instead of using the cached one.")] bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await inventory.GetSnapshotAsync(refresh, cancellationToken);
        var matches = snapshot.Query(filter).Where(a => !onlyUpdates || a.HasUpdate).ToList();
        var page = matches.Take(Math.Clamp(limit, 1, MaxPageSize)).Select(InstalledSoftwareDto.From).ToList();
        return new InventoryPageDto(matches.Count, page.Count, snapshot.UpdateCount, snapshot.Warnings, page);
    }

    [McpServerTool(Name = "get_software_details", Title = "Installed software details", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Full details of one installed application: install location, date, size and how it would be uninstalled.")]
    public async Task<SoftwareDetailsDto> GetSoftwareDetailsAsync(
        [Description("The `key` returned by list_installed_software.")] string key,
        CancellationToken cancellationToken = default)
    {
        var application = await inventory.FindAsync(ParseKey(key), cancellationToken)
            ?? throw new McpException($"No installed application has the key '{key}'. Call list_installed_software to get a current key.");
        return SoftwareDetailsDto.From(application);
    }

    [McpServerTool(Name = "search_software_catalog", Title = "Search the WinGet catalog", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Searches the WinGet and Microsoft Store catalogs for software that can be installed. Returns `packageId` for each result.")]
    public async Task<IReadOnlyList<CatalogPackageDto>> SearchSoftwareCatalogAsync(
        [Description("Name, publisher or keyword, e.g. 'vlc', 'visual studio code', 'Mozilla'.")] string query,
        [Description("Maximum number of results (1-50).")] int limit = 15,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new McpException("The search query cannot be empty.");
        }

        try
        {
            var results = await packageManager.SearchAsync(query, Math.Clamp(limit, 1, 50), cancellationToken);
            return results.Select(CatalogPackageDto.From).ToList();
        }
        catch (PackageManagerUnavailableException ex)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    internal static ApplicationKey ParseKey(string key) =>
        string.IsNullOrWhiteSpace(key)
            ? throw new McpException("The key cannot be empty. Use a `key` returned by list_installed_software.")
            : ApplicationKey.Parse(key);
}
