using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SoftwareDock.Application.Inventory;
using SoftwareDock.Domain.Packages;

namespace SoftwareDock.Application.Tests;

public sealed class SoftwareInventoryTests : IDisposable
{
    private readonly FakeRegistry _registry = new();
    private readonly FakePackageManager _packages = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-11-04T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly SoftwareInventory _sut;

    public SoftwareInventoryTests()
    {
        _sut = new SoftwareInventory(
            _registry,
            _packages,
            _time,
            Options.Create(new InventoryOptions { CacheDuration = TimeSpan.FromMinutes(5) }),
            NullLogger<SoftwareInventory>.Instance);
    }

    public void Dispose() => _sut.Dispose();

    [Fact]
    public async Task Snapshot_IsServedFromCacheWhileFresh()
    {
        await _sut.GetSnapshotAsync(false, CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(4));
        await _sut.GetSnapshotAsync(false, CancellationToken.None);

        Assert.Equal(1, _registry.Reads);
    }

    [Fact]
    public async Task Snapshot_IsRebuiltWhenExpiredInvalidatedOrForced()
    {
        await _sut.GetSnapshotAsync(false, CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(6));
        await _sut.GetSnapshotAsync(false, CancellationToken.None);
        _sut.Invalidate();
        await _sut.GetSnapshotAsync(false, CancellationToken.None);
        await _sut.GetSnapshotAsync(true, CancellationToken.None);

        Assert.Equal(4, _registry.Reads);
    }

    [Fact]
    public async Task PackageManagerFailure_StillListsTheRegistry_AndSaysWhy()
    {
        _registry.Entries.Add(Apps.Registry("{A}", "App"));
        _packages.Throw = new InvalidOperationException("winget exploded");

        var snapshot = await _sut.GetSnapshotAsync(false, CancellationToken.None);

        Assert.Single(snapshot.Applications);
        Assert.True(snapshot.IsPartial);
        Assert.Contains("winget exploded", Assert.Single(snapshot.Warnings));
    }

    [Fact]
    public async Task Snapshot_MergesBothSources_AndCountsUpdates()
    {
        _registry.Entries.Add(Apps.Registry("Git_is1", "Git", version: "2.45.0"));
        _packages.Installed.Add(new InstalledPackage("Git", PackageId.Parse("Git.Git"), "2.45.0", "2.47.1", "winget"));

        var snapshot = await _sut.GetSnapshotAsync(false, CancellationToken.None);

        Assert.Equal(1, snapshot.UpdateCount);
        Assert.Equal("Git.Git", snapshot.Applications[0].PackageId!.Value);
    }

    [Fact]
    public async Task SnapshotChanged_IsRaisedOnRebuildOnly()
    {
        var raised = 0;
        _sut.SnapshotChanged += (_, _) => raised++;

        await _sut.GetSnapshotAsync(false, CancellationToken.None);
        await _sut.GetSnapshotAsync(false, CancellationToken.None);

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task ConcurrentCallers_ShareOneRebuild()
    {
        var slow = new TaskCompletionSource<IReadOnlyList<Domain.Applications.InstalledApplication>>();
        var registry = new GatedRegistry(slow.Task);
        using var sut = new SoftwareInventory(registry, _packages, _time, Options.Create(new InventoryOptions()), NullLogger<SoftwareInventory>.Instance);

        var first = sut.GetSnapshotAsync(false, CancellationToken.None);
        var second = sut.GetSnapshotAsync(false, CancellationToken.None);
        slow.SetResult([]);

        Assert.Same(await first, await second);
        Assert.Equal(1, registry.Reads);
    }

    private sealed class GatedRegistry(Task<IReadOnlyList<Domain.Applications.InstalledApplication>> result) : Abstractions.IInstalledApplicationSource
    {
        public int Reads { get; private set; }

        public Task<IReadOnlyList<Domain.Applications.InstalledApplication>> GetInstalledApplicationsAsync(CancellationToken cancellationToken)
        {
            Reads++;
            return result;
        }
    }
}
