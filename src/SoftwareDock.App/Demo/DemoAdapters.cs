using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SoftwareDock.Application.Abstractions;
using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Leftovers;
using SoftwareDock.Domain.Monitoring;
using SoftwareDock.Domain.Security;
using SoftwareDock.Domain.Startup;
using SoftwareDock.Domain.Usage;
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
        services.TryAddSingleton<ISystemMonitor>(sp => sp.GetRequiredService<DemoMachine>());
        services.TryAddSingleton<IStartupItemSource>(sp => sp.GetRequiredService<DemoMachine>());
        services.TryAddSingleton<IUsageHistorySource>(sp => sp.GetRequiredService<DemoMachine>());
        services.TryAddSingleton<IVulnerabilityFeed>(sp => sp.GetRequiredService<DemoMachine>());
        services.TryAddSingleton<ILeftoverCleaner>(sp => sp.GetRequiredService<DemoMachine>());
        return services;
    }
}

/// <summary>A pretend PC whose state changes as operations run.</summary>
public sealed class DemoMachine : IInstalledApplicationSource, IPackageManager, IUninstallerLauncher, ISystemMonitor, IStartupItemSource, IUsageHistorySource, IVulnerabilityFeed, ILeftoverCleaner
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

    // What "runs" on the demo PC: executable, cores kept busy, working set.
    private static readonly (string App, string Exe, double Cores, long Memory)[] Running =
    [
        ("Google.Chrome", "chrome.exe", 0.9, 1_850L << 20),
        ("Microsoft.VisualStudioCode", "Code.exe", 0.35, 920L << 20),
        ("Docker.DockerDesktop", "Docker Desktop.exe", 0.6, 1_400L << 20),
        ("Spotify.Spotify", "Spotify.exe", 0.08, 310L << 20),
        ("SlackTechnologies.Slack", "slack.exe", 0.05, 480L << 20),
        ("Ollama.Ollama", "ollama.exe", 1.6, 4_200L << 20),
    ];

    // App id → days since last opened, as Explorer's launch history would report.
    private static readonly (string App, string Exe, int DaysAgo, int Runs)[] History =
    [
        ("Google.Chrome", "chrome.exe", 0, 812),
        ("Microsoft.VisualStudioCode", "Code.exe", 0, 430),
        ("Valve.Steam", "steam.exe", 241, 37),
        ("OBSProject.OBSStudio", "obs64.exe", 156, 12),
        ("Zoom.Zoom", "Zoom.exe", 402, 9),
        ("Postman.Postman", "Postman.exe", 120, 51),
        ("Discord.Discord", "Discord.exe", 3, 220),
    ];

    private readonly Lock _lock = new();
    private readonly List<StartupItem> _startup =
    [
        new("Discord", @"""C:\Program Files\Discord.Discord\Update.exe"" --processStart Discord.exe", StartupLocation.UserRun, @"C:\Program Files\Discord.Discord\Update.exe", true),
        new("Spotify", @"""C:\Program Files\Spotify.Spotify\Spotify.exe"" /minimized", StartupLocation.UserRun, @"C:\Program Files\Spotify.Spotify\Spotify.exe", true),
        new("Steam", @"""C:\Program Files\Valve.Steam\steam.exe"" -silent", StartupLocation.UserRun, @"C:\Program Files\Valve.Steam\steam.exe", true),
        new("Docker Desktop", @"C:\Program Files\Docker.DockerDesktop\Docker Desktop.exe -Autostart", StartupLocation.UserRun, @"C:\Program Files\Docker.DockerDesktop\Docker Desktop.exe", false),
        new("Ollama.lnk", @"C:\Users\demo\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\Ollama.lnk", StartupLocation.UserStartupFolder, @"C:\Program Files\Ollama.Ollama\ollama app.exe", true),
        new("SecurityHealth", @"%windir%\system32\SecurityHealthSystray.exe", StartupLocation.MachineRun, @"C:\Windows\system32\SecurityHealthSystray.exe", true),
        new("Contoso Reporting Agent", @"""C:\Program Files\ARP\Machine\X86\{6F320B93-EE3C-4826-85E0-ADF79F8D4C61}\agent.exe""", StartupLocation.MachineRun32, @"C:\Program Files\ARP\Machine\X86\{6F320B93-EE3C-4826-85E0-ADF79F8D4C61}\agent.exe", true),
    ];

    private readonly System.Diagnostics.Stopwatch _uptime = System.Diagnostics.Stopwatch.StartNew();
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
                InstallLocation = InstallFolder(p.Id.Value),
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

    public int ProcessorCount => 8;

    public IReadOnlyList<ProcessSample> GetProcesses()
    {
        // CPU time grows with wall-clock time, so the reported % does not depend on how often it is sampled.
        var seconds = _uptime.Elapsed.TotalSeconds;
        lock (_lock)
        {
            var installed = _installed.Select(p => p.Id.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return Running
                .Where(r => installed.Contains(r.App))
                .Select((r, i) => new ProcessSample(1000 + i, $@"{InstallFolder(r.App)}\{r.Exe}", TimeSpan.FromSeconds(r.Cores * seconds), r.Memory))
                .Append(new ProcessSample(4000, @"C:\Windows\explorer.exe", TimeSpan.FromSeconds(0.4 * seconds), 180L << 20))
                .ToList();
        }
    }

    public MemoryStatus GetMemoryStatus() => new(32L << 30, 17L << 30);

    public Task<IReadOnlyList<StartupItem>> ListAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<StartupItem>>(_startup.ToList());
        }
    }

    public Task<OperationResult> SetEnabledAsync(StartupItem item, bool enabled, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.IsPerUser)
        {
            return Task.FromResult(OperationResult.Failure($"{item.Name} starts for every user; changing it needs administrator rights."));
        }

        lock (_lock)
        {
            var index = _startup.FindIndex(s => s.Id == item.Id);
            _startup[index] = _startup[index] with { IsEnabled = enabled };
        }

        return Task.FromResult(OperationResult.Success($"{item.Name} will {(enabled ? string.Empty : "no longer ")}start with Windows."));
    }

    public IReadOnlyList<UsageRecord> GetUsage() =>
        History.Select(h => new UsageRecord($@"{InstallFolder(h.App)}\{h.Exe}", DateTimeOffset.UtcNow.AddDays(-h.DaysAgo), h.Runs)).ToList();

    public Task<IReadOnlyList<KnownExploitedVulnerability>> GetAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return Task.FromResult<IReadOnlyList<KnownExploitedVulnerability>>(
        [
            new("CVE-2026-31337", "Notepad++", "Notepad++", "Notepad++ Plugin Loader Code Execution", today.AddDays(-12), false),
            new("CVE-2026-29001", "Docker", "Desktop", "Docker Desktop Container Escape", today.AddDays(-30), true),
            new("CVE-2025-1001", "Microsoft", "Windows", "Windows Win32k Privilege Escalation", today.AddDays(-20), false),
        ]);
    }

    public Task<IReadOnlyList<LeftoverFolder>> FindAsync(InstalledApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        var folders = new KnownFolders(@"C:\Program Files", @"C:\Program Files (x86)", @"C:\ProgramData", @"C:\Users\demo\AppData\Roaming", @"C:\Users\demo\AppData\Local");

        // Pretend the uninstaller forgot the user's data folders, as most do.
        return Task.FromResult<IReadOnlyList<LeftoverFolder>>(LeftoverCandidates.For(application, folders)
            .Where(p => p.Contains("AppData", StringComparison.Ordinal) && !p.Contains(@"\Programs\", StringComparison.Ordinal))
            .Take(2)
            .Select((p, i) => new LeftoverFolder(p, (i + 1) * 87L << 20))
            .ToList());
    }

    public Task<OperationResult> RecycleAsync(IReadOnlyList<LeftoverFolder> folders, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult.Success($"Moved {folders.Count} folders to the Recycle Bin."));

    private static string InstallFolder(string id) => $@"C:\Program Files\{id}";

    private async Task SimulateAsync(IProgress<string>? progress, CancellationToken cancellationToken, params string[] steps)
    {
        foreach (var step in steps)
        {
            progress?.Report(step);
            await Task.Delay(_stepDelay, cancellationToken);
        }
    }
}
