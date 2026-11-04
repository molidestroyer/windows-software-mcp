using System.Text;
using SoftwareDock.Infrastructure.WinGet;

namespace SoftwareDock.Infrastructure.Tests.WinGet;

/// <summary>
/// Builds console output with WinGet's layout: columns padded in terminal cells, one space
/// between them, a dash rule under the header, and the spinner frames it writes before the table.
/// </summary>
internal static class WinGetOutput
{
    public const string Spinner = "   - \r   \\ \r   | \r   / \r";

    public static string Table(string[] header, params string[][] rows)
    {
        var widths = header.Select((h, i) => Math.Max(Cells(h), rows.Select(r => Cells(r[i])).DefaultIfEmpty(0).Max())).ToArray();
        var output = new StringBuilder(Spinner);
        output.Append(Line(header, widths)).Append('\n');
        output.Append(new string('-', widths.Sum() + widths.Length)).Append('\n');
        foreach (var row in rows)
        {
            output.Append(Line(row, widths)).Append('\n');
        }

        return output.ToString();
    }

    private static string Line(string[] cells, int[] widths)
    {
        var line = new StringBuilder();
        for (var i = 0; i < cells.Length; i++)
        {
            line.Append(cells[i]);
            if (i < cells.Length - 1)
            {
                line.Append(' ', widths[i] - Cells(cells[i]) + 1);
            }
        }

        return line.ToString().TrimEnd();
    }

    private static int Cells(string text) => text.EnumerateRunes().Sum(TerminalCells.Width);
}
