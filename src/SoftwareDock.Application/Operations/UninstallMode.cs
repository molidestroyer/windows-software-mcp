namespace SoftwareDock.Application.Operations;

public enum UninstallMode
{
    /// <summary>Let the uninstaller show its UI. The safe default for a person at the keyboard.</summary>
    Interactive,

    /// <summary>
    /// No UI. Only used when a silent path is known to exist (package manager, MSI,
    /// <c>QuietUninstallString</c>); otherwise the operation falls back to interactive.
    /// </summary>
    Silent,
}
