namespace SoftwareDock.Domain.Applications;

/// <summary>Who an application was installed for.</summary>
public enum InstallScope
{
    Unknown = 0,

    /// <summary>All users — <c>HKEY_LOCAL_MACHINE</c>. Removing it usually needs elevation.</summary>
    Machine,

    /// <summary>The current user only — <c>HKEY_CURRENT_USER</c>.</summary>
    User,
}
