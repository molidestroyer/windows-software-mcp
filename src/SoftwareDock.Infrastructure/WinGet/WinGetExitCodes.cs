using SoftwareDock.Domain.Operations;

namespace SoftwareDock.Infrastructure.WinGet;

/// <summary>Translates WinGet's HRESULT exit codes into results a person can act on.</summary>
internal static class WinGetExitCodes
{
    public const int NoPackageFound = unchecked((int)0x8A150014);
    public const int UpdateNotApplicable = unchecked((int)0x8A15002B);

    // Installer exit codes WinGet passes through unchanged.
    private const int MsiUserCancelled = 1602;
    private const int MsiRebootRequired = 3010;
    private const int MsiRebootInitiated = 1641;

    public static OperationResult ToResult(string verb, string target, int exitCode, string output)
    {
        return exitCode switch
        {
            0 => OperationResult.Success($"{verb} {target}: done."),
            MsiRebootRequired or MsiRebootInitiated => new OperationResult(
                OperationStatus.SucceededRebootRequired, $"{verb} {target}: done. Restart Windows to finish.", exitCode),
            MsiUserCancelled => new OperationResult(OperationStatus.Cancelled, $"{verb} {target}: cancelled.", exitCode),
            NoPackageFound => OperationResult.Failure($"No package matches '{target}'.", exitCode),
            UpdateNotApplicable => OperationResult.Failure($"{target} has no applicable update.", exitCode),
            _ => OperationResult.Failure($"{verb} {target} failed (0x{exitCode:X8}). {LastMeaningfulLine(output)}".TrimEnd(), exitCode),
        };
    }

    /// <summary>WinGet's last line is usually the one that explains the failure.</summary>
    private static string LastMeaningfulLine(string output) =>
        output.Split('\n')
            .Select(l => Processes.ConsoleText.CleanLine(l).Trim())
            .LastOrDefault(l => !Processes.ConsoleText.IsNoise(l)) ?? string.Empty;
}
