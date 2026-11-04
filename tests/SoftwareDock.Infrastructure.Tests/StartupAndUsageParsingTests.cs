using System.Text;
using SoftwareDock.Infrastructure.Startup;
using SoftwareDock.Infrastructure.Usage;

namespace SoftwareDock.Infrastructure.Tests;

public class StartupAndUsageParsingTests
{
    /// <summary>A minimal MS-SHLLINK file: header, no ID list, a LinkInfo with a Unicode local base path.</summary>
    private static byte[] Shortcut(string target, bool withIdList = true)
    {
        var link = new List<byte>();
        var header = new byte[0x4C];
        BitConverter.GetBytes(0x4C).CopyTo(header, 0);
        BitConverter.GetBytes((uint)(0x2 | (withIdList ? 0x1 : 0))).CopyTo(header, 0x14);
        link.AddRange(header);
        if (withIdList)
        {
            link.AddRange(BitConverter.GetBytes((ushort)4));
            link.AddRange(new byte[4]);
        }

        var ansi = Encoding.Latin1.GetBytes(target + "\0");
        var unicode = Encoding.Unicode.GetBytes(target + "\0");
        const int infoHeader = 0x24;
        var size = infoHeader + ansi.Length + unicode.Length;
        var info = new byte[size];
        BitConverter.GetBytes(size).CopyTo(info, 0);
        BitConverter.GetBytes(infoHeader).CopyTo(info, 4);
        BitConverter.GetBytes(1).CopyTo(info, 8);
        BitConverter.GetBytes(infoHeader).CopyTo(info, 0x10);
        BitConverter.GetBytes(infoHeader + ansi.Length).CopyTo(info, 0x1C);
        ansi.CopyTo(info, infoHeader);
        unicode.CopyTo(info, infoHeader + ansi.Length);
        link.AddRange(info);
        return [.. link];
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShellLink_TargetIsRead(bool withIdList)
    {
        Assert.Equal(@"C:\Program Files\Ñandú\app.exe", ShellLinkReader.TryReadTarget(Shortcut(@"C:\Program Files\Ñandú\app.exe", withIdList)));
    }

    [Fact]
    public void ShellLink_GarbageIsNotALink()
    {
        Assert.Null(ShellLinkReader.TryReadTarget("not a shortcut at all, really"u8));
        Assert.Null(ShellLinkReader.TryReadTarget([]));
    }

    [Fact]
    public void Rot13_IsItsOwnInverse()
    {
        Assert.Equal(@"P:\Cebtenz Svyrf\Tvg\tvg-onfu.rkr", UserAssistDecoder.Rot13(@"C:\Program Files\Git\git-bash.exe"));
        Assert.Equal("{6D809377-6AF0-444B-8957-A3773F02200E}", UserAssistDecoder.Rot13("{6Q809377-6NS0-444O-8957-N3773S02200R}"));
    }

    [Fact]
    public void UserAssist_DecodesCountLastRunAndKnownFolders()
    {
        var lastRun = new DateTimeOffset(2026, 10, 30, 18, 0, 0, TimeSpan.Zero);
        var value = new byte[72];
        BitConverter.GetBytes(42).CopyTo(value, 4);
        BitConverter.GetBytes(lastRun.ToFileTime()).CopyTo(value, 60);
        var programFiles = new Guid("6D809377-6AF0-444B-8957-A3773F02200E");

        var record = UserAssistDecoder.Decode(
            UserAssistDecoder.Rot13(@"{6D809377-6AF0-444B-8957-A3773F02200E}\Git\git-bash.exe"),
            value,
            id => id == programFiles ? @"C:\Program Files" : null);

        Assert.Equal(@"C:\Program Files\Git\git-bash.exe", record!.ExecutablePath);
        Assert.Equal(42, record.RunCount);
        Assert.Equal(lastRun, record.LastUsed);
    }

    [Theory]
    [InlineData(@"UEME_CTLSESSION", 72)]
    [InlineData(@"C:\Tools\tool.exe", 16)]
    [InlineData(@"{00000000-0000-0000-0000-000000000000}\x.exe", 72)]
    [InlineData(@"Microsoft.Windows.Explorer", 72)]
    public void UserAssist_SkipsNonExecutablesUnknownFoldersAndOldFormats(string name, int length)
    {
        var value = new byte[length];
        if (length >= 72)
        {
            BitConverter.GetBytes(DateTimeOffset.UtcNow.ToFileTime()).CopyTo(value, 60);
        }

        Assert.Null(UserAssistDecoder.Decode(UserAssistDecoder.Rot13(name), value, _ => null));
    }
}
