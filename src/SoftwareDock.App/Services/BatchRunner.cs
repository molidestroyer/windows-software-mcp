using System.Text.Json;
using System.Text.Json.Serialization;
using SoftwareDock.App.Settings;
using SoftwareDock.Application.Inventory;
using SoftwareDock.Application.Operations;
using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Operations;
using SoftwareDock.Domain.Packages;

namespace SoftwareDock.App.Services;

public enum BatchKind
{
    Uninstall,
    Upgrade,
    Install,
}

/// <summary>
/// A batch the dock asks for. <see cref="Items"/> are application keys (uninstall, upgrade) or
/// package ids (install, with <see cref="Sources"/> alongside). Plain strings on purpose: this
/// crosses into the elevated worker, which re-validates every item itself.
/// </summary>
public sealed record BatchRequest(BatchKind Kind, IReadOnlyList<string> Items, IReadOnlyList<string?>? Sources = null);

/// <summary>Runs a batch elevated (one UAC prompt for the whole batch) or in process.</summary>
public interface IBatchElevator
{
    Task<BatchResult> RunAsync(BatchRequest request, IProgress<BatchProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>Demo mode and non-Windows: nothing to elevate, the batch runs here.</summary>
public sealed class InProcessBatchElevator(ISoftwareOperations operations) : IBatchElevator
{
    public Task<BatchResult> RunAsync(BatchRequest request, IProgress<BatchProgress>? progress, CancellationToken cancellationToken) =>
        BatchProtocol.ExecuteAsync(request, operations, progress, cancellationToken);
}

/// <summary>
/// The dock's batches (bulk uninstall, Update all, restore). With "administrator rights once per
/// batch" on, the whole batch goes to the elevated worker; otherwise each installer asks for UAC
/// itself, as before. Scheduled updates never come here: nobody is there to answer a prompt.
/// </summary>
public sealed class BatchRunner(ISoftwareOperations operations, ISoftwareInventory inventory, IUserSettingsStore settings, IBatchElevator elevator)
{
    public Task<BatchResult> UninstallManyAsync(IReadOnlyList<ApplicationKey> keys, IProgress<BatchProgress>? progress) =>
        RunAsync(new BatchRequest(BatchKind.Uninstall, keys.Select(k => k.Value).ToList()), progress);

    public Task<BatchResult> UpgradeManyAsync(IReadOnlyList<ApplicationKey> keys, IProgress<BatchProgress>? progress) =>
        RunAsync(new BatchRequest(BatchKind.Upgrade, keys.Select(k => k.Value).ToList()), progress);

    public Task<BatchResult> InstallManyAsync(IReadOnlyList<InstallRequest> requests, IProgress<BatchProgress>? progress) =>
        RunAsync(new BatchRequest(BatchKind.Install, requests.Select(r => r.Id.Value).ToList(), requests.Select(r => r.Source).ToList()), progress);

    private async Task<BatchResult> RunAsync(BatchRequest request, IProgress<BatchProgress>? progress)
    {
        if (!settings.Current.ElevateBatches)
        {
            return await BatchProtocol.ExecuteAsync(request, operations, progress, CancellationToken.None);
        }

        try
        {
            return await elevator.RunAsync(request, progress, CancellationToken.None);
        }
        finally
        {
            // The installers ran in another process: this one's cached snapshot knows nothing of it.
            inventory.Invalidate();
        }
    }
}

/// <summary>
/// One request line in, progress lines and one result line out (JSON lines over the worker's pipe).
/// </summary>
public static class BatchProtocol
{

    /// <summary>
    /// Validates and runs a request. Keys and ids are parsed again here: in the worker they come
    /// from another process, and only installed applications and well-formed package ids are acted on.
    /// </summary>
    public static Task<BatchResult> ExecuteAsync(BatchRequest request, ISoftwareOperations operations, IProgress<BatchProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(operations);
        return request.Kind switch
        {
            BatchKind.Uninstall => operations.UninstallManyAsync(request.Items.Select(ApplicationKey.Parse).ToList(), UninstallMode.Silent, progress, cancellationToken),
            BatchKind.Upgrade => operations.UpgradeManyAsync(request.Items.Select(ApplicationKey.Parse).ToList(), progress, cancellationToken),
            BatchKind.Install => operations.InstallManyAsync(
                request.Items.Select((id, i) => new InstallRequest(PackageId.Parse(id), Source: request.Sources?.ElementAtOrDefault(i))).ToList(),
                progress,
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Unknown batch kind."),
        };
    }

    /// <summary>Worker side: reads one request, runs it, streams progress and the result.</summary>
    public static async Task ServeAsync(TextReader input, TextWriter output, ISoftwareOperations operations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        var line = await input.ReadLineAsync(cancellationToken)
            ?? throw new InvalidOperationException("The dock closed the pipe before sending a batch.");
        var request = JsonSerializer.Deserialize(line, BatchJson.Default.BatchRequest)
            ?? throw new InvalidOperationException("Empty batch request.");

        var gate = new object();
        void Write(BatchMessage message)
        {
            lock (gate)
            {
                output.WriteLine(JsonSerializer.Serialize(message, BatchJson.Default.BatchMessage));
                output.Flush();
            }
        }

        var result = await ExecuteAsync(request, operations, new SyncProgress(p => Write(new BatchMessage(p, null))), cancellationToken);
        Write(new BatchMessage(null, result));
    }

    /// <summary>Dock side: sends the request, relays progress, returns the result.</summary>
    public static async Task<BatchResult> RequestAsync(TextReader input, TextWriter output, BatchRequest request, IProgress<BatchProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        await output.WriteLineAsync(JsonSerializer.Serialize(request, BatchJson.Default.BatchRequest).AsMemory(), cancellationToken);
        await output.FlushAsync(cancellationToken);
        while (await input.ReadLineAsync(cancellationToken) is { } line)
        {
            var message = JsonSerializer.Deserialize(line, BatchJson.Default.BatchMessage);
            if (message?.Result is { } result)
            {
                return result;
            }

            if (message?.Progress is { } p)
            {
                progress?.Report(p);
            }
        }

        throw new InvalidOperationException("The elevated worker stopped before finishing the batch.");
    }

    /// <summary>Every item of <paramref name="request"/> as cancelled with <paramref name="reason"/>.</summary>
    public static BatchResult Cancelled(BatchRequest request, string reason)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new BatchResult(request.Items.Select(i => new BatchItemResult(i, new OperationResult(OperationStatus.Cancelled, reason))).ToList());
    }

    /// <summary><see cref="Progress{T}"/> posts to a sync context the worker does not have.</summary>
    private sealed class SyncProgress(Action<BatchProgress> report) : IProgress<BatchProgress>
    {
        public void Report(BatchProgress value) => report(value);
    }
}

/// <summary>One line from the worker: progress while it runs, the result at the end.</summary>
internal sealed record BatchMessage(BatchProgress? Progress, BatchResult? Result);

/// <summary>Source-generated: the worker must not depend on reflection-based serialization.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(BatchRequest))]
[JsonSerializable(typeof(BatchMessage))]
internal sealed partial class BatchJson : JsonSerializerContext;
