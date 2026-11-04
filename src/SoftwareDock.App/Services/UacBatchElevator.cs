using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Extensions.Logging;
using SoftwareDock.App.Platform;
using SoftwareDock.Application.Operations;

namespace SoftwareDock.App.Services;

/// <summary>
/// One UAC prompt per batch: starts this same executable elevated with <c>--elevated-worker</c>,
/// hands it the batch over a named pipe and relays its progress. The worker runs one batch and exits.
/// </summary>
/// <remarks>
/// The pipe is the trust boundary. Only this user may open it (ACL), the dock accepts only the
/// process it started (client PID), the worker accepts only the dock that started it (server PID and
/// same executable), and the worker takes typed items it re-validates — never a command line.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed partial class UacBatchElevator(ILogger<UacBatchElevator> logger) : IBatchElevator
{
    public const string WorkerArgument = "--elevated-worker";
    private const int ErrorCancelled = 1223;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMinutes(2);

    public async Task<BatchResult> RunAsync(BatchRequest request, IProgress<BatchProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pipeName = $"SoftwareDock.Elevated.{Guid.NewGuid():N}";
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        await using var pipe = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);

        Process? worker;
        try
        {
            worker = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = string.Create(CultureInfo.InvariantCulture, $"{WorkerArgument} {pipeName} {Environment.ProcessId}"),
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return BatchProtocol.Cancelled(request, "Administrator approval was not given: the Windows prompt was declined.");
        }

        if (worker is null)
        {
            return Failed(request, "The elevated worker did not start.");
        }

        using (worker)
        {
            try
            {
                using var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connect.CancelAfter(ConnectTimeout);
                var exited = worker.WaitForExitAsync(connect.Token);
                var connected = pipe.WaitForConnectionAsync(connect.Token);
                if (await Task.WhenAny(connected, exited) != connected)
                {
                    return Failed(request, $"The elevated worker exited before connecting (code {worker.ExitCode}).");
                }

                await connected;
                if (!NativeMethods.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var clientId) || clientId != worker.Id)
                {
                    return Failed(request, "Another process answered on the elevated worker's pipe; the batch was not sent.");
                }

                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true);
                return await BatchProtocol.RequestAsync(reader, writer, request, progress, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException)
            {
                LogWorkerFailed(ex);
                return Failed(request, $"The elevated worker stopped: {ex.Message}");
            }
        }
    }

    private static BatchResult Failed(BatchRequest request, string reason) =>
        new(request.Items.Select(i => new BatchItemResult(i, Domain.Operations.OperationResult.Failure(reason))).ToList());

    [LoggerMessage(Level = LogLevel.Warning, Message = "Elevated batch failed")]
    private partial void LogWorkerFailed(Exception exception);
}
