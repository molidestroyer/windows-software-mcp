using SoftwareDock.Domain.Applications;

namespace SoftwareDock.Domain.Packages;

/// <summary>What to install. Everything but the id is optional and left to the package manager by default.</summary>
public sealed record InstallRequest(
    PackageId Id,
    string? Version = null,
    string? Source = null,
    InstallScope Scope = InstallScope.Unknown);
