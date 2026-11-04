namespace SoftwareDock.Application.Inventory;

public sealed class InventoryOptions
{
    public const string SectionName = "Inventory";

    /// <summary>
    /// How long a snapshot is served from cache. Listing through WinGet takes seconds, so the
    /// dock opening on a hotkey must not pay for it every time.
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(5);
}
