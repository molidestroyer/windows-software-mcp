using SoftwareDock.Domain.Applications;

namespace SoftwareDock.Domain.Monitoring;

/// <summary>
/// Attributes running processes to installed applications by where their executable lives.
/// </summary>
/// <remarks>
/// Windows has no link between a process and the "Apps &amp; features" entry that installed it. The
/// install folder is the best evidence available: a process whose image is under
/// <c>C:\Program Files\Git\</c> belongs to Git. Rules:
/// <list type="bullet">
///   <item>Root = <c>InstallLocation</c>, else the folder of the <c>DisplayIcon</c> executable.</item>
///   <item>Roots that would swallow unrelated processes (a drive, <c>Program Files</c>,
///         <c>Windows</c>, <c>Common Files</c>, <c>AppData</c>…) are ignored.</item>
///   <item>The deepest root wins, so an app installed inside another's folder keeps its own processes.</item>
///   <item>CPU is the processor time each process gained since the previous sample, over wall-clock
///         time and all cores. A process seen for the first time contributes memory but no CPU yet.</item>
/// </list>
/// Paths are compared as Windows paths on every OS.
/// </remarks>
public static class ResourceAttribution
{
    private static readonly string[] TooGenericSuffixes =
    [
        @":\",
        @"\program files\",
        @"\program files (x86)\",
        @"\program files\common files\",
        @"\program files (x86)\common files\",
        @"\programdata\",
        @"\windows\",
        @"\windows\system32\",
        @"\windows\syswow64\",
        @"\appdata\local\",
        @"\appdata\roaming\",
        @"\appdata\local\programs\",
        @"\appdata\local\microsoft\windowsapps\",
    ];

    public static IReadOnlyDictionary<ApplicationKey, ResourceUsage> Attribute(
        IEnumerable<InstalledApplication> applications,
        IReadOnlyList<ProcessSample>? previous,
        IReadOnlyList<ProcessSample> current,
        TimeSpan elapsed,
        int processorCount)
    {
        ArgumentNullException.ThrowIfNull(applications);
        ArgumentNullException.ThrowIfNull(current);

        var roots = applications
            .Select(a => (a.Key, Root: RootOf(a)))
            .Where(r => r.Root is not null)
            .OrderByDescending(r => r.Root!.Length)
            .ToList();

        var before = previous?.ToDictionary(p => p.ProcessId) ?? [];
        var capacity = elapsed.TotalSeconds * Math.Max(1, processorCount);

        var totals = new Dictionary<ApplicationKey, (int Count, double Cpu, long Memory)>();
        foreach (var process in current)
        {
            var image = Normalize(process.ImagePath);
            if (image is null)
            {
                continue;
            }

            var owner = roots.FirstOrDefault(r => image.StartsWith(r.Root!, StringComparison.Ordinal));
            if (owner.Root is null)
            {
                continue;
            }

            var cpu = 0d;
            if (capacity > 0
                && before.TryGetValue(process.ProcessId, out var earlier)
                && string.Equals(Normalize(earlier.ImagePath), image, StringComparison.Ordinal))
            {
                cpu = Math.Max(0, (process.ProcessorTime - earlier.ProcessorTime).TotalSeconds) / capacity * 100;
            }

            totals.TryGetValue(owner.Key, out var sum);
            totals[owner.Key] = (sum.Count + 1, sum.Cpu + cpu, sum.Memory + process.WorkingSetBytes);
        }

        return totals.ToDictionary(t => t.Key, t => new ResourceUsage(t.Value.Count, Math.Min(100, t.Value.Cpu), t.Value.Memory));
    }

    /// <summary>The folder whose processes belong to <paramref name="application"/>, or <see langword="null"/>.</summary>
    internal static string? RootOf(InstalledApplication application)
    {
        var root = Normalize(application.InstallLocation, isDirectory: true)
            ?? Normalize(DirectoryOf(application.IconPath), isDirectory: true);
        return root is null || TooGenericSuffixes.Any(s => root.EndsWith(s, StringComparison.Ordinal)) || root.Length < 8
            ? null
            : root;
    }

    private static string? DirectoryOf(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            return null;
        }

        var path = iconPath.Trim();
        var comma = path.LastIndexOf(',');
        if (comma > 0 && int.TryParse(path[(comma + 1)..], out _))
        {
            path = path[..comma];
        }

        path = path.Trim().Trim('"');
        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var slash = path.LastIndexOfAny(['\\', '/']);
        return slash > 0 ? path[..slash] : null;
    }

    private static string? Normalize(string? path, bool isDirectory = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var text = path.Trim().Trim('"').Replace('/', '\\').ToLowerInvariant();
        if (text.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            text = text[4..];
        }

        if (text.Length < 3 || text[1] != ':')
        {
            return null;
        }

        return isDirectory && !text.EndsWith('\\') ? text + '\\' : text;
    }
}
