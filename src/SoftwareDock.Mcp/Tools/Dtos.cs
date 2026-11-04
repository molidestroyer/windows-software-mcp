using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Operations;
using SoftwareDock.Domain.Packages;

namespace SoftwareDock.Mcp.Tools;

/// <summary>What an agent sees of an installed application. Flat, stable, JSON-friendly.</summary>
public sealed record InstalledSoftwareDto(
    string Key,
    string Name,
    string? Publisher,
    string? Version,
    string? AvailableVersion,
    string? PackageId,
    string Scope,
    bool CanUpgrade,
    bool CanUninstall)
{
    public static InstalledSoftwareDto From(InstalledApplication app) => new(
        app.Key.Value,
        app.Name,
        app.Publisher,
        app.Version,
        app.AvailableVersion,
        app.PackageId?.Value,
        app.Scope.ToString(),
        app.CanUpgrade,
        app.CanUninstall);
}

public sealed record SoftwareDetailsDto(
    InstalledSoftwareDto Summary,
    string Origin,
    string? PackageSource,
    string? InstallLocation,
    DateOnly? InstallDate,
    long? EstimatedSizeBytes,
    string? UninstallMethod)
{
    public static SoftwareDetailsDto From(InstalledApplication app) => new(
        InstalledSoftwareDto.From(app),
        app.Origin.ToString(),
        app.PackageSource,
        app.InstallLocation,
        app.InstallDate,
        app.EstimatedSizeBytes,
        app.IsManagedByPackageManager ? "winget"
            : app.QuietUninstall is not null || app.Uninstall?.IsWindowsInstaller == true ? "registered uninstaller (can run silently)"
            : app.Uninstall is not null ? "registered uninstaller (interactive)"
            : null);
}

public sealed record InventoryPageDto(
    int Total,
    int Returned,
    int UpdatesAvailable,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<InstalledSoftwareDto> Items);

public sealed record CatalogPackageDto(string PackageId, string Name, string? Version, string? Source)
{
    public static CatalogPackageDto From(PackageSearchResult result) =>
        new(result.Id.Value, result.Name, result.Version, result.Source);
}

public sealed record OperationResultDto(string Status, bool Succeeded, string Message, int? ExitCode)
{
    public static OperationResultDto From(OperationResult result) =>
        new(result.Status.ToString(), result.IsSuccess, result.Message, result.ExitCode);
}
