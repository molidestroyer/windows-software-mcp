using SoftwareDock.Domain.Applications;

namespace SoftwareDock.Domain.Usage;

/// <summary>
/// Which installed applications count as "not used" — a hint the user may act on by uninstalling,
/// so it must not fire on software that is clearly in use.
/// </summary>
/// <remarks>
/// Launch history only records programs started from Explorer, the Start menu or the taskbar. An
/// application that starts with Windows, or is running right now, is in use however long ago it
/// was last clicked (QA, 0.3.0: Ollama showed "170 days unused" while running from a Startup
/// shortcut). Parts of Windows are never suggested either.
/// </remarks>
public static class UnusedApplications
{
    public static readonly TimeSpan DefaultThreshold = TimeSpan.FromDays(90);

    // Ship with Windows or are needed by other software; "unused" would be bad advice.
    private static readonly string[] SystemPrefixes =
    [
        "Microsoft Edge",
        "Microsoft Update Health Tools",
        "Microsoft Visual C++",
        "Microsoft .NET",
        "Microsoft Windows Desktop Runtime",
        "Microsoft ASP.NET Core",
        "Windows Software Development Kit",
        "Windows SDK",
    ];

    /// <param name="applications">Installed applications.</param>
    /// <param name="usage">Last use per application, from launch history.</param>
    /// <param name="now">Current time.</param>
    /// <param name="inUse">Applications running now or enabled at startup.</param>
    /// <param name="threshold">How long without use counts as unused (default 90 days).</param>
    public static IReadOnlyDictionary<ApplicationKey, ApplicationUsage> Find(
        IEnumerable<InstalledApplication> applications,
        IReadOnlyDictionary<ApplicationKey, ApplicationUsage> usage,
        DateTimeOffset now,
        IReadOnlySet<ApplicationKey> inUse,
        TimeSpan? threshold = null)
    {
        ArgumentNullException.ThrowIfNull(applications);
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(inUse);

        var limit = threshold ?? DefaultThreshold;
        return applications
            .Where(a => usage.TryGetValue(a.Key, out var u) && now - u.LastUsed > limit)
            .Where(a => !inUse.Contains(a.Key) && !IsPartOfWindows(a))
            .ToDictionary(a => a.Key, a => usage[a.Key]);
    }

    public static bool IsPartOfWindows(InstalledApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        return SystemPrefixes.Any(p => application.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }
}
