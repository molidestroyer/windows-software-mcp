using System.IO.Pipes;
using System.Text;
using SoftwareDock.App.Services;
using SoftwareDock.App.Settings;
using SoftwareDock.Application.Inventory;
using SoftwareDock.Application.Operations;
using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Operations;
using SoftwareDock.Domain.Packages;

namespace SoftwareDock.App.Tests;

public sealed class BatchElevationTests
{
    [Fact]
    public async Task Protocol_RunsTheBatchOnTheWorkerSide_AndRelaysProgressAndResult()
    {
        var worker = new RecordingOperations();
        using var toWorker = new AnonymousPipeServerStream(PipeDirection.Out);
        using var workerIn = new AnonymousPipeClientStream(PipeDirection.In, toWorker.ClientSafePipeHandle);
        using var toDock = new AnonymousPipeServerStream(PipeDirection.Out);
        using var dockIn = new AnonymousPipeClientStream(PipeDirection.In, toDock.ClientSafePipeHandle);

        var serve = Task.Run(() => BatchProtocol.ServeAsync(new StreamReader(workerIn), new StreamWriter(toDock, new UTF8Encoding(false)), worker, CancellationToken.None));
        var progress = new List<BatchProgress>();
        var request = new BatchRequest(BatchKind.Uninstall, [@"ARP\Machine\X64\{A}", @"ARP\User\X64\B_is1"]);

        var result = await BatchProtocol.RequestAsync(
            new StreamReader(dockIn),
            new StreamWriter(toWorker, new UTF8Encoding(false)),
            request,
            new SyncProgress(progress.Add),
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await serve;

        Assert.Equal([@"ARP\Machine\X64\{A}", @"ARP\User\X64\B_is1"], worker.Keys);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(OperationStatus.SucceededRebootRequired, result.Items[1].Result!.Status);
        Assert.Equal(["{A}", "B_is1"], progress.Select(p => p.ItemName));
    }

    [Theory]
    [InlineData("-evil --override")]
    [InlineData("Id\"With\"Quotes")]
    public async Task Execute_RejectsMalformedPackageIds(string id)
    {
        var request = new BatchRequest(BatchKind.Install, [id], [null]);

        await Assert.ThrowsAsync<ArgumentException>(() => BatchProtocol.ExecuteAsync(request, new RecordingOperations(), null, CancellationToken.None));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Runner_UsesTheElevatorOnlyWhenTheSettingIsOn(bool elevate)
    {
        var operations = new RecordingOperations();
        var elevator = new RecordingElevator();
        var inventory = new CountingInventory();
        var runner = new BatchRunner(operations, inventory, new InMemoryUserSettingsStore(new UserSettings { ElevateBatches = elevate }), elevator);

        await runner.InstallManyAsync([new InstallRequest(PackageId.Parse("VideoLAN.VLC"), Source: "winget")], null);

        Assert.Equal(elevate ? 1 : 0, elevator.Requests.Count);
        Assert.Equal(elevate ? 0 : 1, operations.Installs.Count);
        Assert.Equal(elevate ? 1 : 0, inventory.Invalidations);
        if (elevate)
        {
            Assert.Equal(new BatchRequest(BatchKind.Install, ["VideoLAN.VLC"], ["winget"]).Kind, elevator.Requests[0].Kind);
            Assert.Equal(["winget"], elevator.Requests[0].Sources!);
        }
    }

    [Fact]
    public void Cancelled_MarksEveryItemCancelled_SoTheBatchSummaryReadsSkipped()
    {
        var result = BatchProtocol.Cancelled(new BatchRequest(BatchKind.Upgrade, ["a", "b"]), "declined");

        Assert.Equal(2, result.Skipped);
        Assert.Equal(0, result.Failed);
    }

    private sealed class SyncProgress(Action<BatchProgress> report) : IProgress<BatchProgress>
    {
        public void Report(BatchProgress value) => report(value);
    }

    private sealed class RecordingElevator : IBatchElevator
    {
        public List<BatchRequest> Requests { get; } = [];

        public Task<BatchResult> RunAsync(BatchRequest request, IProgress<BatchProgress>? progress, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new BatchResult([]));
        }
    }

    private sealed class CountingInventory : ISoftwareInventory
    {
        public int Invalidations { get; private set; }

        public event EventHandler<InventorySnapshot>? SnapshotChanged { add { } remove { } }

        public Task<InventorySnapshot> GetSnapshotAsync(bool forceRefresh, CancellationToken cancellationToken) => throw new NotSupportedException();

        public void Invalidate() => Invalidations++;

        public Task<InstalledApplication?> FindAsync(ApplicationKey key, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingOperations : ISoftwareOperations
    {
        public List<string> Keys { get; } = [];

        public List<InstallRequest> Installs { get; } = [];

        public event EventHandler<OperationCompletedEventArgs>? OperationCompleted { add { } remove { } }

        public Task<OperationResult> InstallAsync(InstallRequest request, IProgress<string>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OperationResult> UpgradeAsync(ApplicationKey key, IProgress<string>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OperationResult> UninstallAsync(ApplicationKey key, UninstallMode mode, IProgress<string>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BatchResult> UninstallManyAsync(IReadOnlyList<ApplicationKey> keys, UninstallMode mode, IProgress<BatchProgress>? progress, CancellationToken cancellationToken)
        {
            var items = new List<BatchItemResult>();
            for (var i = 0; i < keys.Count; i++)
            {
                Keys.Add(keys[i].Value);
                var name = keys[i].Value.Split('\\')[^1];
                progress?.Report(new BatchProgress(i + 1, keys.Count, name));
                items.Add(new BatchItemResult(name, i == 0 ? OperationResult.Success("ok") : new OperationResult(OperationStatus.SucceededRebootRequired, "reboot", 3010)));
            }

            return Task.FromResult(new BatchResult(items));
        }

        public Task<BatchResult> UpgradeManyAsync(IReadOnlyList<ApplicationKey> keys, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BatchResult> InstallManyAsync(IReadOnlyList<InstallRequest> requests, IProgress<BatchProgress>? progress, CancellationToken cancellationToken)
        {
            Installs.AddRange(requests);
            return Task.FromResult(new BatchResult([]));
        }
    }
}
