using System.Text.RegularExpressions;
using SoftwareDock.Domain.Applications;

namespace SoftwareDock.Domain.Leftovers;

/// <summary>The user's well-known folders, resolved by the caller (they differ per user and per OS).</summary>
public sealed record KnownFolders(string ProgramFiles, string ProgramFilesX86, string ProgramData, string RoamingAppData, string LocalAppData);

/// <summary>
/// Folders an uninstalled application may have left behind.
/// </summary>
/// <remarks>
/// Conservative on purpose — these may be deleted. A candidate is either the application's own
/// install folder (when it is specific, see <see cref="ApplicationRoots"/>), or a folder under a
/// well-known location whose name is <em>exactly</em> the application's or publisher's name, never
/// a prefix or a fuzzy match. Callers move them to the Recycle Bin, never delete outright.
/// <para>
/// Applications still installed veto candidates (found by QA in 0.3.0: removing one of two Pythons
/// offered <c>%LOCALAPPDATA%\Python</c>, which held the other one):
/// </para>
/// <list type="bullet">
///   <item>a candidate that is, contains or lies inside the folder of an installed application is dropped;</item>
///   <item>name-based candidates are dropped altogether when an installed application shares the
///         cleaned name (VC++ x86/x64, two Pythons, two versions of a tool) — the folder is theirs too.</item>
/// </list>
/// </remarks>
public static partial class LeftoverCandidates
{
    /// <param name="application">The application that was removed.</param>
    /// <param name="folders">Well-known folders of this user.</param>
    /// <param name="stillInstalled">Every application still installed; any of them can veto a candidate.</param>
    public static IReadOnlyList<string> For(InstalledApplication application, KnownFolders folders, IReadOnlyCollection<InstalledApplication> stillInstalled)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(stillInstalled);

        var others = stillInstalled.Where(a => a.Key != application.Key).ToList();
        var protectedFolders = others
            .SelectMany(a => new[] { ApplicationRoots.RootOf(a), ApplicationRoots.NormalizePath(a.InstallLocation, isDirectory: true) })
            .OfType<string>()
            .Where(f => !ApplicationRoots.IsTooGeneric(f))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return Raw(application, folders, sharesName: others.Any(o => string.Equals(CleanName(o.Name), CleanName(application.Name), StringComparison.OrdinalIgnoreCase)))
            .Where(c => !Overlaps(c, protectedFolders))
            .ToList();
    }

    private static bool Overlaps(string candidate, IReadOnlyList<string> protectedFolders)
    {
        var folder = ApplicationRoots.NormalizePath(candidate, isDirectory: true);
        return folder is null
            || protectedFolders.Any(p => p.StartsWith(folder, StringComparison.Ordinal) || folder.StartsWith(p, StringComparison.Ordinal));
    }

    private static List<string> Raw(InstalledApplication application, KnownFolders folders, bool sharesName)
    {
        var candidates = new List<string>();
        if (ApplicationRoots.RootOf(application) is not null && application.InstallLocation is { } location)
        {
            candidates.Add(location.Trim().Trim('"').TrimEnd('\\'));
        }

        var name = CleanName(application.Name);
        var publisher = CleanPublisher(application.Publisher);
        if (name.Length < 3 || sharesName)
        {
            return candidates;
        }

        foreach (var root in new[] { folders.ProgramFiles, folders.ProgramFilesX86, folders.ProgramData, folders.RoamingAppData, folders.LocalAppData })
        {
            candidates.Add($@"{root.TrimEnd('\\')}\{name}");
            if (publisher is not null && !string.Equals(publisher, name, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add($@"{root.TrimEnd('\\')}\{publisher}\{name}");
            }
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>"Notepad++ (64-bit x64)" → "Notepad++"; "7-Zip 24.08 (x64)" → "7-Zip"; "Python 3.12.7 (64-bit)" → "Python".</summary>
    internal static string CleanName(string displayName)
    {
        var text = Parenthesized().Replace(displayName, " ");
        text = TrailingVersion().Replace(text, string.Empty);
        return Spaces().Replace(text, " ").Trim().TrimEnd('-', '.', ' ');
    }

    /// <summary>"Google LLC" → "Google"; "Igor Pavlov" stays; empty or odd values → null.</summary>
    internal static string? CleanPublisher(string? publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher))
        {
            return null;
        }

        var text = CompanySuffix().Replace(publisher, string.Empty).Trim().TrimEnd(',', '.');
        return text.Length >= 3 && !text.Contains('\\', StringComparison.Ordinal) && !text.Contains('/', StringComparison.Ordinal) ? text : null;
    }

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Parenthesized();

    [GeneratedRegex(@"(\s+[vV]?\d+(\.\d+)*)+\s*$")]
    private static partial Regex TrailingVersion();

    [GeneratedRegex(@"[,\s]+(Inc|LLC|Ltd|GmbH|Corporation|Corp|Co|AB|SA|S\.A|AG|BV|B\.V)\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex CompanySuffix();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
