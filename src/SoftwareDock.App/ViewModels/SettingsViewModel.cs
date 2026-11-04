using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoftwareDock.App.Platform;
using SoftwareDock.App.Services;
using SoftwareDock.App.Settings;
using SoftwareDock.Application.Backup;
using SoftwareDock.Domain.Backup;
using SoftwareDock.Domain.Updates;

namespace SoftwareDock.App.ViewModels;

/// <summary>The ⚙️ page: startup, monitoring, backups, AI agents and the dock's own updates.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IUserSettingsStore _store;
    private readonly IStartupRegistration _startup;
    private readonly SoftwareBackup _backup;
    private readonly AutoBackup _autoBackup;
    private readonly IShellServices _shell;
    private readonly IAppUpdater _updater;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _messageIsError;

    [ObservableProperty]
    private string? _appUpdateStatus;

    public SettingsViewModel(
        IUserSettingsStore store,
        IStartupRegistration startup,
        SoftwareBackup backup,
        AutoBackup autoBackup,
        IShellServices shell,
        IAppUpdater updater)
    {
        _store = store;
        _startup = startup;
        _backup = backup;
        _autoBackup = autoBackup;
        _shell = shell;
        _updater = updater;
        _store.Changed += (_, _) => OnPropertyChanged(string.Empty);
    }

    /// <summary>Raised with a backup the user picked, to open the restore page.</summary>
    public event EventHandler<SoftwareManifest>? RestoreRequested;

    /// <summary>Raised when the user wants an update found by "Check now" applied.</summary>
    public event EventHandler<AppUpdate>? AppUpdateReady;

    public bool StartWithWindows
    {
        get => _startup.IsEnabled;
        set
        {
            _startup.SetEnabled(value);
            OnPropertyChanged();
        }
    }

    public bool CanStartWithWindows => _startup.IsSupported;

    public bool ShowResourceUsage
    {
        get => _store.Current.ShowResourceUsage;
        set => _store.Update(s => s with { ShowResourceUsage = value });
    }

    public bool CheckForSoftwareUpdates
    {
        get => _store.Current.CheckForSoftwareUpdates;
        set => _store.Update(s => s with { CheckForSoftwareUpdates = value });
    }

    public string SoftwareUpdateInterval => $"Every {_store.Current.SoftwareUpdateCheckHours} h, while the dock is in the tray";

    public bool AutomaticBackup
    {
        get => _store.Current.AutomaticBackup;
        set => _store.Update(s => s with { AutomaticBackup = value });
    }

    public string BackupFolder => _store.Current.EffectiveBackupFolder;

    public string LastBackupText => _autoBackup.LastBackupAt is { } at
        ? $"Last saved {at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)} — updated whenever your software changes"
        : "Saved whenever your software changes";

    public bool Notifications
    {
        get => _store.Current.Notifications;
        set => _store.Update(s => s with { Notifications = value });
    }

    public bool ElevateBatches
    {
        get => _store.Current.ElevateBatches;
        set => _store.Update(s => s with { ElevateBatches = value });
    }

    public bool SecurityAlerts
    {
        get => _store.Current.SecurityAlerts;
        set => _store.Update(s => s with { SecurityAlerts = value });
    }

    public IReadOnlyList<ScheduleOption> ScheduleOptions => ScheduleOption.All;

    public ScheduleOption SelectedSchedule
    {
        get => ScheduleOption.All.First(o => o.Mode == _store.Current.ScheduledUpdates);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _store.Update(s => s with { ScheduledUpdates = value.Mode });
        }
    }

    public string ScheduleHint => _store.Current.ScheduledUpdates == UpdateScheduleMode.Off
        ? "Updates wait for you to press Update."
        : $"At {_store.Current.ScheduledUpdateHour:00}:00, or at the next start if the PC was off. Pinned apps and skipped versions are left alone; apps that need administrator rights will ask when you are back.";

    public bool AutoUpdateApp
    {
        get => _store.Current.AutoUpdateApp && CanUpdateApp;
        set => _store.Update(s => s with { AutoUpdateApp = value });
    }

    public string AppVersion => $"Software Dock {_updater.CurrentVersion}";

    public bool CanUpdateApp => _updater.IsSupported;

    [RelayCommand]
    private Task ExportBackupAsync() => ExportAsync(BackupFormat.SoftwareDock, $"software-backup-{Environment.MachineName}.json");

    [RelayCommand]
    private Task ExportWinGetAsync() => ExportAsync(BackupFormat.WinGet, $"winget-{Environment.MachineName}.json");

    [RelayCommand]
    private async Task ImportBackupAsync()
    {
        var path = await _shell.PickOpenFileAsync("Restore software from a backup");
        if (path is null)
        {
            return;
        }

        try
        {
            RestoreRequested?.Invoke(this, SoftwareManifestJson.Read(await File.ReadAllTextAsync(path)));
            Say(null);
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            Say($"Could not read the backup: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private Task OpenBackupFolderAsync() => _shell.OpenFolderAsync(BackupFolder);

    [RelayCommand]
    private async Task CopyMcpConfigAsync()
    {
        await _shell.CopyTextAsync(McpConnection.ClientConfig());
        Say("MCP config copied — paste it into your agent's .mcp.json.");
    }

    [RelayCommand]
    private async Task CheckAppUpdateAsync()
    {
        AppUpdateStatus = "Checking…";
        try
        {
            var update = await _updater.CheckAsync(CancellationToken.None);
            if (update is null)
            {
                AppUpdateStatus = "You have the latest version.";
                return;
            }

            AppUpdateStatus = $"Downloading {update.Version}…";
            await _updater.DownloadAsync(update, new Progress<int>(p => AppUpdateStatus = $"Downloading {update.Version}… {p}%"), CancellationToken.None);
            AppUpdateStatus = $"Version {update.Version} is ready.";
            AppUpdateReady?.Invoke(this, update);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            AppUpdateStatus = $"Could not check for updates: {ex.Message}";
        }
    }

    private async Task ExportAsync(BackupFormat format, string suggestedName)
    {
        var path = await _shell.PickSaveFileAsync(format == BackupFormat.WinGet ? "Export for winget import" : "Export software backup", suggestedName);
        if (path is null)
        {
            return;
        }

        var manifest = await _backup.CaptureAsync(CancellationToken.None);
        await File.WriteAllTextAsync(path, SoftwareManifestJson.Write(manifest, format));
        Say(format == BackupFormat.WinGet
            ? $"Saved {manifest.Packages.Count} packages. Restore anywhere with: winget import -i \"{Path.GetFileName(path)}\""
            : $"Saved {manifest.Packages.Count} packages and {manifest.Unmanaged.Count} apps to reinstall by hand.");
    }

    private void Say(string? message, bool isError = false)
    {
        Message = message;
        MessageIsError = isError;
    }
}

public sealed record ScheduleOption(UpdateScheduleMode Mode, string Label)
{
    public static IReadOnlyList<ScheduleOption> All { get; } =
    [
        new(UpdateScheduleMode.Off, "Off"),
        new(UpdateScheduleMode.Daily, "Every night"),
        new(UpdateScheduleMode.Weekly, "Every Sunday night"),
    ];

    public override string ToString() => Label;
}
