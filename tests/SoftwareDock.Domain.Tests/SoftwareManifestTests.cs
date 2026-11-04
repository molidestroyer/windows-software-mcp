using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Backup;
using SoftwareDock.Domain.Packages;

namespace SoftwareDock.Domain.Tests;

public class SoftwareManifestTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

    private static InstalledApplication App(string name, string? id = null, string? source = null, string key = "") => new()
    {
        Key = ApplicationKey.Parse(string.IsNullOrEmpty(key) ? $@"ARP\Machine\X64\{name}" : key),
        Name = name,
        Origin = ApplicationOrigin.Registry,
        PackageId = id is null ? null : PackageId.Parse(id),
        PackageSource = source,
        Version = "1.0",
    };

    [Fact]
    public void From_SplitsReinstallablePackagesFromTheRest()
    {
        var manifest = SoftwareManifest.From(
            [
                App("Git", "Git.Git", "winget"),
                App("Spotify", "9NCBCSZSJRSB", "msstore"),
                App("Legacy tool", @"ARP\Machine\X64\Legacy tool"),
                App("Truncated", "Vendor.Trunc…", "winget"),
                App("Terminal", @"MSIX\Microsoft.WindowsTerminal_1.21", "msstore", @"MSIX\Microsoft.WindowsTerminal_1.21"),
            ],
            Now,
            "PC");

        Assert.Equal(["Git.Git", "9NCBCSZSJRSB"], manifest.Packages.Select(p => p.Id.Value));
        Assert.Equal(["Legacy tool", "Terminal", "Truncated"], manifest.Unmanaged.Select(u => u.Name));
    }

    [Fact]
    public void HasSameContentAs_IgnoresWhenAndWhere()
    {
        var apps = new[] { App("Git", "Git.Git", "winget") };

        Assert.True(SoftwareManifest.From(apps, Now, "A").HasSameContentAs(SoftwareManifest.From(apps, Now.AddDays(1), "B")));
        Assert.False(SoftwareManifest.From(apps, Now, "A").HasSameContentAs(SoftwareManifest.From([], Now, "A")));
    }

    [Fact]
    public void RestorePlan_InstallsOnlyWhatIsMissing_AndListsManualOnes()
    {
        var backup = SoftwareManifest.From(
            [App("Git", "Git.Git", "winget"), App("VLC media player", "VideoLAN.VLC", "winget"), App("Legacy tool"), App("Old driver")],
            Now,
            "old PC");

        var plan = RestorePlan.Create(backup, [App("Git", "Git.Git", "winget"), App("Old driver")]);

        Assert.Equal(["VideoLAN.VLC"], plan.ToInstall.Select(p => p.Id.Value));
        Assert.Equal(["Git.Git"], plan.AlreadyInstalled.Select(p => p.Id.Value));
        Assert.Equal(["Legacy tool"], plan.InstallManually.Select(u => u.Name));
    }
}
