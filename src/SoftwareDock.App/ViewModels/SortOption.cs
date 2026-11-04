using SoftwareDock.App.Settings;

namespace SoftwareDock.App.ViewModels;

public sealed record SortOption(SortOrder Order, string Label)
{
    public static IReadOnlyList<SortOption> All { get; } =
    [
        new(SortOrder.Name, "Name"),
        new(SortOrder.Size, "Size on disk"),
        new(SortOrder.RecentlyInstalled, "Recently installed"),
        new(SortOrder.MemoryUse, "Memory in use"),
    ];

    public override string ToString() => Label;
}
