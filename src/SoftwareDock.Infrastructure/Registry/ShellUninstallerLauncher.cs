using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SoftwareDock.Application.Abstractions;
using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Operations;

namespace SoftwareDock.Infrastructure.Registry;

/// <summary>Starts an application's registered uninstaller and waits for it.</summary>
/// <remarks>
/// Started through the shell (<c>UseShellExecute</c>) so an uninstaller whose manifest requires
/// elevation gets its UAC prompt; a plain <c>CreateProcess</c> would fail with error 740.
/// Arguments are passed verbatim: they were written by the application's own installer.
/// <para>
/// Known limit: some uninstallers (Inno Setup, several NSIS ones) copy themselves to %TEMP%,
/// relaunch and exit at once. The wait then ends before the removal does, and the next
/// inventory refresh may still list the application.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed partial class ShellUninstallerLauncher(ILogger<ShellUninstallerLauncher> logger) : IUninstallerLauncher
{
    private const int ErrorCancelled = 1223;
    private const int MsiUserCancelled = 1602;
    private const int MsiFatalError = 1603;
    private const int MsiAdminRequired = 1730;
    private const int MsiRebootRequired = 3010;
    private const int MsiRebootInitiated = 1641;

    public async Task<OperationResult> RunAsync(UninstallCommand command, bool silent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (fileName, arguments) = command.ToProcessCommand(silent);

        Process? process;
        try
        {
            LogStarting(fileName, arguments);
            process = Process.Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = true });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return new OperationResult(OperationStatus.Cancelled, "The administrator prompt was dismissed.", ErrorCancelled);
        }
        catch (Win32Exception ex)
        {
            return OperationResult.Failure($"Could not start the uninstaller '{fileName}': {ex.Message}", ex.NativeErrorCode);
        }

        if (process is null)
        {
            return OperationResult.Success("The uninstaller was handed to the shell.", exitCode: null);
        }

        using (process)
        {
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode switch
            {
                0 => OperationResult.Success("The uninstaller finished."),
                MsiRebootRequired or MsiRebootInitiated => new OperationResult(
                    OperationStatus.SucceededRebootRequired, "Uninstalled. Restart Windows to finish.", process.ExitCode),
                MsiUserCancelled => new OperationResult(OperationStatus.Cancelled, "The uninstaller was cancelled.", process.ExitCode),
                MsiAdminRequired => new OperationResult(OperationStatus.Cancelled, "Administrator approval was not given: the Windows prompt was declined or timed out.", process.ExitCode),

                // msiexec folds a declined or timed-out UAC prompt into 1603 (QA, 0.3.0); say what most likely happened.
                MsiFatalError when command.IsWindowsInstaller => OperationResult.Failure(
                    "The uninstaller failed (1603). If a Windows administrator prompt appeared, it was declined or timed out; try again and approve it.",
                    process.ExitCode),
                var code => OperationResult.Failure($"The uninstaller exited with code {code}.", code),
            };
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting uninstaller {FileName} {Arguments}")]
    private partial void LogStarting(string fileName, string arguments);
}
