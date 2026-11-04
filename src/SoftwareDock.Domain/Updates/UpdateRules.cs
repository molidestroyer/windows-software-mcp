using SoftwareDock.Domain.Applications;

namespace SoftwareDock.Domain.Updates;

/// <summary>
/// The user's say over updates: apps pinned to their current version, and versions skipped.
/// A skipped version only hides <em>that</em> version — the next one shows up again.
/// </summary>
public sealed record UpdateRules(IReadOnlySet<string> Pinned, IReadOnlyDictionary<string, string> SkippedVersions)
{
    public static readonly UpdateRules None = new(new HashSet<string>(), new Dictionary<string, string>());

    public bool IsPinned(InstalledApplication application) =>
        Pinned.Contains(Normalize((application ?? throw new ArgumentNullException(nameof(application))).Key));

    public string? SkippedVersion(InstalledApplication application) =>
        SkippedVersions.GetValueOrDefault(Normalize((application ?? throw new ArgumentNullException(nameof(application))).Key));

    /// <summary>True when the app has an update the user still wants to hear about.</summary>
    public bool IsActionable(InstalledApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        return application.HasUpdate
            && !IsPinned(application)
            && !string.Equals(SkippedVersion(application), application.AvailableVersion, StringComparison.OrdinalIgnoreCase);
    }

    public UpdateRules Pin(ApplicationKey key) =>
        this with { Pinned = new HashSet<string>(Pinned, StringComparer.OrdinalIgnoreCase) { Normalize(key) } };

    public UpdateRules Unpin(ApplicationKey key)
    {
        var pinned = new HashSet<string>(Pinned, StringComparer.OrdinalIgnoreCase);
        pinned.Remove(Normalize(key));
        var skipped = new Dictionary<string, string>(SkippedVersions, StringComparer.OrdinalIgnoreCase);
        skipped.Remove(Normalize(key));
        return new UpdateRules(pinned, skipped);
    }

    public UpdateRules Skip(ApplicationKey key, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        return this with
        {
            SkippedVersions = new Dictionary<string, string>(SkippedVersions, StringComparer.OrdinalIgnoreCase) { [Normalize(key)] = version },
        };
    }

    private static string Normalize(ApplicationKey key) => key.Value.ToUpperInvariant();
}
