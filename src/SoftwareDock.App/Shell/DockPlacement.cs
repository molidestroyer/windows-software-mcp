using Avalonia;

namespace SoftwareDock.App.Shell;

/// <summary>
/// Where the anchored panel goes: flush with the right edge of the working area (the screen
/// minus the taskbar), full height, like CasaAdminDock's panel.
/// </summary>
/// <remarks>
/// The working area is in physical pixels and the window size in device-independent pixels;
/// mixing them is the classic multi-DPI bug, so the conversion lives here and is tested.
/// </remarks>
public static class DockPlacement
{
    public static (PixelPoint Position, Size Size) Anchored(PixelRect workingArea, double scaling, double width, double margin)
    {
        if (scaling <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scaling), scaling, "Scaling must be positive.");
        }

        var area = workingArea.ToRect(scaling);
        var clampedWidth = Math.Min(width, Math.Max(0, area.Width - (2 * margin)));
        var height = Math.Max(0, area.Height - (2 * margin));

        var left = area.Right - clampedWidth - margin;
        var top = area.Top + margin;
        var position = new PixelPoint((int)Math.Round(left * scaling), (int)Math.Round(top * scaling));
        return (position, new Size(clampedWidth, height));
    }
}
