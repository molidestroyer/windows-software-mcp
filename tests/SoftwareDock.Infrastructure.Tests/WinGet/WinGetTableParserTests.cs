using SoftwareDock.Infrastructure.WinGet;

namespace SoftwareDock.Infrastructure.Tests.WinGet;

public class WinGetTableParserTests
{
    private static readonly string[] ListHeader = ["Name", "Id", "Version", "Available", "Source"];

    [Fact]
    public void Parse_ListWithSpinnerAndEmptyCells_ReturnsEveryColumn()
    {
        var output = WinGetOutput.Table(
            ListHeader,
            ["7-Zip 24.08 (x64)", "7zip.7zip", "24.08", "", "winget"],
            ["Git", "Git.Git", "2.45.0", "2.47.1", "winget"],
            ["Contoso Legacy Tool", @"ARP\Machine\X86\{6F320B93-EE3C-4826-85E0-ADF79F8D4C61}", "3.1", "", ""]);

        var rows = WinGetTableParser.Parse(output);

        Assert.Equal(3, rows.Count);
        Assert.Equal(["Git", "Git.Git", "2.45.0", "2.47.1", "winget"], rows[1]);
        Assert.Equal(["7-Zip 24.08 (x64)", "7zip.7zip", "24.08", "", "winget"], rows[0]);
        Assert.Equal(@"ARP\Machine\X86\{6F320B93-EE3C-4826-85E0-ADF79F8D4C61}", rows[2][1]);
        Assert.Equal("", rows[2][4]);
    }

    [Fact]
    public void Parse_LocalizedHeader_IsReadByPositionNotByName()
    {
        var output = WinGetOutput.Table(["Nombre", "Id", "Versión", "Disponible", "Origen"], ["Git", "Git.Git", "2.45.0", "2.47.1", "winget"]);

        Assert.Equal(["Git", "Git.Git", "2.45.0", "2.47.1", "winget"], Assert.Single(WinGetTableParser.Parse(output)));
    }

    [Fact]
    public void Parse_WideCharacters_DoNotShiftLaterColumns()
    {
        var output = WinGetOutput.Table(
            ["Name", "Id", "Version", "Source"],
            ["微信", "Tencent.WeChat", "3.9.12", "winget"],
            ["카카오톡", "Kakao.KakaoTalk", "4.2.0", "winget"]);

        var rows = WinGetTableParser.Parse(output);

        Assert.Equal(["微信", "Tencent.WeChat", "3.9.12", "winget"], rows[0]);
        Assert.Equal(["카카오톡", "Kakao.KakaoTalk", "4.2.0", "winget"], rows[1]);
    }

    [Fact]
    public void Parse_UpgradeFooter_IsNotARow()
    {
        var output = WinGetOutput.Table(
            ListHeader,
            ["Git", "Git.Git", "2.45.0", "2.47.1", "winget"],
            ["Microsoft PowerToys (Preview) x64", "Microsoft.PowerToys", "0.99.0", "0.100.1", "winget"])
            + "2 upgrades available.\n";

        Assert.Equal(2, WinGetTableParser.Parse(output).Count);
    }

    [Fact]
    public void Parse_StopsAtTheBlankLineBeforeASecondSection()
    {
        var output = WinGetOutput.Table(ListHeader, ["Git", "Git.Git", "2.45.0", "2.47.1", "winget"])
            + "\nThe following packages have an upgrade available, but require explicit targeting for upgrade:\n"
            + WinGetOutput.Table(ListHeader, ["Pinned", "Vendor.Pinned", "1.0", "2.0", "winget"]);

        Assert.Equal("Git.Git", Assert.Single(WinGetTableParser.Parse(output))[1]);
    }

    [Fact]
    public void Parse_TruncatedCells_AreKeptVerbatimForTheDomainToReject()
    {
        var output = WinGetOutput.Table(["Name", "Id", "Version"], ["Microsoft Visual Studio Inst…", "Microsoft.VisualStudio.Ins…", "3.11"]);

        Assert.Equal("Microsoft.VisualStudio.Ins…", Assert.Single(WinGetTableParser.Parse(output))[1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("No installed package found matching input criteria.\n")]
    [InlineData("   - \r   \\ \rNo package found matching input criteria.\n")]
    public void Parse_NoTable_ReturnsNothing(string output)
    {
        Assert.Empty(WinGetTableParser.Parse(output));
    }

    [Fact]
    public void Parse_VtEscapeSequences_AreIgnored()
    {
        var output = "\u001b[0G\u001b[2K" + WinGetOutput.Table(["Name", "Id", "Version", "Source"], ["Git", "Git.Git", "2.45.0", "winget"]);

        Assert.Equal(["Git", "Git.Git", "2.45.0", "winget"], Assert.Single(WinGetTableParser.Parse(output)));
    }
}
