namespace SoftwareDock.Application.Backup;

public enum BackupFormat
{
    /// <summary>Software Dock's own file: packages plus the applications that must be installed by hand.</summary>
    SoftwareDock,

    /// <summary>
    /// <c>winget export</c>'s format, so the file restores a machine with nothing but
    /// <c>winget import -i file.json</c> — no Software Dock needed.
    /// </summary>
    WinGet,
}
