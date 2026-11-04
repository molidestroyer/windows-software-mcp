namespace SoftwareDock.Domain.Operations;

/// <summary>How an install, upgrade or uninstall ended.</summary>
public enum OperationStatus
{
    Succeeded,

    /// <summary>Succeeded, but Windows must restart before the change is complete.</summary>
    SucceededRebootRequired,

    /// <summary>The user dismissed a UAC prompt or the installer's own dialog.</summary>
    Cancelled,

    Failed,
}

/// <summary>
/// Result of a software operation. Failures are values, not exceptions: an installer
/// returning 1603 is an expected outcome the UI and the MCP client both need to explain.
/// </summary>
public sealed record OperationResult(OperationStatus Status, string Message, int? ExitCode = null)
{
    public bool IsSuccess => Status is OperationStatus.Succeeded or OperationStatus.SucceededRebootRequired;

    public static OperationResult Success(string message, int? exitCode = 0) =>
        new(OperationStatus.Succeeded, message, exitCode);

    public static OperationResult Failure(string message, int? exitCode = null) =>
        new(OperationStatus.Failed, message, exitCode);
}
