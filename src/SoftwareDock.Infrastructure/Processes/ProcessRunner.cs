using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SoftwareDock.Infrastructure.Processes;

public sealed partial class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<string>? outputLines, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = new ProcessStartInfo(request.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var timeout = request.Timeout is { } limit ? new CancellationTokenSource(limit) : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        using var process = new Process { StartInfo = startInfo };
        LogStarting(request.FileName, string.Join(' ', request.Arguments));
        process.Start();

        // Nothing we run is ever meant to read input; closing stdin turns a forgotten prompt into an exit instead of a hang.
        process.StandardInput.Close();

        var stdout = PumpAsync(process.StandardOutput, outputLines, linked.Token);
        var stderr = PumpAsync(process.StandardError, null, linked.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
            return new ProcessResult(process.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            if (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"'{request.FileName}' did not finish within {request.Timeout}.");
            }

            throw;
        }
    }

    private static async Task<string> PumpAsync(StreamReader reader, IProgress<string>? lines, CancellationToken cancellationToken)
    {
        var buffer = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken) is { } raw)
        {
            buffer.Append(raw).Append('\n');
            if (lines is not null)
            {
                var line = ConsoleText.CleanLine(raw);
                if (!ConsoleText.IsNoise(line))
                {
                    lines.Report(line.Trim());
                }
            }
        }

        return buffer.ToString();
    }

    private void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException ex)
        {
            LogKillFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Running {FileName} {Arguments}")]
    private partial void LogStarting(string fileName, string arguments);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The process had already exited when it was cancelled")]
    private partial void LogKillFailed(Exception exception);
}
