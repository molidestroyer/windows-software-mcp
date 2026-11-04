using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Packages;

namespace SoftwareDock.Domain.Inventory;

/// <summary>
/// Merges the registry's view of installed software with the package manager's into one list.
/// </summary>
/// <remarks>
/// Neither source is enough on its own. The registry has publisher, size, icon and uninstaller,
/// but no idea whether an update exists. WinGet knows ids and updates, but truncates names and
/// hides everything else. The rules, in order:
/// <list type="number">
///   <item>A local package whose id is an ARP key is that registry entry — exact match.</item>
///   <item>A source package (winget/msstore) is matched to an unclaimed registry entry by name,
///         preferring the one whose version also matches.</item>
///   <item>Anything the package manager reports that is still unmatched (MSIX apps, mostly)
///         is added on its own.</item>
/// </list>
/// Pure and deterministic: same inputs, same output, sorted by name.
/// </remarks>
public static class InventoryReconciler
{
    public static IReadOnlyList<InstalledApplication> Reconcile(
        IEnumerable<InstalledApplication> registryEntries,
        IEnumerable<InstalledPackage> packages)
    {
        ArgumentNullException.ThrowIfNull(registryEntries);
        ArgumentNullException.ThrowIfNull(packages);

        var byKey = new Dictionary<ApplicationKey, InstalledApplication>();
        foreach (var entry in registryEntries)
        {
            // Two views can expose the same key; the first one wins, as it does in Settings.
            byKey.TryAdd(entry.Key, entry);
        }

        var claimed = new HashSet<ApplicationKey>();
        var extras = new List<InstalledApplication>();

        foreach (var package in packages)
        {
            if (TryClaimByKey(package, byKey, claimed, out var exact))
            {
                byKey[exact.Key] = Enrich(exact, package);
                continue;
            }

            var byName = FindByName(package, byKey.Values, claimed);
            if (byName is not null)
            {
                claimed.Add(byName.Key);
                byKey[byName.Key] = Enrich(byName, package);
                continue;
            }

            extras.Add(FromPackage(package));
        }

        return byKey.Values
            .Concat(extras)
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(a => a.Key.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryClaimByKey(
        InstalledPackage package,
        Dictionary<ApplicationKey, InstalledApplication> byKey,
        HashSet<ApplicationKey> claimed,
        out InstalledApplication match)
    {
        match = null!;
        var key = ApplicationKey.Parse(package.Id.Value);
        if (!key.IsRegistryEntry || !byKey.TryGetValue(key, out var found) || !claimed.Add(key))
        {
            return false;
        }

        match = found;
        return true;
    }

    private static InstalledApplication? FindByName(
        InstalledPackage package,
        IEnumerable<InstalledApplication> candidates,
        HashSet<ApplicationKey> claimed)
    {
        var sameName = candidates
            .Where(c => !claimed.Contains(c.Key) && NamesMatch(package.Name, c.Name))
            .ToList();

        return sameName.FirstOrDefault(c => VersionsMatch(package.Version, c.Version))
            ?? (sameName.Count == 1 ? sameName[0] : null);
    }

    /// <summary>
    /// Exact, case-insensitive name match — or a prefix match when WinGet truncated its copy.
    /// </summary>
    internal static bool NamesMatch(string packageName, string registryName)
    {
        var name = packageName.Trim();
        if (name.EndsWith(PackageId.TruncationMarker))
        {
            var prefix = name.TrimEnd(PackageId.TruncationMarker).TrimEnd();
            return prefix.Length > 0 && registryName.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase);
        }

        return string.Equals(name, registryName.Trim(), StringComparison.CurrentCultureIgnoreCase);
    }

    private static bool VersionsMatch(string? left, string? right) =>
        !string.IsNullOrEmpty(left) && string.Equals(left.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static InstalledApplication Enrich(InstalledApplication entry, InstalledPackage package) => entry with
    {
        PackageId = package.Id,
        PackageSource = package.Source,
        AvailableVersion = package.AvailableVersion,
        Version = entry.Version ?? package.Version,
    };

    private static InstalledApplication FromPackage(InstalledPackage package) => new()
    {
        Key = ApplicationKey.Parse(package.Id.Value),
        Name = package.Name,
        Origin = package.Id.Value.StartsWith(@"MSIX\", StringComparison.OrdinalIgnoreCase)
            ? ApplicationOrigin.Packaged
            : ApplicationOrigin.PackageManager,
        Version = package.Version,
        PackageId = package.Id,
        PackageSource = package.Source,
        AvailableVersion = package.AvailableVersion,
    };
}
