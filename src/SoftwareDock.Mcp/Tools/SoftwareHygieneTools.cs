using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SoftwareDock.Application.Insights;
using SoftwareDock.Application.Inventory;
using SoftwareDock.Application.Startup;

namespace SoftwareDock.Mcp.Tools;

/// <summary>Keeping a PC healthy: what starts with Windows, what is exploitable, what is never used.</summary>
[McpServerToolType]
public sealed class SoftwareHygieneTools(StartupManager startup, SoftwareInsights insights, ISoftwareInventory inventory, TimeProvider time)
{
    [McpServerTool(Name = "list_startup_apps", Title = "Apps that start with Windows", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Programs Windows starts at sign-in (same list as Task Manager › Startup apps), whether each is enabled, and the installed app it belongs to. Returns `id` for set_startup_app_enabled.")]
    public async Task<IReadOnlyList<StartupAppDto>> ListStartupAppsAsync(CancellationToken cancellationToken = default)
    {
        var entries = await startup.ListAsync(cancellationToken);
        return entries.Select(e => new StartupAppDto(e.Item.Id, e.DisplayName, e.Item.IsEnabled, e.Item.Location.ToString(), e.Item.IsPerUser, e.Item.Command)).ToList();
    }

    [McpServerTool(Name = "get_security_advisories", Title = "Installed apps under active attack", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Installed applications that have an update pending and appear in CISA's Known Exploited Vulnerabilities catalog with recent entries — the updates worth doing first. Use upgrade_software with the `key`.")]
    public async Task<IReadOnlyList<SecurityAdvisoryDto>> GetSecurityAdvisoriesAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await inventory.GetSnapshotAsync(forceRefresh: false, cancellationToken);
        var advisories = await insights.GetSecurityAdvisoriesAsync(snapshot, cancellationToken);
        return advisories
            .Select(a => (App: snapshot.Find(a.Key)!, Advisory: a.Value))
            .OrderByDescending(x => x.Advisory.LatestAdded)
            .Select(x => new SecurityAdvisoryDto(
                x.App.Key.Value,
                x.App.Name,
                x.App.Version,
                x.App.AvailableVersion,
                x.Advisory.UsedByRansomware,
                x.Advisory.Vulnerabilities.Select(v => $"{v.CveId}: {v.Title} (added {v.DateAdded:yyyy-MM-dd})").ToList()))
            .ToList();
    }

    [McpServerTool(Name = "list_unused_software", Title = "Software not used in a long time", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Installed applications the user has not opened for at least `days` days, largest first — candidates to uninstall. Based on Windows' own launch history (Start menu, taskbar, Explorer); apps running now, enabled at startup or part of Windows are never listed.")]
    public async Task<IReadOnlyList<UnusedSoftwareDto>> ListUnusedSoftwareAsync(
        [Description("Minimum days since last use (default 90).")] int days = 90,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await inventory.GetSnapshotAsync(forceRefresh: false, cancellationToken);
        var unused = await insights.GetUnusedAsync(snapshot, TimeSpan.FromDays(Math.Max(1, days)), cancellationToken);
        var now = time.GetUtcNow();
        return unused
            .Select(u => (App: snapshot.Find(u.Key)!, Usage: u.Value))
            .Where(x => x.App is not null)
            .OrderByDescending(x => x.App.EstimatedSizeBytes ?? 0)
            .Select(x => new UnusedSoftwareDto(x.App.Key.Value, x.App.Name, x.Usage.LastUsed, (int)(now - x.Usage.LastUsed).TotalDays, x.App.EstimatedSizeBytes / (1024 * 1024)))
            .ToList();
    }
}

public sealed record StartupAppDto(string Id, string Name, bool Enabled, string Location, bool PerUser, string Command);

public sealed record SecurityAdvisoryDto(string Key, string Name, string? InstalledVersion, string? AvailableVersion, bool UsedByRansomware, IReadOnlyList<string> ExploitedVulnerabilities);

public sealed record UnusedSoftwareDto(string Key, string Name, DateTimeOffset LastUsed, int DaysUnused, long? SizeMb);
