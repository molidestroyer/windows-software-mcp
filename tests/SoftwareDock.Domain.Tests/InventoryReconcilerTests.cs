using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Inventory;
using SoftwareDock.Domain.Packages;

namespace SoftwareDock.Domain.Tests;

public class InventoryReconcilerTests
{
    private static InstalledApplication Registry(string subKey, string name, string? version = null, InstallScope scope = InstallScope.Machine) => new()
    {
        Key = ApplicationKey.ForRegistry(scope, RegistryView.Native64, subKey),
        Name = name,
        Version = version,
        Origin = ApplicationOrigin.Registry,
        Scope = scope,
    };

    private static InstalledPackage Package(string name, string id, string? version = null, string? available = null, string? source = null) =>
        new(name, PackageId.Parse(id), version, available, source);

    [Fact]
    public void LocalPackageWithArpId_MatchesItsRegistryEntryExactly()
    {
        var entry = Registry("{ABC}", "7-Zip 24.08 (x64)", "24.08");

        var result = InventoryReconciler.Reconcile([entry], [Package("7-Zip 24.08 (x64)", @"ARP\Machine\X64\{ABC}", "24.08")]);

        var app = Assert.Single(result);
        Assert.Equal(ApplicationOrigin.Registry, app.Origin);
        Assert.Equal(@"ARP\Machine\X64\{ABC}", app.PackageId!.Value);
        Assert.Null(app.PackageSource);
        Assert.False(app.CanUpgrade);
    }

    [Fact]
    public void SourcePackage_IsMatchedByName_AndBringsItsUpdate()
    {
        var entry = Registry("Git_is1", "Git", "2.45.0");

        var result = InventoryReconciler.Reconcile([entry], [Package("Git", "Git.Git", "2.45.0", "2.47.1", "winget")]);

        var app = Assert.Single(result);
        Assert.Equal("Git.Git", app.PackageId!.Value);
        Assert.Equal("2.47.1", app.AvailableVersion);
        Assert.True(app.CanUpgrade);
    }

    [Fact]
    public void TruncatedPackageName_MatchesByPrefix()
    {
        var entry = Registry("{VS}", "Microsoft Visual Studio Installer");

        var result = InventoryReconciler.Reconcile([entry], [Package("Microsoft Visual Studio Inst…", "Microsoft.VisualStudio.Installer", source: "winget")]);

        Assert.Equal("Microsoft.VisualStudio.Installer", Assert.Single(result).PackageId!.Value);
    }

    [Fact]
    public void SameNameTwice_VersionDecidesWhichEntryIsWhich()
    {
        var old = Registry("{OLD}", "Python Launcher", "3.11.0");
        var current = Registry("{NEW}", "Python Launcher", "3.12.4");

        var result = InventoryReconciler.Reconcile([old, current], [Package("Python Launcher", "Python.Launcher", "3.12.4", source: "winget")]);

        Assert.Equal("Python.Launcher", result.Single(a => a.Key == current.Key).PackageId!.Value);
        Assert.Null(result.Single(a => a.Key == old.Key).PackageId);
    }

    [Fact]
    public void SameNameTwice_WithoutAMatchingVersion_IsLeftAloneRatherThanGuessed()
    {
        var a = Registry("{A}", "Runtime", "1.0");
        var b = Registry("{B}", "Runtime", "2.0");

        var result = InventoryReconciler.Reconcile([a, b], [Package("Runtime", "Vendor.Runtime", "3.0", source: "winget")]);

        Assert.Equal(3, result.Count);
        Assert.All(result.Where(r => r.Origin == ApplicationOrigin.Registry), r => Assert.Null(r.PackageId));
    }

    [Fact]
    public void MsixPackage_WithoutRegistryEntry_IsAddedAsPackaged()
    {
        var result = InventoryReconciler.Reconcile([], [Package("Windows Terminal", @"MSIX\Microsoft.WindowsTerminal_1.21_x64__8wekyb3d8bbwe", "1.21")]);

        var app = Assert.Single(result);
        Assert.Equal(ApplicationOrigin.Packaged, app.Origin);
        Assert.True(app.CanUninstall);
    }

    [Fact]
    public void Result_IsSortedByName()
    {
        var result = InventoryReconciler.Reconcile([Registry("b", "beta"), Registry("a", "Alpha")], []);

        Assert.Equal(["Alpha", "beta"], result.Select(a => a.Name));
    }

    [Fact]
    public void EachRegistryEntry_IsClaimedAtMostOnce()
    {
        var entry = Registry("{X}", "Tool", "1.0");

        var result = InventoryReconciler.Reconcile(
            [entry],
            [Package("Tool", "Vendor.Tool", "1.0", source: "winget"), Package("Tool", "Other.Tool", "1.0", source: "msstore")]);

        Assert.Equal(2, result.Count);
        Assert.Equal("Vendor.Tool", result.Single(a => a.Origin == ApplicationOrigin.Registry).PackageId!.Value);
    }
}
