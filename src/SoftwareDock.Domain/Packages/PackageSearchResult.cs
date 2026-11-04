namespace SoftwareDock.Domain.Packages;

/// <summary>A package found in a source that can be installed.</summary>
public sealed record PackageSearchResult(
    string Name,
    PackageId Id,
    string? Version,
    string? Source);
