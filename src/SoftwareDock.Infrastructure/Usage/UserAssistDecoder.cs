using SoftwareDock.Domain.Usage;

namespace SoftwareDock.Infrastructure.Usage;

/// <summary>
/// Decodes Explorer's UserAssist entries: the per-user record of programs started from the
/// Start menu, taskbar or Explorer, with a run count and the last time.
/// </summary>
/// <remarks>
/// Value names are ROT13-encoded paths, optionally starting with a known-folder GUID
/// (<c>{6D809377-…}\Git\git-bash.exe</c>). Values are 72 bytes on Windows 7 and later: the run
/// count is at offset 4 and the last-run FILETIME at offset 60. Programs started some other way
/// (a script, a scheduled task) are not recorded, so "never used" really means "never opened".
/// </remarks>
internal static class UserAssistDecoder
{
    private const int EntryLength = 72;

    public static UsageRecord? Decode(string encodedName, byte[]? value, Func<Guid, string?> resolveKnownFolder)
    {
        if (value is not { Length: >= EntryLength })
        {
            return null;
        }

        var path = ExpandKnownFolder(Rot13(encodedName), resolveKnownFolder);
        if (path is null || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var fileTime = BitConverter.ToInt64(value, 60);
        if (fileTime <= 0)
        {
            return null;
        }

        return new UsageRecord(path, DateTimeOffset.FromFileTime(fileTime), BitConverter.ToInt32(value, 4));
    }

    internal static string Rot13(string text) => string.Create(text.Length, text, static (span, source) =>
    {
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            span[i] = c switch
            {
                >= 'a' and <= 'z' => (char)('a' + ((c - 'a' + 13) % 26)),
                >= 'A' and <= 'Z' => (char)('A' + ((c - 'A' + 13) % 26)),
                _ => c,
            };
        }
    });

    private static string? ExpandKnownFolder(string path, Func<Guid, string?> resolve)
    {
        if (!path.StartsWith('{'))
        {
            return path.Length > 2 && path[1] == ':' ? path : null;
        }

        var close = path.IndexOf('}', StringComparison.Ordinal);
        if (close < 0 || !Guid.TryParse(path[..(close + 1)], out var folderId) || resolve(folderId) is not { } folder)
        {
            return null;
        }

        return folder.TrimEnd('\\') + path[(close + 1)..];
    }
}
