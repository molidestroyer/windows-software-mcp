using System.Runtime.Versioning;
using Microsoft.Win32;

namespace SoftwareDock.App.Platform;

/// <summary>"Start with Windows".</summary>
public interface IStartupRegistration
{
    bool IsSupported { get; }

    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}

/// <summary>
/// Per-user <c>Run</c> key: no elevation, nothing to clean up machine-wide, and the user sees it
/// in Task Manager › Startup apps like any other entry.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RunKeyStartupRegistration : IStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SoftwareDock";

    public bool IsSupported => Environment.ProcessPath is not null;

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && value.Contains(Environment.ProcessPath ?? "\0", StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --background");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}

public sealed class UnsupportedStartupRegistration : IStartupRegistration
{
    public bool IsSupported => false;

    public bool IsEnabled => false;

    public void SetEnabled(bool enabled)
    {
    }
}
