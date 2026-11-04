using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Leftovers;

namespace SoftwareDock.Domain.Tests;

public class LeftoverCandidatesTests
{
    private static readonly KnownFolders Folders = new(
        @"C:\Program Files", @"C:\Program Files (x86)", @"C:\ProgramData", @"C:\Users\m\AppData\Roaming", @"C:\Users\m\AppData\Local");

    private static InstalledApplication App(string name, string? publisher = null, string? location = null) => new()
    {
        Key = ApplicationKey.Parse(name),
        Name = name,
        Publisher = publisher,
        InstallLocation = location,
        Origin = ApplicationOrigin.Registry,
    };

    [Fact]
    public void IncludesTheInstallFolder_AndExactNameFoldersInKnownLocations()
    {
        var candidates = LeftoverCandidates.For(App("Discord", "Discord Inc.", @"C:\Users\m\AppData\Local\Discord\"), Folders);

        Assert.Contains(@"C:\Users\m\AppData\Local\Discord", candidates);
        Assert.Contains(@"C:\Users\m\AppData\Roaming\Discord", candidates);
        Assert.Contains(@"C:\ProgramData\Discord", candidates);
        Assert.DoesNotContain(candidates, c => c.EndsWith(@"Discord\Discord", StringComparison.Ordinal));
    }

    [Fact]
    public void PublisherFolders_AreTried()
    {
        Assert.Contains(@"C:\Users\m\AppData\Local\Google\Chrome", LeftoverCandidates.For(App("Chrome", "Google LLC"), Folders));
    }

    [Fact]
    public void GenericInstallLocations_AreNeverCandidates()
    {
        var candidates = LeftoverCandidates.For(App("Tool", location: @"C:\Program Files\"), Folders);

        Assert.DoesNotContain(@"C:\Program Files", candidates);
    }

    [Fact]
    public void TooShortNames_YieldOnlyTheInstallFolder()
    {
        Assert.Empty(LeftoverCandidates.For(App("Go"), Folders));
    }

    [Theory]
    [InlineData("Notepad++ (64-bit x64)", "Notepad++")]
    [InlineData("7-Zip 24.08 (x64)", "7-Zip")]
    [InlineData("Python 3.12.7 (64-bit)", "Python")]
    [InlineData("OBS Studio", "OBS Studio")]
    public void CleanName(string display, string expected) => Assert.Equal(expected, LeftoverCandidates.CleanName(display));

    [Theory]
    [InlineData("Google LLC", "Google")]
    [InlineData("Discord Inc.", "Discord")]
    [InlineData("Spotify AB", "Spotify")]
    [InlineData("", null)]
    [InlineData("CN=Foo/Bar", null)]
    public void CleanPublisher(string publisher, string? expected) => Assert.Equal(expected, LeftoverCandidates.CleanPublisher(publisher));
}
