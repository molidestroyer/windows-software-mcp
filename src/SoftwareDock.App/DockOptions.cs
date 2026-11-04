namespace SoftwareDock.App;

public sealed class DockOptions
{
    public const string SectionName = "Dock";

    /// <summary>Global shortcut that toggles the dock, e.g. <c>Win+Shift+I</c>. Empty disables it.</summary>
    public string Hotkey { get; set; } = "Win+Shift+I";

    /// <summary>Panel width in device-independent pixels.</summary>
    public double Width { get; set; } = 420;

    /// <summary>Gap between the panel and the screen edges, in device-independent pixels.</summary>
    public double Margin { get; set; } = 8;
}
