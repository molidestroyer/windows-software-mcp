namespace SoftwareDock.Infrastructure.Processes;

/// <param name="FileName">Executable to start.</param>
/// <param name="Arguments">
/// Passed through <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/>, one element per
/// argument, so values coming from a user or a model are never re-parsed as a command line.
/// </param>
/// <param name="Timeout">Kills the process tree when exceeded; <see langword="null"/> waits for as long as it takes.</param>
public sealed record ProcessRequest(string FileName, IReadOnlyList<string> Arguments, TimeSpan? Timeout = null);

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Runs a console process with redirected output. Seam for testing the WinGet adapter.</summary>
public interface IProcessRunner
{
    /// <param name="request">What to run.</param>
    /// <param name="outputLines">Receives each meaningful output line as it is written.</param>
    /// <param name="cancellationToken">Cancelling kills the whole process tree.</param>
    /// <exception cref="System.ComponentModel.Win32Exception">The executable could not be started.</exception>
    /// <exception cref="TimeoutException">The process ran longer than <see cref="ProcessRequest.Timeout"/>.</exception>
    Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<string>? outputLines, CancellationToken cancellationToken);
}
