namespace SoftwareDock.Domain.Applications;

/// <summary>Where the inventory learned about an application.</summary>
public enum ApplicationOrigin
{
    /// <summary>An "Apps &amp; features" (ARP) entry in the registry. Richest metadata: publisher, size, icon.</summary>
    Registry,

    /// <summary>A packaged (MSIX / Store) app, which has no ARP entry and is only visible to the package manager.</summary>
    Packaged,

    /// <summary>Reported by the package manager only, with no matching registry entry.</summary>
    PackageManager,
}
