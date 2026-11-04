namespace SoftwareDock.App.Settings;

public enum SortOrder
{
    Name,
    Size,
    RecentlyInstalled,
    MemoryUse,
}

/// <summary>
/// Preferences the user changes from the ⚙️ page. Stored per user, apart from
/// <c>appsettings.json</c>: the install folder is replaced on every update.
/// </summary>
public sealed record UserSettings
{
    public bool ShowResourceUsage { get; init; } = true;

    public bool CheckForSoftwareUpdates { get; init; } = true;

    public int SoftwareUpdateCheckHours { get; init; } = 6;

    public bool AutomaticBackup { get; init; } = true;

    /// <summary><see langword="null"/> = <c>Documents\Software Dock</c>, which OneDrive usually syncs.</summary>
    public string? BackupFolder { get; init; }

    public bool AutoUpdateApp { get; init; } = true;

    public SortOrder SortOrder { get; init; } = SortOrder.Name;

    public string EffectiveBackupFolder =>
        string.IsNullOrWhiteSpace(BackupFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Software Dock")
            : BackupFolder;
}
