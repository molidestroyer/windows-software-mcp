using SoftwareDock.Infrastructure.Processes;

namespace SoftwareDock.Infrastructure.WinGet;

/// <summary>
/// Reads the fixed-width tables that <c>winget list</c>, <c>search</c> and <c>upgrade</c> print.
/// </summary>
/// <remarks>
/// WinGet has no machine-readable output for these commands, so this is the contract:
/// <list type="bullet">
///   <item>The table is the header line directly above the first line made only of dashes.</item>
///   <item>Column starts are taken from where each header word begins. Header words are
///         localized, so columns are identified by <em>position and count</em>, never by name.</item>
///   <item>Rows end at the first blank line, or at the first line whose words straddle a
///         column boundary (the "N upgrades available." footer).</item>
///   <item>Widths are measured in terminal cells (<see cref="TerminalCells"/>).</item>
/// </list>
/// </remarks>
internal static class WinGetTableParser
{
    public static IReadOnlyList<string[]> Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var lines = output.Split('\n').Select(ConsoleText.CleanLine).ToList();
        var separator = lines.FindIndex(IsSeparator);
        if (separator <= 0)
        {
            return [];
        }

        var header = lines[separator - 1];
        var columnStarts = ColumnStarts(header);
        if (columnStarts.Count < 2)
        {
            return [];
        }

        var rows = new List<string[]>();
        foreach (var line in lines.Skip(separator + 1))
        {
            if (string.IsNullOrWhiteSpace(line) || IsSeparator(line))
            {
                break;
            }

            var cells = TerminalCells.Slice(line, columnStarts, out var aligned);
            if (!aligned || cells[0].Length == 0 || cells[1].Length == 0)
            {
                break;
            }

            rows.Add(cells);
        }

        return rows;
    }

    private static bool IsSeparator(string line)
    {
        var text = line.Trim();
        return text.Length >= 3 && text.All(c => c == '-');
    }

    private static List<int> ColumnStarts(string header)
    {
        var starts = new List<int>();
        var position = 0;
        var previousWasSpace = true;
        foreach (var rune in header.EnumerateRunes())
        {
            var isSpace = System.Text.Rune.IsWhiteSpace(rune);
            if (!isSpace && previousWasSpace)
            {
                starts.Add(position);
            }

            previousWasSpace = isSpace;
            position += TerminalCells.Width(rune);
        }

        // Rows are laid out from column zero even if the header drifted right.
        if (starts.Count > 0)
        {
            starts[0] = 0;
        }

        return starts;
    }
}
