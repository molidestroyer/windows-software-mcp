using SoftwareDock.Application.Abstractions;
using SoftwareDock.Application.Inventory;
using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Operations;
using SoftwareDock.Domain.Startup;

namespace SoftwareDock.Application.Startup;

/// <summary>A startup entry, with the installed application it belongs to when that can be told.</summary>
public sealed record StartupEntry(StartupItem Item, InstalledApplication? Application)
{
    public string DisplayName => Application?.Name ?? Item.Name;
}

/// <summary>Use case: see and switch what starts with Windows.</summary>
public sealed class StartupManager(IStartupItemSource source, ISoftwareInventory inventory)
{
    public async Task<IReadOnlyList<StartupEntry>> ListAsync(CancellationToken cancellationToken)
    {
        var itemsTask = source.ListAsync(cancellationToken);
        var snapshotTask = inventory.GetSnapshotAsync(forceRefresh: false, cancellationToken);
        await Task.WhenAll(itemsTask, snapshotTask);

        var snapshot = await snapshotTask;
        var ownerOf = ApplicationRoots.Lookup(snapshot.Applications);
        return (await itemsTask)
            .Select(i => new StartupEntry(i, ownerOf(i.ExecutablePath) is { } key ? snapshot.Find(key) : null))
            .OrderByDescending(e => e.Item.IsEnabled)
            .ThenBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public Task<OperationResult> SetEnabledAsync(StartupItem item, bool enabled, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.IsEnabled == enabled
            ? Task.FromResult(OperationResult.Success($"{item.Name} is already {(enabled ? "enabled" : "disabled")}."))
            : source.SetEnabledAsync(item, enabled, cancellationToken);
    }
}
