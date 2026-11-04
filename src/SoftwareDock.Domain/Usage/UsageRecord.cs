using SoftwareDock.Domain.Applications;

namespace SoftwareDock.Domain.Usage;

/// <summary>When a program was last started by the user, as Windows recorded it.</summary>
public sealed record UsageRecord(string ExecutablePath, DateTimeOffset LastUsed, int RunCount);

/// <summary>Last use of an installed application: the latest of all its executables.</summary>
public sealed record ApplicationUsage(DateTimeOffset LastUsed, int RunCount);

public static class UsageAttribution
{
    public static IReadOnlyDictionary<ApplicationKey, ApplicationUsage> Attribute(
        IEnumerable<InstalledApplication> applications,
        IEnumerable<UsageRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var ownerOf = ApplicationRoots.Lookup(applications);
        var result = new Dictionary<ApplicationKey, ApplicationUsage>();
        foreach (var record in records)
        {
            if (ownerOf(record.ExecutablePath) is not { } owner)
            {
                continue;
            }

            result[owner] = result.TryGetValue(owner, out var seen)
                ? new ApplicationUsage(record.LastUsed > seen.LastUsed ? record.LastUsed : seen.LastUsed, seen.RunCount + record.RunCount)
                : new ApplicationUsage(record.LastUsed, record.RunCount);
        }

        return result;
    }
}
