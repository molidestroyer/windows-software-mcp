using System.Text.RegularExpressions;

namespace SoftwareDock.Infrastructure.Processes;

/// <summary>Cleans console output written for a terminal, not for a pipe.</summary>
internal static partial class ConsoleText
{
    /// <summary>
    /// Removes VT escape sequences and keeps only what a terminal would finally show for a line
    /// that was rewritten in place with <c>\r</c> (spinners, progress bars).
    /// </summary>
    public static string CleanLine(string line)
    {
        var text = AnsiEscape().Replace(line, string.Empty).TrimEnd('\r');
        var lastReturn = text.LastIndexOf('\r');
        return lastReturn >= 0 ? text[(lastReturn + 1)..] : text;
    }

    /// <summary>A spinner frame or progress bar: nothing a person needs to read.</summary>
    public static bool IsNoise(string line)
    {
        var text = line.Trim();
        return text.Length == 0
            || text is "-" or "\\" or "|" or "/"
            || text.All(c => c is '█' or '▒' or '░' or ' ');
    }

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex AnsiEscape();
}
