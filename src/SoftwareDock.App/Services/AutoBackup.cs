using Microsoft.Extensions.Logging;
using SoftwareDock.App.Settings;
using SoftwareDock.Application.Backup;
using SoftwareDock.Application.Inventory;
using SoftwareDock.Domain.Backup;

namespace SoftwareDock.App.Services;

/// <summary>
/// Keeps an up-to-date backup next to the user's documents: every time the inventory changes, both
/// formats are rewritten — but only when the software actually changed, so the files' dates say
/// when something was installed or removed, not when the dock last looked.
/// </summary>
public sealed partial class AutoBackup : IDisposable
{
    public const string BackupFileName = "software-backup.json";
    public const string WinGetFileName = "software-winget.json";

    private readonly ISoftwareInventory _inventory;
    private readonly IUserSettingsStore _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<AutoBackup> _logger;
    private SoftwareManifest? _lastWritten;

    public AutoBackup(ISoftwareInventory inventory, IUserSettingsStore settings, TimeProvider time, ILogger<AutoBackup> logger)
    {
        _inventory = inventory;
        _settings = settings;
        _time = time;
        _logger = logger;
        _inventory.SnapshotChanged += OnSnapshotChanged;
    }

    /// <summary>Path of the last file written, for the settings page.</summary>
    public string? LastBackupPath { get; private set; }

    public DateTimeOffset? LastBackupAt { get; private set; }

    public void Dispose() => _inventory.SnapshotChanged -= OnSnapshotChanged;

    /// <summary>Writes the backup for <paramref name="snapshot"/> if enabled and different from the last one.</summary>
    public bool TryWrite(InventorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var settings = _settings.Current;
        if (!settings.AutomaticBackup || snapshot.IsPartial)
        {
            // A partial inventory (WinGet unavailable) would overwrite a good backup with a poorer one.
            return false;
        }

        var manifest = SoftwareManifest.From(snapshot.Applications, _time.GetUtcNow(), Environment.MachineName);
        var folder = settings.EffectiveBackupFolder;
        var path = Path.Combine(folder, BackupFileName);
        if (manifest.HasSameContentAs(_lastWritten ?? ReadExisting(path)))
        {
            _lastWritten = manifest;
            return false;
        }

        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, SoftwareManifestJson.Write(manifest, BackupFormat.SoftwareDock));
            File.WriteAllText(Path.Combine(folder, WinGetFileName), SoftwareManifestJson.Write(manifest, BackupFormat.WinGet));
            _lastWritten = manifest;
            LastBackupPath = path;
            LastBackupAt = manifest.CreatedAt;
            LogWritten(path, manifest.Packages.Count, manifest.Unmanaged.Count);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogWriteFailed(ex, path);
            return false;
        }
    }

    private void OnSnapshotChanged(object? sender, InventorySnapshot snapshot) => TryWrite(snapshot);

    private static SoftwareManifest? ReadExisting(string path)
    {
        try
        {
            return File.Exists(path) ? SoftwareManifestJson.Read(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Backup written to {Path}: {Packages} packages, {Manual} to install by hand")]
    private partial void LogWritten(string path, int packages, int manual);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not write the backup to {Path}")]
    private partial void LogWriteFailed(Exception exception, string path);
}
