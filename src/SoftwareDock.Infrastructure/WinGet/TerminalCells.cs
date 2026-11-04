using System.Text;

namespace SoftwareDock.Infrastructure.WinGet;

/// <summary>
/// Measures text the way a terminal lays it out: East Asian wide characters and emoji take two
/// cells. WinGet pads its columns by cells, not by UTF-16 units, so slicing a row by
/// <c>string</c> index misaligns every column after a CJK name.
/// </summary>
internal static class TerminalCells
{
    public static int Width(Rune rune)
    {
        var v = rune.Value;
        if (Rune.GetUnicodeCategory(rune) is System.Globalization.UnicodeCategory.NonSpacingMark
            or System.Globalization.UnicodeCategory.EnclosingMark
            or System.Globalization.UnicodeCategory.Format)
        {
            return 0;
        }

        var wide = v is >= 0x1100 and <= 0x115F
            or >= 0x2E80 and <= 0x303E
            or >= 0x3041 and <= 0x33FF
            or >= 0x3400 and <= 0x4DBF
            or >= 0x4E00 and <= 0x9FFF
            or >= 0xA000 and <= 0xA4CF
            or >= 0xAC00 and <= 0xD7A3
            or >= 0xF900 and <= 0xFAFF
            or >= 0xFE30 and <= 0xFE4F
            or >= 0xFF00 and <= 0xFF60
            or >= 0xFFE0 and <= 0xFFE6
            or >= 0x1F300 and <= 0x1F64F
            or >= 0x1F900 and <= 0x1F9FF
            or >= 0x20000 and <= 0x3FFFD;
        return wide ? 2 : 1;
    }

    /// <summary>Splits <paramref name="line"/> into one string per cell range starting at <paramref name="columnStarts"/>.</summary>
    /// <param name="line">A table row.</param>
    /// <param name="columnStarts">Cell offset where each column begins, ascending.</param>
    /// <param name="aligned">
    /// <see langword="false"/> when a column boundary falls inside a word — the line is not a row
    /// of this table (a footer such as "3 upgrades available.").
    /// </param>
    public static string[] Slice(string line, IReadOnlyList<int> columnStarts, out bool aligned)
    {
        var cells = new StringBuilder[columnStarts.Count];
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i] = new StringBuilder();
        }

        aligned = true;
        var position = 0;
        var column = 0;
        var previousWasSpace = true;
        foreach (var rune in line.EnumerateRunes())
        {
            while (column + 1 < columnStarts.Count && position >= columnStarts[column + 1])
            {
                column++;
                if (position == columnStarts[column] && !previousWasSpace)
                {
                    aligned = false;
                }
            }

            cells[column].Append(rune.ToString());
            previousWasSpace = Rune.IsWhiteSpace(rune);
            position += Width(rune);
        }

        return cells.Select(c => c.ToString().Trim()).ToArray();
    }
}
