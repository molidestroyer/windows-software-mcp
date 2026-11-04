using SoftwareDock.App.Platform;

namespace SoftwareDock.App.Tests;

public class IconLocatorTests
{
    [Fact]
    public void PrefersTheExecutableNamedLikeTheApp()
    {
        var chosen = IconLocator.Choose("Microsoft Visual Studio Code", [("Code.exe", 150_000_000), ("unins000.exe", 3_000_000), ("bin.exe", 900_000_000)]);

        Assert.Equal("Code.exe", chosen);
    }

    [Fact]
    public void FallsBackToTheLargestPlausibleExecutable()
    {
        var chosen = IconLocator.Choose("Contoso Reporting 3", [("crashpad_handler.exe", 5_000_000), ("main.exe", 40_000_000), ("aux.exe", 1_000)]);

        Assert.Equal("main.exe", chosen);
    }

    [Fact]
    public void NeverPicksUninstallersOrUpdaters()
    {
        Assert.Null(IconLocator.Choose("Thing", [("uninstall.exe", 10), ("Update.exe", 10), ("setup.exe", 10)]));
    }
}
