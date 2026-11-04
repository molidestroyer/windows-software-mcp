using SoftwareDock.Domain.Updates;

namespace SoftwareDock.App.Settings;

public enum SortOrder
{
    Name,
    Size,
    RecentlyInstalled,
    MemoryUse,
    LeastRecentlyUsed,
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

    public bool Notifications { get; init; } = true;

    public bool SecurityAlerts { get; init; } = true;

    /// <summary>Bulk uninstall, Update all and restore ask for administrator rights once, for the whole batch.</summary>
    public bool ElevateBatches { get; init; }

    public UpdateScheduleMode ScheduledUpdates { get; init; } = UpdateScheduleMode.Off;

    public int ScheduledUpdateHour { get; init; } = 3;

    public DateTime? LastScheduledRun { get; init; }

    /// <summary>Application keys the user pinned to their current version.</summary>
    public IReadOnlyList<string> PinnedApplications { get; init; } = [];

    /// <summary>Application key → version the user chose to skip.</summary>
    public IReadOnlyDictionary<string, string> SkippedVersions { get; init; } = new Dictionary<string, string>();

    /// <summary>Application key → version already announced in a notification, so it is announced once.</summary>
    public IReadOnlyDictionary<string, string> AnnouncedUpdates { get; init; } = new Dictionary<string, string>();

    public string EffectiveBackupFolder =>
        string.IsNullOrWhiteSpace(BackupFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Software Dock")
            : BackupFolder;

    public UpdateRules UpdateRules => new(
        new HashSet<string>(PinnedApplications.Select(p => p.ToUpperInvariant()), StringComparer.OrdinalIgnoreCase),
        SkippedVersions.ToDictionary(s => s.Key.ToUpperInvariant(), s => s.Value, StringComparer.OrdinalIgnoreCase));

    public UpdateSchedule Schedule => new(ScheduledUpdates, ScheduledUpdateHour);

    public UserSettings WithRules(UpdateRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return this with
        {
            PinnedApplications = rules.Pinned.Order(StringComparer.OrdinalIgnoreCase).ToList(),
            SkippedVersions = rules.SkippedVersions.ToDictionary(s => s.Key, s => s.Value),
        };
    }
}
