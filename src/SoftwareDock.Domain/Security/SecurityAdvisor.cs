using System.Text.RegularExpressions;
using SoftwareDock.Domain.Applications;

namespace SoftwareDock.Domain.Security;

/// <summary>
/// Flags installed applications that have an update pending <em>and</em> appear in the Known
/// Exploited Vulnerabilities catalog with recent entries.
/// </summary>
/// <remarks>
/// The catalog names products, not versions, so it cannot say whether the installed build is
/// affected. The combination it can vouch for: "attackers are exploiting this product right now"
/// and "you are not on the latest version". That is the signal worth interrupting someone for.
/// Matching is deliberately strict — exact product names after normalization, plus a short alias
/// list — because a false "your PC is vulnerable" erodes trust faster than a miss.
/// </remarks>
public static partial class SecurityAdvisor
{
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(120);

    // Catalog product → installed application names it ships in.
    private static readonly (string Vendor, string ProductPrefix, string[] Applications)[] Aliases =
    [
        ("google", "chromium", ["google chrome", "chrome"]),
        ("google", "chrome", ["google chrome", "chrome"]),
        ("microsoft", "edge", ["microsoft edge"]),
        ("microsoft", "chromium", ["microsoft edge"]),
        ("mozilla", "firefox", ["mozilla firefox", "firefox"]),
        ("mozilla", "thunderbird", ["mozilla thunderbird", "thunderbird"]),
        ("adobe", "acrobat", ["adobe acrobat", "adobe acrobat reader", "adobe acrobat reader dc", "adobe acrobat dc"]),
        ("adobe", "reader", ["adobe acrobat reader", "adobe acrobat reader dc", "adobe reader"]),
        ("rarlab", "winrar", ["winrar"]),
        ("7-zip", "7-zip", ["7-zip"]),
        ("oracle", "java", ["java", "java 8", "oracle java"]),
    ];

    public static IReadOnlyDictionary<ApplicationKey, SecurityAdvisory> Assess(
        IEnumerable<InstalledApplication> applications,
        IReadOnlyList<KnownExploitedVulnerability> catalog,
        DateOnly today,
        TimeSpan? window = null)
    {
        ArgumentNullException.ThrowIfNull(applications);
        ArgumentNullException.ThrowIfNull(catalog);

        var since = today.AddDays(-(int)(window ?? DefaultWindow).TotalDays);
        var recent = catalog.Where(v => v.DateAdded >= since).ToList();
        var result = new Dictionary<ApplicationKey, SecurityAdvisory>();

        foreach (var application in applications.Where(a => a.HasUpdate))
        {
            var name = CoreName(application.Name);
            var hits = recent.Where(v => Matches(v, name, application.Publisher)).OrderByDescending(v => v.DateAdded).ToList();
            if (hits.Count > 0)
            {
                result[application.Key] = new SecurityAdvisory(hits);
            }
        }

        return result;
    }

    internal static bool Matches(KnownExploitedVulnerability vulnerability, string coreName, string? publisher)
    {
        var vendor = Normalize(vulnerability.Vendor);
        var product = Normalize(vulnerability.Product);

        foreach (var (aliasVendor, prefix, names) in Aliases)
        {
            if (vendor == aliasVendor && product.StartsWith(prefix, StringComparison.Ordinal))
            {
                return names.Contains(coreName, StringComparer.Ordinal);
            }
        }

        // "Notepad++" from vendor "Notepad++" ships as "Notepad++"; "VMware Workstation" as "VMware Workstation".
        var vendorMatches = (publisher is not null && Normalize(publisher).StartsWith(vendor, StringComparison.Ordinal)) || coreName.StartsWith(vendor, StringComparison.Ordinal);
        return vendorMatches && (coreName == product || coreName == $"{vendor} {product}");
    }

    /// <summary>"Mozilla Firefox (x64 en-US)" → "mozilla firefox"; "7-Zip 24.08 (x64)" → "7-zip".</summary>
    internal static string CoreName(string displayName)
    {
        var text = Parenthesized().Replace(displayName, " ");
        text = VersionLike().Replace(text, " ");
        text = Architecture().Replace(text, " ");
        return Normalize(text);
    }

    private static string Normalize(string text) =>
        Spaces().Replace(text.ToLowerInvariant().Replace('®', ' ').Replace('™', ' '), " ").Trim();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Parenthesized();

    [GeneratedRegex(@"\b[vV]?\d+(\.\d+)+\b|\b\d{4}\b")]
    private static partial Regex VersionLike();

    [GeneratedRegex(@"\b(x64|x86|64-bit|32-bit|arm64)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Architecture();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
