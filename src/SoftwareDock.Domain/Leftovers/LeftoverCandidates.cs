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
/// </remarks>
public static partial class LeftoverCandidates
{
    public static IReadOnlyList<string> For(InstalledApplication application, KnownFolders folders)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(folders);

        var candidates = new List<string>();
        if (ApplicationRoots.RootOf(application) is not null && application.InstallLocation is { } location)
        {
            candidates.Add(location.Trim().Trim('"').TrimEnd('\\'));
        }

        var name = CleanName(application.Name);
        var publisher = CleanPublisher(application.Publisher);
        if (name.Length < 3)
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
