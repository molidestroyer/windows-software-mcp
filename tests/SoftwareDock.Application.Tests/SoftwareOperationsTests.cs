using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SoftwareDock.Application.Inventory;
using SoftwareDock.Application.Operations;
using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Operations;
using SoftwareDock.Domain.Packages;

namespace SoftwareDock.Application.Tests;

public sealed class SoftwareOperationsTests : IDisposable
{
    private const string MsiUninstall = "MsiExec.exe /I{23170F69-40C1-2702-2301-000001000000}";

    private readonly FakeRegistry _registry = new();
    private readonly FakePackageManager _packages = new();
    private readonly FakeUninstaller _uninstaller = new();
    private readonly FakeProcesses _processes = new();
    private readonly FakeTimeProvider _time = new();
    private readonly SoftwareInventory _inventory;
    private readonly SoftwareOperations _sut;

    public SoftwareOperationsTests()
    {
        _inventory = new SoftwareInventory(_registry, _packages, TimeProvider.System, Options.Create(new InventoryOptions()), NullLogger<SoftwareInventory>.Instance);
        _sut = new SoftwareOperations(_inventory, _packages, _uninstaller, _registry, _processes, _time, NullLogger<SoftwareOperations>.Instance);
    }

    public void Dispose()
    {
        _sut.Dispose();
        _inventory.Dispose();
    }

    private static ApplicationKey Key(string subKey) => ApplicationKey.ForRegistry(InstallScope.Machine, RegistryView.Native64, subKey);

    [Fact]
    public async Task Uninstall_KnownToThePackageManager_GoesThroughIt()
    {
        _registry.Entries.Add(Apps.Registry("Git_is1", "Git", uninstall: @"""C:\Git\unins000.exe"""));
        _packages.Installed.Add(new InstalledPackage("Git", PackageId.Parse("Git.Git"), "1.0", null, "winget"));

        var result = await _sut.UninstallAsync(Key("Git_is1"), UninstallMode.Silent, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["uninstall Git.Git silent=True"], _packages.Calls);
        Assert.Empty(_uninstaller.Runs);
    }

    [Fact]
    public async Task Uninstall_Silent_PrefersTheQuietUninstallString()
    {
        _registry.Entries.Add(Apps.Registry("{A}", "App", uninstall: @"C:\A\uninstall.exe", quiet: @"C:\A\uninstall.exe /S"));

        await _sut.UninstallAsync(Key("{A}"), UninstallMode.Silent, null, CancellationToken.None);

        var run = Assert.Single(_uninstaller.Runs);
        Assert.Equal("/S", run.Command.Arguments);
    }

    [Fact]
    public async Task Uninstall_SilentMsi_RunsQuietly()
    {
        _registry.Entries.Add(Apps.Registry("{M}", "Msi app", uninstall: MsiUninstall));

        await _sut.UninstallAsync(Key("{M}"), UninstallMode.Silent, null, CancellationToken.None);

        Assert.True(Assert.Single(_uninstaller.Runs).Silent);
    }

    [Fact]
    public async Task Uninstall_SilentPerMachineMsi_UsesMsiexecEvenWhenWinGetKnowsIt()
    {
        _registry.Entries.Add(Apps.Registry("{M}", "Msi app", uninstall: MsiUninstall));
        _packages.Installed.Add(new InstalledPackage("Msi app", PackageId.Parse("Vendor.MsiApp"), "1.0", null, "winget"));

        await _sut.UninstallAsync(Key("{M}"), UninstallMode.Silent, null, CancellationToken.None);

        Assert.Empty(_packages.Calls);
        Assert.True(Assert.Single(_uninstaller.Runs).Command.IsWindowsInstaller);
    }

    [Fact]
    public async Task Uninstall_InteractivePerMachineMsi_StillPrefersWinGet()
    {
        _registry.Entries.Add(Apps.Registry("{M}", "Msi app", uninstall: MsiUninstall));
        _packages.Installed.Add(new InstalledPackage("Msi app", PackageId.Parse("Vendor.MsiApp"), "1.0", null, "winget"));

        await _sut.UninstallAsync(Key("{M}"), UninstallMode.Interactive, null, CancellationToken.None);

        Assert.Equal(["uninstall Vendor.MsiApp silent=False"], _packages.Calls);
    }

    [Fact]
    public async Task Uninstall_SilentUnknownExe_FallsBackToInteractive()
    {
        _registry.Entries.Add(Apps.Registry("{E}", "Exe app", uninstall: @"""C:\E\remove.exe"""));

        await _sut.UninstallAsync(Key("{E}"), UninstallMode.Silent, null, CancellationToken.None);

        Assert.False(Assert.Single(_uninstaller.Runs).Silent);
    }

    [Fact]
    public async Task Uninstall_TruncatedPackageId_FallsBackToTheRegisteredUninstaller()
    {
        _registry.Entries.Add(Apps.Registry("{T}", "Truncated", uninstall: MsiUninstall));
        _packages.Installed.Add(new InstalledPackage("Truncated", PackageId.Parse("Vendor.Truncat…"), "1.0", null, "winget"));

        await _sut.UninstallAsync(Key("{T}"), UninstallMode.Interactive, null, CancellationToken.None);

        Assert.Empty(_packages.Calls);
        Assert.Single(_uninstaller.Runs);
    }

    [Fact]
    public async Task Uninstall_NothingToRunIt_FailsWithAReason()
    {
        _registry.Entries.Add(Apps.Registry("{N}", "Orphan"));

        var result = await _sut.UninstallAsync(Key("{N}"), UninstallMode.Interactive, null, CancellationToken.None);

        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.Contains("Orphan", result.Message);
    }

