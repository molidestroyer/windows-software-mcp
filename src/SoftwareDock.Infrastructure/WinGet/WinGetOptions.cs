namespace SoftwareDock.Infrastructure.WinGet;

public sealed class WinGetOptions
{
    public const string SectionName = "WinGet";

    /// <summary>Resolved through <c>PATH</c>; WinGet installs an execution alias under <c>%LOCALAPPDATA%\Microsoft\WindowsApps</c>.</summary>
    public string ExecutablePath { get; set; } = "winget.exe";

    /// <summary>Upper bound for <c>list</c> and <c>search</c>. The first run after boot refreshes the source index and is slow.</summary>
    public TimeSpan QueryTimeout { get; set; } = TimeSpan.FromMinutes(2);
}
