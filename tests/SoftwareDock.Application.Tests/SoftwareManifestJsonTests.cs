using SoftwareDock.Application.Backup;
using SoftwareDock.Domain.Backup;
using SoftwareDock.Domain.Packages;

namespace SoftwareDock.Application.Tests;

public class SoftwareManifestJsonTests
{
    private static readonly SoftwareManifest Manifest = new(
        new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero),
        "MIGUEL-PC",
        [
            new ManifestPackage(PackageId.Parse("Git.Git"), "Git", "winget", "2.47.1"),
            new ManifestPackage(PackageId.Parse("9NCBCSZSJRSB"), "Spotify", "msstore", "1.2"),
        ],
        [new ManifestApplication("Contoso Legacy Reporting", "Contoso Ltd.", "3.1.0")]);

    [Fact]
    public void SoftwareDockFormat_RoundTrips()
    {
        var read = SoftwareManifestJson.Read(SoftwareManifestJson.Write(Manifest, BackupFormat.SoftwareDock));

        Assert.Equal(Manifest.CreatedAt, read.CreatedAt);
        Assert.Equal("MIGUEL-PC", read.MachineName);
        Assert.Equal(Manifest.Packages, read.Packages);
        Assert.Equal(Manifest.Unmanaged, read.Unmanaged);
    }

    [Fact]
    public void WinGetFormat_GroupsPackagesBySourceAsWinGetExportDoes()
    {
        var json = SoftwareManifestJson.Write(Manifest, BackupFormat.WinGet);

        Assert.Contains("\"$schema\": \"https://aka.ms/winget-packages.schema.2.0.json\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Identifier\": \"Microsoft.Winget.Source_8wekyb3d8bbwe\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Identifier\": \"StoreEdgeFD\"", json, StringComparison.Ordinal);

        var read = SoftwareManifestJson.Read(json);
        Assert.Equal(["Git.Git", "9NCBCSZSJRSB"], read.Packages.Select(p => p.Id.Value));
        Assert.Empty(read.Unmanaged);
    }

    [Fact]
    public void Read_AcceptsARealWinGetExport()
    {
        const string export = """
            {
              "$schema" : "https://aka.ms/winget-packages.schema.2.0.json",
              "CreationDate" : "2026-10-01T09:30:00.000-00:00",
              "Sources" :
              [
                {
                  "Packages" :
                  [
                    { "PackageIdentifier" : "Microsoft.PowerToys" },
                    { "PackageIdentifier" : "VideoLAN.VLC", "Version" : "3.0.21" }
                  ],
                  "SourceDetails" :
                  {
                    "Argument" : "https://cdn.winget.microsoft.com/cache",
                    "Identifier" : "Microsoft.Winget.Source_8wekyb3d8bbwe",
                    "Name" : "winget",
                    "Type" : "Microsoft.PreIndexed.Package"
                  }
                }
              ],
              "WinGetVersion" : "1.12.350"
            }
            """;

        var read = SoftwareManifestJson.Read(export);

        Assert.Equal(["Microsoft.PowerToys", "VideoLAN.VLC"], read.Packages.Select(p => p.Id.Value));
        Assert.All(read.Packages, p => Assert.Equal("winget", p.Source));
    }

    [Fact]
    public void Read_SkipsIdsThatCouldInjectArguments()
    {
        const string hostile = """{ "format": "software-dock-backup", "packages": [ { "id": "--override" }, { "id": "Git.Git" } ] }""";

        Assert.Equal(["Git.Git"], SoftwareManifestJson.Read(hostile).Packages.Select(p => p.Id.Value));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "hello": "world" }""")]
    [InlineData("[]")]
    public void Read_SomethingElse_IsAFormatError(string text)
    {
        Assert.Throws<FormatException>(() => SoftwareManifestJson.Read(text));
    }
}