    [Fact]
    public async Task Uninstall_UnknownKey_Fails()
    {
        var result = await _sut.UninstallAsync(Key("{missing}"), UninstallMode.Interactive, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Upgrade_WithoutUpdate_DoesNotCallThePackageManager()
    {
        _registry.Entries.Add(Apps.Registry("Git_is1", "Git"));
        _packages.Installed.Add(new InstalledPackage("Git", PackageId.Parse("Git.Git"), "1.0", null, "winget"));

        var result = await _sut.UpgradeAsync(Key("Git_is1"), null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(_packages.Calls);
    }

    [Fact]
    public async Task Upgrade_WithUpdate_UsesThePackageId()
    {
        _registry.Entries.Add(Apps.Registry("Git_is1", "Git"));
        _packages.Installed.Add(new InstalledPackage("Git", PackageId.Parse("Git.Git"), "1.0", "2.0", "winget"));

        await _sut.UpgradeAsync(Key("Git_is1"), null, CancellationToken.None);

        Assert.Equal(["upgrade Git.Git"], _packages.Calls);
    }

    [Fact]
    public async Task AnyOperation_InvalidatesTheInventory_EvenWhenItFails()
    {
        await _inventory.GetSnapshotAsync(false, CancellationToken.None);
        _packages.NextResult = OperationResult.Failure("boom", 1603);

        await _sut.InstallAsync(new InstallRequest(PackageId.Parse("Git.Git")), null, CancellationToken.None);
        await _inventory.GetSnapshotAsync(false, CancellationToken.None);

        Assert.Equal(2, _registry.Reads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Uninstall_HandedOffToTemp_WaitsUntilTheEntryIsGone(bool finishes)
    {
        // Inno/NSIS: unins000.exe relaunches from %TEMP% and exits at once (QA, 0.3.2).
        _registry.Entries.Add(Apps.Registry("App_is1", "Inno app", uninstall: @"""C:\Apps\Inno\unins000.exe"""));
        _processes.ImagePaths.Add(Path.Combine(Path.GetTempPath(), "is-QA123.tmp", "unins000.tmp"));
        var lines = new List<string>();

        var uninstall = _sut.UninstallAsync(Key("App_is1"), UninstallMode.Interactive, new SyncProgress<string>(lines.Add), CancellationToken.None);
        for (var i = 0; i < 3; i++)
        {
            await Task.Delay(20);
            _time.Advance(TimeSpan.FromSeconds(2));
        }

        Assert.False(uninstall.IsCompleted);
        Assert.Equal(["Waiting for the uninstaller window to finish…"], lines);

        _processes.ImagePaths.Clear();
        if (finishes)
        {
            _registry.Entries.Clear();
        }

        _time.Advance(TimeSpan.FromSeconds(2));
        var result = await uninstall.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(finishes, result.IsSuccess);
        if (!finishes)
        {
            Assert.Contains("did not finish", result.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Uninstall_NoHandOff_ReturnsAtOnceEvenIfTheEntryLingers()
    {
        _registry.Entries.Add(Apps.Registry("App_is1", "Inno app", uninstall: @"""C:\Apps\Inno\unins000.exe"""));

        var result = await _sut.UninstallAsync(Key("App_is1"), UninstallMode.Interactive, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Operations_RunOneAtATime()
    {
        var gate = new BlockingPackageManager();
        using var inventory = new SoftwareInventory(_registry, gate, TimeProvider.System, Options.Create(new InventoryOptions()), NullLogger<SoftwareInventory>.Instance);
        using var sut = new SoftwareOperations(inventory, gate, _uninstaller, _registry, _processes, _time, NullLogger<SoftwareOperations>.Instance);

        var first = sut.InstallAsync(new InstallRequest(PackageId.Parse("A.A")), null, CancellationToken.None);
        var second = sut.InstallAsync(new InstallRequest(PackageId.Parse("B.B")), null, CancellationToken.None);
        await gate.Started.Task;

        Assert.Equal(1, gate.Running);
        gate.Release.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(1, gate.MaxConcurrent);
    }

    [Fact]
    public async Task OperationCompleted_IsRaisedWithTheResult()
    {
        OperationCompletedEventArgs? seen = null;
        _sut.OperationCompleted += (_, e) => seen = e;

        await _sut.InstallAsync(new InstallRequest(PackageId.Parse("Git.Git")), null, CancellationToken.None);

        Assert.Equal("Install Git.Git", seen!.Description);
        Assert.True(seen.Result.IsSuccess);
    }

    private sealed class BlockingPackageManager : Abstractions.IPackageManager
    {
        private int _running;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Running => _running;

        public int MaxConcurrent { get; private set; }

        public Task<IReadOnlyList<InstalledPackage>> ListInstalledAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InstalledPackage>>([]);

        public Task<IReadOnlyList<PackageSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PackageSearchResult>>([]);

        public async Task<OperationResult> InstallAsync(InstallRequest request, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            MaxConcurrent = Math.Max(MaxConcurrent, Interlocked.Increment(ref _running));
            Started.TrySetResult();
            await Release.Task;
            Interlocked.Decrement(ref _running);
            return OperationResult.Success("ok");
        }

        public Task<OperationResult> UpgradeAsync(PackageId id, IProgress<string>? progress, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult> UninstallAsync(PackageId id, bool silent, IProgress<string>? progress, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
