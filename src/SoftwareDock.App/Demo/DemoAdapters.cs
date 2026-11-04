using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SoftwareDock.Application.Abstractions;
using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Operations;
using SoftwareDock.Domain.Packages;

namespace SoftwareDock.App.Demo;

/// <summary>
/// In-memory adapters for <c>--demo</c>: the dock runs end to end (inventory, search, install,
/// upgrade, uninstall) without touching the machine. Also what renders the screenshots on CI.
/// </summary>
public static class DemoAdapters
{
    public static IServiceCollection AddSoftwareDockDemoAdapters(this IServiceCollection services)
    {
        services.TryAddSingleton<DemoMachine>();
        services.TryAddSingleton<IInstalledApplicationSource>(sp => sp.GetRequiredService<DemoMachine>());
        services.TryAddSingleton<IPackageManager>(sp => sp.GetRequiredService<DemoMachine>());
        services.TryAddSingleton<IUninstallerLauncher>(sp => sp.GetRequiredService<DemoMachine>());
        return services;
    }
}

/// <summary>A pretend PC whose state changes as operations run.</summary>
public sealed class DemoMachine : IInstalledApplicationSource, IPackageManager, IUninstallerLauncher
{
    private static readonly (string Name, string Id, string Publisher, string Version, string? Available, string? Source)[] Seed =
    [
        ("7-Zip 24.08 (x64)", "7zip.7zip", "Igor Pavlov", "24.08", null, "winget"),
        ("Discord", "Discord.Discord", "Discord Inc.", "1.0.9163", null, "winget"),
        ("Docker Desktop", "Docker.DockerDesktop", "Docker Inc.", "4.36.0", "4.38.0", "winget"),
        ("Git", "Git.Git", "The Git Development Community", "2.45.0", "2.47.1", "winget"),
        ("Google Chrome", "Google.Chrome", "Google LLC", "131.0.6778.86", null, "winget"),
        ("Microsoft PowerToys", "Microsoft.PowerToys", "Microsoft Corporation", "0.99.1", "0.100.1", "winget"),
        ("Microsoft Visual Studio Code", "Microsoft.VisualStudioCode", "Microsoft Corporation", "1.95.3", "1.96.0", "winget"),
        ("Mozilla Firefox (x64 en-US)", "Mozilla.Firefox", "Mozilla", "133.0", null, "winget"),
        ("Node.js", "OpenJS.NodeJS.LTS", "Node.js Foundation", "22.11.0", null, "winget"),
        ("Notepad++ (64-bit x64)", "Notepad++.Notepad++", "Notepad++ Team", "8.7.1", "8.7.4", "winget"),
        ("OBS Studio", "OBSProject.OBSStudio", "OBS Project", "30.2.3", null, "winget"),
        ("Ollama", "Ollama.Ollama", "Ollama", "0.5.1", null, "winget"),
        ("Postman x86_64 11.20.0", "Postman.Postman", "Postman", "11.20.0", null, "winget"),
        ("Python 3.12.7 (64-bit)", "Python.Python.3.12", "Python Software Foundation", "3.12.7", "3.12.8", "winget"),
        ("Slack", "SlackTechnologies.Slack", "Slack Technologies Inc.", "4.41.105", null, "winget"),
        ("Spotify", "Spotify.Spotify", "Spotify AB", "1.2.50.335", null, "winget"),
        ("Steam", "Valve.Steam", "Valve Corporation", "2.10.91.91", null, "winget"),
        ("VLC media player", "VideoLAN.VLC", "VideoLAN", "3.0.21", null, "winget"),
        ("Windows Terminal", "Microsoft.WindowsTerminal", "Microsoft Corporation", "1.21.3231.0", null, "winget"),
        ("Zoom Workplace", "Zoom.Zoom", "Zoom Video Communications, Inc.", "6.2.11", null, "winget"),
        ("Contoso Legacy Reporting", @"ARP\Machine\X86\{6F320B93-EE3C-4826-85E0-ADF79F8D4C61}", "Contoso Ltd.", "3.1.0", null, null),
    ];

    private static readonly PackageSearchResult[] Catalog =
    [
        new("Visual Studio Code", PackageId.Parse("Microsoft.VisualStudioCode"), "1.96.0", "winget"),
        new("Visual Studio Code Insiders", PackageId.Parse("Microsoft.VisualStudioCode.Insiders"), "1.97.0", "winget"),
        new("VLC media player", PackageId.Parse("VideoLAN.VLC"), "3.0.21", "winget"),
        new("Brave", PackageId.Parse("Brave.Brave"), "1.73.97", "winget"),
        new("Bitwarden", PackageId.Parse("Bitwarden.Bitwarden"), "2024.12.0", "winget"),
        new("JetBrains Rider", PackageId.Parse("JetBrains.Rider"), "2024.3.2", "winget"),
        new("Obsidian", PackageId.Parse("Obsidian.Obsidian"), "1.7.7", "winget"),
        new("WinSCP", PackageId.Parse("WinSCP.WinSCP"), "6.3.6", "winget"),
        new("Visual Studio Community 2022", PackageId.Parse("Microsoft.VisualStudio.2022.Community"), "17.12.3", "winget"),
        new("Microsoft PowerToys", PackageId.Parse("Microsoft.PowerToys"), "0.100.1", "winget"),
        new("Everything", PackageId.Parse("voidtools.Everything"), "1.4.1.1026", "winget"),
        new("ShareX", PackageId.Parse("ShareX.ShareX"), "17.0.0", "winget"),
    ];

