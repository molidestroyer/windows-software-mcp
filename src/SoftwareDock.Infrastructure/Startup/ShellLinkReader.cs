using System.Text;

namespace SoftwareDock.Infrastructure.Startup;

/// <summary>
/// Reads the target path out of a <c>.lnk</c> file (MS-SHLLINK) without COM: the Startup folder
/// is full of shortcuts, and the target is what ties one to an installed application.
/// </summary>
internal static class ShellLinkReader
{
    private const int HeaderSize = 0x4C;
    private const uint HasLinkTargetIdList = 0x1;
    private const uint HasLinkInfo = 0x2;
    private const uint VolumeIdAndLocalBasePath = 0x1;

    public static string? TryReadTarget(ReadOnlySpan<byte> link)
    {
        if (link.Length < HeaderSize || BitConverter.ToUInt32(link[..4]) != HeaderSize)
        {
            return null;
        }

        var flags = BitConverter.ToUInt32(link.Slice(0x14, 4));
        var offset = HeaderSize;
        if ((flags & HasLinkTargetIdList) != 0)
        {
            if (offset + 2 > link.Length)
            {
                return null;
            }

            offset += 2 + BitConverter.ToUInt16(link.Slice(offset, 2));
        }

        if ((flags & HasLinkInfo) == 0 || offset + 0x1C > link.Length)
        {
            return null;
        }

        var info = link[offset..];
        var infoSize = (int)BitConverter.ToUInt32(info[..4]);
        var headerSize = BitConverter.ToUInt32(info.Slice(4, 4));
        var infoFlags = BitConverter.ToUInt32(info.Slice(8, 4));
        if ((infoFlags & VolumeIdAndLocalBasePath) == 0 || infoSize > info.Length)
        {
            return null;
        }

        if (headerSize >= 0x24)
        {
            var unicodeOffset = (int)BitConverter.ToUInt32(info.Slice(0x1C, 4));
            if (unicodeOffset > 0 && unicodeOffset < infoSize)
            {
                return ReadUnicode(info[unicodeOffset..infoSize]);
            }
        }

        var ansiOffset = (int)BitConverter.ToUInt32(info.Slice(0x10, 4));
        return ansiOffset > 0 && ansiOffset < infoSize ? ReadAnsi(info[ansiOffset..infoSize]) : null;
    }

    private static string? ReadAnsi(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        var text = Encoding.Latin1.GetString(end < 0 ? bytes : bytes[..end]);
        return text.Length == 0 ? null : text;
    }

    private static string? ReadUnicode(ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i + 1 < bytes.Length; i += 2)
        {
            if (bytes[i] == 0 && bytes[i + 1] == 0)
            {
                var text = Encoding.Unicode.GetString(bytes[..i]);
                return text.Length == 0 ? null : text;
            }
        }

        return null;
    }
}
