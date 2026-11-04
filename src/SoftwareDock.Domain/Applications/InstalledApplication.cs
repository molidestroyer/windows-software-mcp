using SoftwareDock.Domain.Packages;

namespace SoftwareDock.Domain.Applications;

/// <summary>
/// An application installed on this machine, merged from every source that knows about it.
/// </summary>
public sealed record InstalledApplication
{
    public required ApplicationKey Key { get; init; }

    public required string Name { get; init; }

    public required ApplicationOrigin Origin { get; init; }

    public string? Publisher { get; init; }

    public string? Version { get; init; }

    public InstallScope Scope { get; init; }

    public string? InstallLocation { get; init; }

    public DateOnly? InstallDate { get; init; }

    public long? EstimatedSizeBytes { get; init; }

    /// <summary><c>DisplayIcon</c> as registered: a path, optionally followed by <c>,index</c>.</summary>
    public string? IconPath { get; init; }

    public UninstallCommand? Uninstall { get; init; }

    public UninstallCommand? QuietUninstall { get; init; }

    /// <summary>The package manager's id for this application, when it knows it.</summary>
    public PackageId? PackageId { get; init; }

    /// <summary>The source able to update it; <see langword="null"/> means "local only".</summary>
    public string? PackageSource { get; init; }

    public string? AvailableVersion { get; init; }

    public bool HasUpdate => !string.IsNullOrEmpty(AvailableVersion);

    /// <summary>A package manager can act on it: it has an id and the id was not cut short.</summary>
    public bool IsManagedByPackageManager => PackageId is { IsTruncated: false };

    public bool CanUninstall => IsManagedByPackageManager || Uninstall is not null || QuietUninstall is not null;

    /// <summary>Upgrading needs a source to pull the new version from.</summary>
    public bool CanUpgrade => HasUpdate && IsManagedByPackageManager && PackageSource is not null;

    /// <summary>Case-insensitive match against the fields a person would type.</summary>
    public bool Matches(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var term = text.Trim();
        return Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)
            || (Publisher?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false)
            || (PackageId?.Value.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
