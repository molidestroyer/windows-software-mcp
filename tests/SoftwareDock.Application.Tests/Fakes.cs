using SoftwareDock.Application.Abstractions;
using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Operations;
using SoftwareDock.Domain.Packages;

namespace SoftwareDock.Application.Tests;

internal sealed class FakeRegistry : IInstalledApplicationSource
{
    public List<InstalledApplication> Entries { get; } = [];

    public Exception? Throw { get; set; }

    public int Reads { get; private set; }

    public Task<IReadOnlyList<InstalledApplication>> GetInstalledApplicationsAsync(CancellationToken cancellationToken)
    {
        Reads++;
        return Throw is null
            ? Task.FromResult<IReadOnlyList<InstalledApplication>>(Entries.ToList())
            : Task.FromException<IReadOnlyList<InstalledApplication>>(Throw);
    }
}

internal sealed class FakePackageManager : IPackageManager
{
    public List<InstalledPackage> Installed { get; } = [];

    public Exception? Throw { get; set; }

    public OperationResult NextResult { get; set; } = OperationResult.Success("ok");

    public List<string> Calls { get; } = [];

    public Task<IReadOnlyList<InstalledPackage>> ListInstalledAsync(CancellationToken cancellationToken) =>
        Throw is null
            ? Task.FromResult<IReadOnlyList<InstalledPackage>>(Installed.ToList())
            : Task.FromException<IReadOnlyList<InstalledPackage>>(Throw);

    public Task<IReadOnlyList<PackageSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PackageSearchResult>>([]);

    public Task<OperationResult> InstallAsync(InstallRequest request, IProgress<string>? progress, CancellationToken cancellationToken) =>
        Record($"install {request.Id}");

    public Task<OperationResult> UpgradeAsync(PackageId id, IProgress<string>? progress, CancellationToken cancellationToken) =>
        Record($"upgrade {id}");

    public Task<OperationResult> UninstallAsync(PackageId id, bool silent, IProgress<string>? progress, CancellationToken cancellationToken) =>
        Record($"uninstall {id} silent={silent}");

    private Task<OperationResult> Record(string call)
    {
        Calls.Add(call);
        return Task.FromResult(NextResult);
    }
}

internal sealed class FakeUninstaller : IUninstallerLauncher
{
    public List<(UninstallCommand Command, bool Silent)> Runs { get; } = [];

    public Task<OperationResult> RunAsync(UninstallCommand command, bool silent, CancellationToken cancellationToken)
    {
        Runs.Add((command, silent));
        return Task.FromResult(OperationResult.Success("ran"));
    }
}

internal sealed class FakeProcesses : ISystemMonitor
{
    public List<string> ImagePaths { get; } = [];

    public int ProcessorCount => 1;

    public IReadOnlyList<Domain.Monitoring.ProcessSample> GetProcesses() =>
        ImagePaths.Select((p, i) => new Domain.Monitoring.ProcessSample(i + 1, p, TimeSpan.Zero, 0)).ToList();

    public Domain.Monitoring.MemoryStatus GetMemoryStatus() => new(1, 1);
}

/// <summary><see cref="Progress{T}"/> posts asynchronously; tests need the reports in order, now.</summary>
internal sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

internal static class Apps
{
    public static InstalledApplication Registry(string subKey, string name, string? uninstall = null, string? quiet = null, string? version = "1.0")
    {
        UninstallCommand.TryParse(uninstall, out var u);
        UninstallCommand.TryParse(quiet, out var q);
        return new InstalledApplication
        {
            Key = ApplicationKey.ForRegistry(InstallScope.Machine, RegistryView.Native64, subKey),
            Name = name,
            Version = version,
            Origin = ApplicationOrigin.Registry,
            Scope = InstallScope.Machine,
            Uninstall = u,
            QuietUninstall = q,
        };
    }
}
