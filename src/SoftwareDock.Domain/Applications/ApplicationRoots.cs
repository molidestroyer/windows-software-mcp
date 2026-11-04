namespace SoftwareDock.Domain.Applications;

/// <summary>
/// The folder an application lives in, and which application a file belongs to. Shared by
/// everything that links a path back to an installed application: running processes, startup
/// entries, usage history, leftovers.
/// </summary>
/// <remarks>
/// Root = <c>InstallLocation</c>, else the folder of the <c>DisplayIcon</c> executable. Folders
/// that would swallow unrelated files (a drive, <c>Program Files</c>, <c>Windows</c>,
/// <c>AppData</c>…) are never roots. Paths are compared as lower-case Windows paths on every OS.
/// </remarks>
public static class ApplicationRoots
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

    /// <summary>The application's root folder, normalized with a trailing backslash, or <see langword="null"/>.</summary>
    public static string? RootOf(InstalledApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        var root = NormalizePath(application.InstallLocation, isDirectory: true)
            ?? NormalizePath(ExecutableDirectory(application.IconPath), isDirectory: true);
        return root is null || IsTooGeneric(root) || root.Length < 8 ? null : root;
    }

    public static bool IsTooGeneric(string normalizedDirectory) =>
        TooGenericSuffixes.Any(s => normalizedDirectory.EndsWith(s, StringComparison.Ordinal));

    /// <summary>
    /// Builds a lookup from file paths to the application whose root contains them; the deepest
    /// root wins, so an app installed inside another's folder keeps its own files.
    /// </summary>
    public static Func<string?, ApplicationKey?> Lookup(IEnumerable<InstalledApplication> applications)
    {
        ArgumentNullException.ThrowIfNull(applications);
        var roots = applications
            .Select(a => (a.Key, Root: RootOf(a)))
            .Where(r => r.Root is not null)
            .OrderByDescending(r => r.Root!.Length)
            .ToList();

        return path =>
        {
            var normalized = NormalizePath(path);
            return normalized is null
                ? null
                : roots.FirstOrDefault(r => normalized.StartsWith(r.Root!, StringComparison.Ordinal)).Key;
        };
    }

    /// <summary>Lower-case, backslashes, no <c>\\?\</c> prefix; <see langword="null"/> unless it is an absolute drive path.</summary>
    public static string? NormalizePath(string? path, bool isDirectory = false)
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

    /// <summary>Folder of the executable in a <c>DisplayIcon</c> value (<c>"C:\App\app.exe",0</c>).</summary>
    public static string? ExecutableDirectory(string? iconPath)
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
}
