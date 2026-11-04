using Avalonia;
using SoftwareDock.App.Shell;

namespace SoftwareDock.App.Tests;

public class DockPlacementTests
{
    [Fact]
    public void Anchored_At100Percent_HugsTheRightEdgeFullHeight()
    {
        // 1920×1080 with a 48 px taskbar at the bottom.
        var (position, size) = DockPlacement.Anchored(new PixelRect(0, 0, 1920, 1032), scaling: 1, width: 420, margin: 8);

        Assert.Equal(new PixelPoint(1492, 8), position);
        Assert.Equal(new Size(420, 1016), size);
    }

    [Fact]
    public void Anchored_At150Percent_ConvertsBetweenPixelsAndDips()
    {
        // 2560×1440 physical at 150 % → 1706.67×928 DIPs of working area.
        var (position, size) = DockPlacement.Anchored(new PixelRect(0, 0, 2560, 1392), scaling: 1.5, width: 420, margin: 8);

        Assert.Equal(new Size(420, 912), size);
        Assert.Equal(2560 - (int)Math.Round((420 + 8) * 1.5), position.X);
        Assert.Equal(12, position.Y);
    }

    [Fact]
    public void Anchored_OnASecondaryMonitorWithTaskbarOnTop_UsesItsWorkingArea()
    {
        var (position, _) = DockPlacement.Anchored(new PixelRect(1920, 40, 1920, 1040), scaling: 1, width: 420, margin: 8);

        Assert.Equal(new PixelPoint(1920 + 1920 - 428, 48), position);
    }

    [Fact]
    public void Anchored_NarrowScreen_NeverOverflows()
    {
        var (position, size) = DockPlacement.Anchored(new PixelRect(0, 0, 300, 600), scaling: 1, width: 420, margin: 8);

        Assert.Equal(284, size.Width);
        Assert.Equal(8, position.X);
    }
}
