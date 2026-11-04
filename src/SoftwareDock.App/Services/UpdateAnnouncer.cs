using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Security;
using SoftwareDock.Domain.Updates;

namespace SoftwareDock.App.Services;

/// <summary>
/// Decides what a background check should tell the user: each update once (per version), with
/// security updates first and called out as such. Silence is the default — a dock that nags gets
/// switched off.
/// </summary>
public static class UpdateAnnouncer
{
    public static (DockNotification? Notification, IReadOnlyDictionary<string, string> Announced) Announce(
        IEnumerable<InstalledApplication> applications,
        UpdateRules rules,
        IReadOnlyDictionary<ApplicationKey, SecurityAdvisory> advisories,
        IReadOnlyDictionary<string, string> alreadyAnnounced)
    {
        ArgumentNullException.ThrowIfNull(applications);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(advisories);
        ArgumentNullException.ThrowIfNull(alreadyAnnounced);

        var actionable = applications.Where(rules.IsActionable).ToList();
        var fresh = actionable
            .Where(a => !string.Equals(alreadyAnnounced.GetValueOrDefault(Key(a)), a.AvailableVersion, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => advisories.ContainsKey(a.Key))
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // Forget announcements for apps no longer pending, so a future version is announced again.
        var announced = actionable.ToDictionary(Key, a => alreadyAnnounced.GetValueOrDefault(Key(a)) ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        foreach (var app in fresh)
        {
            announced[Key(app)] = app.AvailableVersion!;
        }

        var cleaned = announced.Where(a => a.Value.Length > 0).ToDictionary(a => a.Key, a => a.Value, StringComparer.OrdinalIgnoreCase);
        if (fresh.Count == 0)
        {
            return (null, cleaned);
        }

        var security = fresh.Where(a => advisories.ContainsKey(a.Key)).ToList();
        var names = Names(fresh.Select(a => a.Name).ToList());
        var notification = security.Count > 0
            ? new DockNotification(
                security.Count == 1 ? $"Security update: {security[0].Name}" : $"{security.Count} security updates",
                security.Count == 1
                    ? $"{advisories[security[0].Key].Summary}. Update to {security[0].AvailableVersion}."
                    : $"Attackers are exploiting {Names(security.Select(a => a.Name).ToList())}. Update them first.",
                NotificationTarget.Updates,
                "Open updates")
            : new DockNotification(
                fresh.Count == 1 ? $"Update available: {fresh[0].Name}" : $"{fresh.Count} updates available",
                fresh.Count == 1 ? $"Version {fresh[0].AvailableVersion} is ready to install." : names,
                NotificationTarget.Updates,
                "Open updates");

        return (notification, cleaned);
    }

    private static string Key(InstalledApplication application) => application.Key.Value.ToUpperInvariant();

    private static string Names(IReadOnlyList<string> names) => names.Count switch
    {
        <= 3 => string.Join(", ", names),
        _ => $"{string.Join(", ", names.Take(3))} and {names.Count - 3} more",
    };
}
