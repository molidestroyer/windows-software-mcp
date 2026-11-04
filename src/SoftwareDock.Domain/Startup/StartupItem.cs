namespace SoftwareDock.Domain.Startup;

/// <summary>Where Windows found an entry that starts at sign-in.</summary>
public enum StartupLocation
{
    /// <summary><c>HKCU\…\Run</c>: this user only; changing it needs no elevation.</summary>
    UserRun,

    /// <summary><c>HKLM\…\Run</c> (64-bit view): every user; changing it needs elevation.</summary>
    MachineRun,

    /// <summary><c>HKLM\…\Run</c> as seen by 32-bit programs (<c>WOW6432Node</c>).</summary>
    MachineRun32,

    /// <summary>The user's Startup folder.</summary>
    UserStartupFolder,

    /// <summary>The all-users Startup folder.</summary>
    CommonStartupFolder,
}

/// <summary>A program Windows starts at sign-in, as Task Manager › Startup apps lists it.</summary>
/// <param name="Name">Registry value name or shortcut file name.</param>
/// <param name="Command">The command line, or the shortcut path for Startup-folder entries.</param>
/// <param name="Location">Where Windows keeps it, which decides who may change it.</param>
/// <param name="ExecutablePath">The program it starts, when it can be told.</param>
/// <param name="IsEnabled">Task Manager's enabled/disabled state (<see cref="StartupApproval"/>).</param>
public sealed record StartupItem(string Name, string Command, StartupLocation Location, string? ExecutablePath, bool IsEnabled)
{
    /// <summary>Stable identity: location + name, which is exactly what Windows keys approvals on.</summary>
    public string Id => $"{Location}:{Name}";

    public bool IsPerUser => Location is StartupLocation.UserRun or StartupLocation.UserStartupFolder;
}
