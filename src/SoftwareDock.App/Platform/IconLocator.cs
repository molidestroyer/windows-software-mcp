namespace SoftwareDock.App.Platform;

/// <summary>
/// Picks the executable whose icon best represents an application when its registry entry has no
/// usable <c>DisplayIcon</c> — about a third of entries on a typical PC.
/// </summary>
public static class IconLocator
{
    private static readonly string[] NotTheApp =
    [
        "unins", "uninstall", "update", "updater", "setup", "install", "crash", "helper", "elevat",
        "report", "service", "launcher-stub", "vc_redist", "dotnet", "createdump",
    ];

    /// <param name="applicationName">Display name, e.g. "Microsoft Visual Studio Code".</param>
    /// <param name="executables">Executables in the install folder: file name and size.</param>
    /// <returns>The chosen file name, or <see langword="null"/> when nothing plausible exists.</returns>
    public static string? Choose(string applicationName, IEnumerable<(string FileName, long Size)> executables)
    {
        ArgumentNullException.ThrowIfNull(executables);

        var candidates = executables
            .Where(e => e.FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .Where(e => !NotTheApp.Any(n => e.FileName.Contains(n, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var tokens = (applicationName ?? string.Empty)
            .Split([' ', '-', '_', '.', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 3 && !t.All(char.IsDigit))
            .Select(t => t.ToUpperInvariant())
            .ToList();

        return candidates
            .OrderByDescending(e => tokens.Count(t => Path.GetFileNameWithoutExtension(e.FileName).ToUpperInvariant().Contains(t, StringComparison.Ordinal)))
            .ThenByDescending(e => e.Size)
            .Select(e => e.FileName)
            .First();
    }
}