    private readonly Lock _lock = new();
    private readonly List<InstalledPackage> _installed;
    private readonly TimeSpan _stepDelay;

    public DemoMachine()
        : this(TimeSpan.FromMilliseconds(350))
    {
    }

    public DemoMachine(TimeSpan stepDelay)
    {
        _stepDelay = stepDelay;
        _installed = Seed.Select(s => new InstalledPackage(s.Name, PackageId.Parse(s.Id), s.Version, s.Available, s.Source)).ToList();
    }

    public Task<IReadOnlyList<InstalledApplication>> GetInstalledApplicationsAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            // Registry entries for everything; WinGet then matches them by name, as on a real PC.
            IReadOnlyList<InstalledApplication> entries = _installed.Select(p => new InstalledApplication
            {
                Key = p.Id.Value.StartsWith(@"ARP\", StringComparison.OrdinalIgnoreCase)
                    ? ApplicationKey.Parse(p.Id.Value)
                    : ApplicationKey.ForRegistry(InstallScope.Machine, RegistryView.Native64, p.Id.Value + "_is1"),
                Name = p.Name,
                Origin = ApplicationOrigin.Registry,
                Scope = InstallScope.Machine,
                Version = p.Version,
                Publisher = Seed.FirstOrDefault(s => s.Id == p.Id.Value).Publisher ?? "Demo publisher",
                EstimatedSizeBytes = 1024L * 1024 * (40 + (p.Name.Length * 13 % 400)),
                InstallDate = new DateOnly(2026, 1 + (p.Name.Length % 10), 1 + (p.Name.Length % 27)),
            }).ToList();
            return Task.FromResult(entries);
        }
    }

    public Task<IReadOnlyList<InstalledPackage>> ListInstalledAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<InstalledPackage>>(_installed.ToList());
        }
    }

    public async Task<IReadOnlyList<PackageSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        await Task.Delay(_stepDelay, cancellationToken);
        return Catalog
            .Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || p.Id.Value.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(maxResults)
            .ToList();
    }

    public async Task<OperationResult> InstallAsync(InstallRequest request, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var package = Catalog.FirstOrDefault(p => p.Id == request.Id);
        if (package is null)
        {
            return OperationResult.Failure($"No package matches '{request.Id}'.", unchecked((int)0x8A150014));
        }

        await SimulateAsync(progress, cancellationToken, $"Found {package.Name} [{package.Id}] Version {package.Version}", "Downloading installer…", "Starting package install…");
        lock (_lock)
        {
            _installed.RemoveAll(p => p.Id == request.Id);
            _installed.Add(new InstalledPackage(package.Name, package.Id, package.Version, null, package.Source));
        }

        return OperationResult.Success($"Install {package.Id}: done.");
    }

    public async Task<OperationResult> UpgradeAsync(PackageId id, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        await SimulateAsync(progress, cancellationToken, "Downloading installer…", "Installing update…");
        lock (_lock)
        {
            var index = _installed.FindIndex(p => p.Id == id);
            if (index < 0 || _installed[index].AvailableVersion is null)
            {
                return OperationResult.Failure($"{id} has no applicable update.", unchecked((int)0x8A15002B));
            }

            _installed[index] = _installed[index] with { Version = _installed[index].AvailableVersion, AvailableVersion = null };
        }

        return OperationResult.Success($"Upgrade {id}: done.");
    }

    public async Task<OperationResult> UninstallAsync(PackageId id, bool silent, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        await SimulateAsync(progress, cancellationToken, "Starting package uninstall…");
        lock (_lock)
        {
            _installed.RemoveAll(p => p.Id == id);
        }

        return OperationResult.Success($"Uninstall {id}: done.");
    }

    public Task<OperationResult> RunAsync(UninstallCommand command, bool silent, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult.Success("The uninstaller finished."));

    private async Task SimulateAsync(IProgress<string>? progress, CancellationToken cancellationToken, params string[] steps)
    {
        foreach (var step in steps)
        {
            progress?.Report(step);
            await Task.Delay(_stepDelay, cancellationToken);
        }
    }
}
