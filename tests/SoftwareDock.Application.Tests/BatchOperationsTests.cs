using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SoftwareDock.Application.Abstractions;
using SoftwareDock.Application.Inventory;
using SoftwareDock.Application.Operations;
using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Operations;
using SoftwareDock.Domain.Packages;

namespace SoftwareDock.Application.Tests;

public sealed class BatchOperationsTests : IDisposable
{
    private readonly FakeRegistry _registry = new();
    private readonly ScriptedPackageManager _packages = new();
    private readonly SoftwareInventory _inventory;
    private readonly SoftwareOperations _sut;

    public BatchOperationsTests()
    {
        foreach (var name in new[] { "A", "B", "C" })
        {
            _registry.Entries.Add(Apps.Registry(name, name));
            _packages.Installed.Add(new InstalledPackage(name, PackageId.Parse($"Vendor.{name}"), "1.0", null, "winget"));
        }

        _inventory = new SoftwareInventory(_registry, _packages, TimeProvider.System, Options.Create(new InventoryOptions()), NullLogger<SoftwareInventory>.Instance);
        _sut = new SoftwareOperations(_inventory, _packages, new FakeUninstaller(), _registry, new FakeProcesses(), TimeProvider.System, NullLogger<SoftwareOperations>.Instance);
    }

    public void Dispose()
    {
        _sut.Dispose();
        _inventory.Dispose();
    }

    private static ApplicationKey Key(string name) => ApplicationKey.ForRegistry(InstallScope.Machine, RegistryView.Native64, name);

    [Fact]
    public async Task UninstallMany_RunsEveryItemInOrder()
    {
        var progress = new List<BatchProgress>();

        var result = await _sut.UninstallManyAsync([Key("A"), Key("B"), Key("C")], UninstallMode.Silent, new SyncProgress<BatchProgress>(progress.Add), CancellationToken.None);

        Assert.Equal(3, result.Succeeded);
        Assert.Equal(["uninstall Vendor.A", "uninstall Vendor.B", "uninstall Vendor.C"], _packages.Calls);
        Assert.Equal([1, 2, 3], progress.Where(p => p.Line is null).Select(p => p.Position));
        Assert.Equal("3 of 3 done.", result.Summary);
    }

    [Fact]
    public async Task UninstallMany_AFailureMovesOnToTheNextItem()
    {
        _packages.Results["uninstall Vendor.B"] = OperationResult.Failure("boom", 1603);

        var result = await _sut.UninstallManyAsync([Key("A"), Key("B"), Key("C")], UninstallMode.Silent, null, CancellationToken.None);

        Assert.Equal(3, _packages.Calls.Count);
        Assert.Equal("2 of 3 done · 1 failed.", result.Summary);
    }

    [Fact]
    public async Task UninstallMany_CancellingAPromptStopsTheBatch()
    {
        _packages.Results["uninstall Vendor.A"] = new OperationResult(OperationStatus.Cancelled, "cancelled", 1602);

        var result = await _sut.UninstallManyAsync([Key("A"), Key("B"), Key("C")], UninstallMode.Silent, null, CancellationToken.None);

        Assert.Equal(["uninstall Vendor.A"], _packages.Calls);
        Assert.Equal(3, result.Skipped);
        Assert.True(result.Items[1].WasSkipped);
    }

    [Fact]
    public async Task UninstallMany_ResolvesTheInventoryOnce()
    {
        await _sut.UninstallManyAsync([Key("A"), Key("B"), Key("C")], UninstallMode.Silent, null, CancellationToken.None);

        Assert.Equal(1, _registry.Reads);
    }

    [Fact]
    public async Task UninstallMany_UnknownKeysFailWithoutStoppingTheRest()
    {
        var result = await _sut.UninstallManyAsync([Key("missing"), Key("A")], UninstallMode.Silent, null, CancellationToken.None);

        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Succeeded);
    }

    [Fact]
    public async Task InstallMany_MissingWinGet_StopsInsteadOfFailingEveryItem()
    {
        _packages.ThrowUnavailable = true;

        var result = await _sut.InstallManyAsync([new(PackageId.Parse("X.X")), new(PackageId.Parse("Y.Y"))], null, CancellationToken.None);

        Assert.Equal(1, result.Failed);
        Assert.True(result.Items[1].WasSkipped);
    }

    private sealed class ScriptedPackageManager : IPackageManager
    {
        public List<InstalledPackage> Installed { get; } = [];

        public Dictionary<string, OperationResult> Results { get; } = [];

        public List<string> Calls { get; } = [];

        public bool ThrowUnavailable { get; set; }

        public Task<IReadOnlyList<InstalledPackage>> ListInstalledAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InstalledPackage>>(Installed.ToList());

        public Task<IReadOnlyList<PackageSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PackageSearchResult>>([]);

        public Task<OperationResult> InstallAsync(InstallRequest request, IProgress<string>? progress, CancellationToken cancellationToken) =>
            ThrowUnavailable ? throw new PackageManagerUnavailableException("no winget") : Run($"install {request.Id}");

        public Task<OperationResult> UpgradeAsync(PackageId id, IProgress<string>? progress, CancellationToken cancellationToken) => Run($"upgrade {id}");

        public Task<OperationResult> UninstallAsync(PackageId id, bool silent, IProgress<string>? progress, CancellationToken cancellationToken) =>
            Run($"uninstall {id}");

        private Task<OperationResult> Run(string call)
        {
            Calls.Add(call);
            return Task.FromResult(Results.GetValueOrDefault(call, OperationResult.Success("ok")));
        }
    }
}
