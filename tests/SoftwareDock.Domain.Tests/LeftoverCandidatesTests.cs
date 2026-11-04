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
        var candidates = LeftoverCandidates.For(App("Discord", "Discord Inc.", @"C:\Users\m\AppData\Local\Discord\"), Folders, []);

        Assert.Contains(@"C:\Users\m\AppData\Local\Discord", candidates);
        Assert.Contains(@"C:\Users\m\AppData\Roaming\Discord", candidates);
        Assert.Contains(@"C:\ProgramData\Discord", candidates);
        Assert.DoesNotContain(candidates, c => c.EndsWith(@"Discord\Discord", StringComparison.Ordinal));
    }

    [Fact]
    public void PublisherFolders_AreTried()
    {
        Assert.Contains(@"C:\Users\m\AppData\Local\Google\Chrome", LeftoverCandidates.For(App("Chrome", "Google LLC"), Folders, []));
    }

    [Fact]
    public void GenericInstallLocations_AreNeverCandidates()
    {
        var candidates = LeftoverCandidates.For(App("Tool", location: @"C:\Program Files\"), Folders, []);

        Assert.DoesNotContain(@"C:\Program Files", candidates);
    }

    [Fact]
    public void TooShortNames_YieldOnlyTheInstallFolder()
    {
        Assert.Empty(LeftoverCandidates.For(App("Go"), Folders, []));
    }

    // QA, 0.3.0 on a real PC: Python 3.14 lives in %LOCALAPPDATA%\Python\pythoncore-3.14-64 and
    // the shared pip user site in %APPDATA%\Python. Removing Python 3.12 must offer neither.
    [Fact]
    public void AnotherInstallInsideACandidate_VetoesIt()
    {
        var removed = App("Python 3.12.7 (64-bit)", "Python Software Foundation");
        var stillInstalled = App("Python 3.14.3", "Python Software Foundation", @"C:\Users\m\AppData\Local\Python\pythoncore-3.14-64");

        var candidates = LeftoverCandidates.For(removed, Folders, [removed, stillInstalled]);

        Assert.DoesNotContain(candidates, c => @"C:\Users\m\AppData\Local\Python\pythoncore-3.14-64".StartsWith(c, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(@"C:\Users\m\AppData\Roaming\Python", candidates);
    }

    [Fact]
    public void SameNameStillInstalled_DropsEveryNameBasedCandidate()
    {
        var x86 = App("Microsoft Visual C++ 2015-2022 Redistributable (x86) - 14.44.35211", "Microsoft Corporation");
        var x64 = App("Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.44.35211", "Microsoft Corporation");

        Assert.Empty(LeftoverCandidates.For(x86, Folders, [x64]));
    }

    [Fact]
    public void ACandidateInsideAnInstalledAppsFolder_IsVetoed()
    {
        var plugin = App("Thing", location: @"C:\Program Files\Suite\Thing");
        var suite = App("Suite", location: @"C:\Program Files\Suite");

        Assert.DoesNotContain(@"C:\Program Files\Suite\Thing", LeftoverCandidates.For(plugin, Folders, [suite]));
    }

    // QA, 0.3.0 re-test: a single Python lives in %LOCALAPPDATA%\Python\pythoncore-3.14-64; the parent
    // also holds the Python Install Manager's bin (python.exe on PATH) and _cache. Only the install folder may go.
    [Fact]
    public void AContainerOfTheAppsOwnInstallFolder_IsNeverOffered()
    {
        var python = App("Python 3.14.3", "Python Software Foundation", @"C:\Users\m\AppData\Local\Python\pythoncore-3.14-64");

        var candidates = LeftoverCandidates.For(python, Folders, [python]);

        Assert.Contains(@"C:\Users\m\AppData\Local\Python\pythoncore-3.14-64", candidates);
        Assert.DoesNotContain(@"C:\Users\m\AppData\Local\Python", candidates);
    }

    [Fact]
    public void FoldersOnPathOrWithRunningProcesses_VetoCandidates()
    {
        var python = App("Python 3.12.7 (64-bit)", "Python Software Foundation");

        var candidates = LeftoverCandidates.For(
            python,
            Folders,
            [],
            [@"C:\Users\m\AppData\Roaming\Python\Python312\Scripts", @"C:\ProgramData\Python\service", @"C:\"]);

        Assert.DoesNotContain(@"C:\Users\m\AppData\Roaming\Python", candidates);
        Assert.DoesNotContain(@"C:\ProgramData\Python", candidates);
        Assert.Contains(@"C:\Users\m\AppData\Local\Python", candidates);
    }

    [Fact]
    public void UnrelatedInstalledApps_DoNotVetoAnything()
    {
        var discord = App("Discord", "Discord Inc.", @"C:\Users\m\AppData\Local\Discord");
        var git = App("Git", location: @"C:\Program Files\Git");

        Assert.Contains(@"C:\Users\m\AppData\Roaming\Discord", LeftoverCandidates.For(discord, Folders, [git, discord]));
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
