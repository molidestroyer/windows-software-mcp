using SoftwareDock.Domain.Applications;

namespace SoftwareDock.Application.Inventory;

/// <summary>An immutable picture of installed software at one point in time.</summary>
/// <param name="Applications">Every application, sorted by name.</param>
/// <param name="TakenAt">When it was taken.</param>
/// <param name="Warnings">Sources that failed. The snapshot is still usable — just partial.</param>
public sealed record InventorySnapshot(
    IReadOnlyList<InstalledApplication> Applications,
    DateTimeOffset TakenAt,
    IReadOnlyList<string> Warnings)
{
    public int UpdateCount => Applications.Count(a => a.CanUpgrade);

    public bool IsPartial => Warnings.Count > 0;

    public InstalledApplication? Find(ApplicationKey key) => Applications.FirstOrDefault(a => a.Key == key);

    public IEnumerable<InstalledApplication> Query(string? text) => Applications.Where(a => a.Matches(text));
}
