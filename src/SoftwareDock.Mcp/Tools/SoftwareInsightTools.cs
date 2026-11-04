using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SoftwareDock.Application.Backup;
using SoftwareDock.Application.Inventory;
using SoftwareDock.Application.Monitoring;

namespace SoftwareDock.Mcp.Tools;

/// <summary>Read-only tools beyond the inventory: what is running, and a backup of what is installed.</summary>
[McpServerToolType]
public sealed class SoftwareInsightTools(ISoftwareInventory inventory, ResourceMonitor monitor, SoftwareBackup backup)
{
    private static readonly TimeSpan SampleWindow = TimeSpan.FromSeconds(1);

    [McpServerTool(Name = "list_running_software", Title = "Running software and its resource usage", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Installed applications that are running now, with CPU (% of the whole machine) and memory, heaviest first. Takes about a second to measure CPU.")]
    public async Task<RunningSoftwareDto> ListRunningSoftwareAsync(
        [Description("Sort by 'memory' or 'cpu'.")] string sortBy = "memory",
        [Description("Maximum number of items (1-100).")] int limit = 15,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await inventory.GetSnapshotAsync(forceRefresh: false, cancellationToken);

        // CPU is a rate: it needs two samples some time apart.
        monitor.Reset();
        monitor.Sample(snapshot.Applications);
        await Task.Delay(SampleWindow, cancellationToken);
        var usage = monitor.Sample(snapshot.Applications);

        var running = snapshot.Applications
            .Select(a => (App: a, Usage: usage.For(a.Key)))
            .Where(x => x.Usage.IsRunning);
        running = string.Equals(sortBy, "cpu", StringComparison.OrdinalIgnoreCase)
            ? running.OrderByDescending(x => x.Usage.CpuPercent)
            : running.OrderByDescending(x => x.Usage.MemoryBytes);

        var items = running
            .Take(Math.Clamp(limit, 1, 100))
            .Select(x => new RunningApplicationDto(x.App.Key.Value, x.App.Name, x.Usage.ProcessCount, Math.Round(x.Usage.CpuPercent, 1), x.Usage.MemoryBytes / (1024 * 1024)))
            .ToList();

        return new RunningSoftwareDto(
            Math.Round(usage.SystemCpuPercent, 1),
            usage.Memory.UsedBytes / (1024 * 1024),
            usage.Memory.TotalBytes / (1024 * 1024),
            items);
    }

    [McpServerTool(Name = "export_software_backup", Title = "Back up the installed software list", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Returns a JSON backup of the installed software. Format 'winget' restores anywhere with `winget import -i file.json`; format 'softwaredock' also lists the apps that must be reinstalled by hand. Offer to save it where the user keeps backups.")]
    public async Task<string> ExportSoftwareBackupAsync(
        [Description("'softwaredock' (default) or 'winget'.")] string format = "softwaredock",
        CancellationToken cancellationToken = default)
    {
        var target = format.Trim().ToUpperInvariant() switch
        {
            "SOFTWAREDOCK" => BackupFormat.SoftwareDock,
            "WINGET" => BackupFormat.WinGet,
            _ => throw new McpException($"Unknown format '{format}'. Use 'softwaredock' or 'winget'."),
        };

        return SoftwareManifestJson.Write(await backup.CaptureAsync(cancellationToken), target);
    }
}

public sealed record RunningApplicationDto(string Key, string Name, int Processes, double CpuPercent, long MemoryMb);

public sealed record RunningSoftwareDto(double SystemCpuPercent, long MemoryUsedMb, long MemoryTotalMb, IReadOnlyList<RunningApplicationDto> Items);
