using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace SoftwareDock.App.ViewModels;

/// <summary>Initials and a stable colour for apps without an icon — the same app always gets the same tile.</summary>
internal static class Avatar
{
    private static readonly IBrush[] Palette =
    [
        Brush("#6366F1"), Brush("#8B5CF6"), Brush("#EC4899"), Brush("#F43F5E"),
        Brush("#F59E0B"), Brush("#10B981"), Brush("#06B6D4"), Brush("#3B82F6"),
    ];

    public static string Initials(string name)
    {
        var words = name
            .Split([' ', '-', '_', '.', '('], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => char.IsLetterOrDigit(w[0]))
            .ToList();
        return words.Count switch
        {
            0 => "?",
            1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
            _ => string.Concat(char.ToUpperInvariant(words[0][0]), char.ToUpperInvariant(words[1][0])),
        };
    }

    public static IBrush BrushFor(string name)
    {
        // FNV-1a: string.GetHashCode is randomized per process, and the colour must not change between runs.
        var hash = 2166136261u;
        foreach (var c in name)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return Palette[hash % (uint)Palette.Length];
    }

    private static ImmutableSolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
