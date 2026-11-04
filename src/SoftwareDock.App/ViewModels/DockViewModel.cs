using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoftwareDock.App.Platform;
using SoftwareDock.App.Services;
using SoftwareDock.App.Settings;
using SoftwareDock.App.Shell;
using SoftwareDock.Application.Abstractions;
using SoftwareDock.Application.Backup;
using SoftwareDock.Application.Insights;
using SoftwareDock.Application.Inventory;
using SoftwareDock.Application.Monitoring;
using SoftwareDock.Application.Operations;
using SoftwareDock.Application.Startup;
using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Backup;
using SoftwareDock.Domain.Operations;
using SoftwareDock.Domain.Packages;
using SoftwareDock.Domain.Security;
using SoftwareDock.Domain.Updates;
using SoftwareDock.Domain.Usage;

namespace SoftwareDock.App.ViewModels;

/// <summary>State and behaviour of the dock panel. Knows nothing about windows or the tray.</summary>
public sealed partial class DockViewModel : ObservableObject, IDisposable
{
    private const int MinimumQueryLength = 2;
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan MonitorInterval = TimeSpan.FromSeconds(2);

    private readonly ISoftwareInventory _inventory;
    private readonly ISoftwareOperations _operations;
    private readonly BatchRunner _batches;
    private readonly IPackageManager _catalog;
    private readonly IIconProvider _icons;
    private readonly IUserSettingsStore _settings;
    private readonly ResourceMonitor _monitor;
    private readonly SoftwareBackup _backup;
    private readonly IAppUpdater _updater;
    private readonly SoftwareInsights _insights;
    private readonly ILeftoverCleaner _leftovers;
    private readonly TimeProvider _time;
    private readonly ILogger<DockViewModel> _logger;
    private readonly Dictionary<ApplicationKey, ApplicationItemViewModel> _items = [];

    private InventorySnapshot? _snapshot;
    private ResourceSnapshot _usage = ResourceSnapshot.Empty;
    private IReadOnlyDictionary<ApplicationKey, SecurityAdvisory> _advisories = new Dictionary<ApplicationKey, SecurityAdvisory>();
    private IReadOnlyDictionary<ApplicationKey, ApplicationUsage> _lastUsed = new Dictionary<ApplicationKey, ApplicationUsage>();
    private IReadOnlyDictionary<ApplicationKey, ApplicationUsage> _unused = new Dictionary<ApplicationKey, ApplicationUsage>();
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _monitorCancellation;
    private AppUpdate? _pendingAppUpdate;
    private int _runningOperations;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstalledSection), nameof(IsUpdatesSection), nameof(IsDiscoverSection), nameof(IsStartupSection), nameof(IsAppListSection), nameof(IsEmpty), nameof(EmptyMessage), nameof(SearchWatermark), nameof(ShowUpdateAll), nameof(ShowListToolbar), nameof(ShowSearch))]
    private DockSection _selectedSection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMainPage), nameof(IsSettingsPage), nameof(IsRestorePage))]
    private DockPage _page;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(IsActivityVisible))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(IsActivityVisible))]
    private bool _isSearching;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActivityVisible))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _statusIsError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int _installedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(HasUpdates), nameof(ShowUpdateAll), nameof(UpdateBanner))]
    private int _updateCount;

    [ObservableProperty]
    private bool _isPinned = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBulkBar))]
    private bool _isSelecting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBulkBar), nameof(BulkLabel), nameof(BulkConfirmLabel))]
    private int _selectedCount;

    [ObservableProperty]
    private bool _isConfirmingBulk;

    [ObservableProperty]
    private SortOption _selectedSort;

    [ObservableProperty]
    private bool _hasResourceData;

    [ObservableProperty]
    private RestoreViewModel? _restore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAppUpdate))]
    private string? _appUpdateText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLeftoverOffer))]
    private LeftoverOfferViewModel? _leftoverOffer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSecurityUpdates), nameof(UpdateBanner))]
    private int _securityUpdateCount;

    public DockViewModel(
        ISoftwareInventory inventory,
        ISoftwareOperations operations,
        BatchRunner batches,
        IPackageManager catalog,
        IIconProvider icons,
        IUserSettingsStore settings,
        ResourceMonitor monitor,
        SoftwareBackup backup,
        IAppUpdater updater,
        SoftwareInsights insights,
        ILeftoverCleaner leftovers,
        StartupManager startup,
        TimeProvider time,
        SettingsViewModel settingsPage,
        IOptions<DockOptions> options,
        ILogger<DockViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settingsPage);
        _inventory = inventory;
        _operations = operations;
        _batches = batches;
        _catalog = catalog;
        _icons = icons;
        _settings = settings;
        _monitor = monitor;
        _backup = backup;
        _updater = updater;
        _insights = insights;
        _leftovers = leftovers;
        _time = time;
        _logger = logger;
        SettingsPage = settingsPage;
        StartupPage = new StartupPageViewModel(startup, SetStatus);
        HotkeyText = HotkeyGesture.TryParse(options.Value.Hotkey, out var gesture) ? gesture!.Display : null;
        _selectedSort = SortOption.All.First(o => o.Order == settings.Current.SortOrder);

        Applications.CollectionChanged += (_, _) => OnListChanged();
        CatalogResults.CollectionChanged += (_, _) => OnListChanged();
        settingsPage.RestoreRequested += async (_, manifest) => await OpenRestoreAsync(manifest);
        settingsPage.AppUpdateReady += (_, update) => OfferAppUpdate(update);
        settings.Changed += (_, _) => HasResourceData = HasResourceData && settings.Current.ShowResourceUsage;
    }

    /// <summary>Raised when the panel should hide (Esc, close button).</summary>
    public event EventHandler? CloseRequested;

    public SettingsViewModel SettingsPage { get; }

    public StartupPageViewModel StartupPage { get; }

    /// <summary>The last inventory shown. The shell reads it to decide what to announce.</summary>
    public InventorySnapshot? Snapshot => _snapshot;

    public IReadOnlyDictionary<ApplicationKey, SecurityAdvisory> Advisories => _advisories;

    public ObservableCollection<ApplicationItemViewModel> Applications { get; } = [];

    public ObservableCollection<CatalogItemViewModel> CatalogResults { get; } = [];

    public IReadOnlyList<SortOption> SortOptions => SortOption.All;

    public string? HotkeyText { get; }

    public string Summary => UpdateCount > 0
        ? $"{InstalledCount} installed · {UpdateCount} update{(UpdateCount == 1 ? string.Empty : "s")}"
        : $"{InstalledCount} installed · all up to date";

    public bool HasUpdates => UpdateCount > 0;

    public bool IsActivityVisible => IsLoading || IsSearching || IsBusy;

    public bool IsMainPage => Page == DockPage.Main;

    public bool IsSettingsPage => Page == DockPage.Settings;

    public bool IsRestorePage => Page == DockPage.Restore;

    public bool IsInstalledSection
    {
        get => SelectedSection == DockSection.Installed;
        set => SelectIf(value, DockSection.Installed);
    }

    public bool IsUpdatesSection
    {
        get => SelectedSection == DockSection.Updates;
        set => SelectIf(value, DockSection.Updates);
    }

    public bool IsDiscoverSection
    {
        get => SelectedSection == DockSection.Discover;
        set => SelectIf(value, DockSection.Discover);
    }

    public bool IsStartupSection
    {
        get => SelectedSection == DockSection.Startup;
        set => SelectIf(value, DockSection.Startup);
    }

    /// <summary>Installed or Updates: the sections that show installed applications.</summary>
    public bool IsAppListSection => SelectedSection is DockSection.Installed or DockSection.Updates;

    public bool ShowListToolbar => IsAppListSection;

    public bool ShowSearch => SelectedSection != DockSection.Startup;

    public bool HasLeftoverOffer => LeftoverOffer is not null;

    public bool HasSecurityUpdates => SecurityUpdateCount > 0;

    public bool ShowUpdateAll => SelectedSection == DockSection.Updates && HasUpdates;

    public string UpdateBanner => SecurityUpdateCount > 0
        ? $"{UpdateCount} update{(UpdateCount == 1 ? string.Empty : "s")} · {SecurityUpdateCount} under active attack"
        : $"{UpdateCount} update{(UpdateCount == 1 ? string.Empty : "s")} ready to install";

    public bool ShowBulkBar => IsSelecting && SelectedCount > 0;

    public string BulkLabel => $"{SelectedCount} selected";

    public string BulkConfirmLabel => $"Remove {SelectedCount} app{(SelectedCount == 1 ? string.Empty : "s")}?";

    public bool HasAppUpdate => AppUpdateText is not null;

    public string SearchWatermark => SelectedSection == DockSection.Discover
        ? "Search WinGet and the Microsoft Store…"
        : "Filter installed apps…";

    public bool IsEmpty => !IsLoading && !IsSearching && SelectedSection switch
    {
        DockSection.Discover => CatalogResults.Count == 0,
        DockSection.Startup => false,
        _ => Applications.Count == 0,
    };

    public string EmptyMessage => SelectedSection switch
    {
        DockSection.Discover when SearchText.Trim().Length < MinimumQueryLength => "Type to search for something new to install.",
        DockSection.Discover => $"Nothing in the catalog matches “{SearchText.Trim()}”.",
        DockSection.Updates when string.IsNullOrWhiteSpace(SearchText) => "Everything is up to date.",
        DockSection.Startup => string.Empty,
        _ when !string.IsNullOrWhiteSpace(SearchText) => $"No installed app matches “{SearchText.Trim()}”.",
        _ => "No applications found.",
    };

    // Header gauges ---------------------------------------------------------------------------

    public double CpuFraction => _usage.SystemCpuPercent / 100;

    public IBrush CpuBrush => Format.Load(CpuFraction);

    public string CpuText => Format.Percent(_usage.SystemCpuPercent);

    public string AppsCpuText => $"{Format.Percent(_usage.ApplicationsCpuPercent)} in your apps";

    public double MemoryFraction => _usage.Memory.UsedFraction;

    public IBrush MemoryBrush => Format.Load(MemoryFraction);

    public string MemoryPercentText => Format.Percent(_usage.Memory.UsedFraction * 100);

    public string MemoryText => $"{Format.Bytes(_usage.ApplicationsMemoryBytes)} in your apps";

    public string MemoryDetail => $"{Format.Bytes(_usage.Memory.UsedBytes)} of {Format.Bytes(_usage.Memory.TotalBytes)} in use";

    public string AppsMemoryText => $"{_usage.RunningApplications} apps running · {Format.Bytes(_usage.ApplicationsMemoryBytes)}";

    /// <summary>Loads the inventory; cheap when the cache is fresh, so it runs every time the panel opens.</summary>
    public async Task LoadAsync(bool forceRefresh = false)
    {
        IsLoading = true;
        try
        {
            Apply(await _inventory.GetSnapshotAsync(forceRefresh, CancellationToken.None));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLoadFailed(ex);
            SetStatus($"Could not read the installed software: {ex.Message}", isError: true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Starts sampling CPU and memory every two seconds. Only while the panel is visible.</summary>
    public void StartMonitoring()
    {
        if (_monitorCancellation is not null)
        {
            return;
        }

        _monitorCancellation = new CancellationTokenSource();
        _ = MonitorAsync(_monitorCancellation.Token);
    }

    public void StopMonitoring()
    {
        _monitorCancellation?.Cancel();
        _monitorCancellation?.Dispose();
        _monitorCancellation = null;
        _monitor.Reset();
    }

    /// <summary>Takes one resource sample now. The monitoring loop calls it; tests and screenshots too.</summary>
    public async Task SampleResourcesAsync()
    {
        if (!_settings.Current.ShowResourceUsage || _snapshot is null)
        {
            HasResourceData = false;
            return;
        }

        var applications = _snapshot.Applications;
        var usage = await Task.Run(() => _monitor.Sample(applications));
        _usage = usage;
        foreach (var item in _items.Values)
        {
            item.Usage = usage.For(item.Key);
        }

        HasResourceData = usage.Memory.TotalBytes > 0;
        OnPropertyChanged(nameof(CpuFraction));
        OnPropertyChanged(nameof(CpuBrush));
        OnPropertyChanged(nameof(CpuText));
        OnPropertyChanged(nameof(AppsCpuText));
        OnPropertyChanged(nameof(MemoryFraction));
        OnPropertyChanged(nameof(MemoryBrush));
        OnPropertyChanged(nameof(MemoryText));
        OnPropertyChanged(nameof(MemoryPercentText));
        OnPropertyChanged(nameof(MemoryDetail));
        OnPropertyChanged(nameof(AppsMemoryText));
    }

    /// <summary>A new version of the dock is downloaded and can be applied.</summary>
    public void OfferAppUpdate(AppUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        _pendingAppUpdate = update;
        AppUpdateText = $"Software Dock {update.Version} is ready";
    }

    [RelayCommand]
    private void ApplyAppUpdate()
    {
        if (_pendingAppUpdate is not null)
        {
            _updater.ApplyAndRestart(_pendingAppUpdate);
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(forceRefresh: true);

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void TogglePin() => IsPinned = !IsPinned;

    [RelayCommand]
    private void ToggleSettings() => Page = Page == DockPage.Settings ? DockPage.Main : DockPage.Settings;

    [RelayCommand]
    private void ToggleSelecting() => IsSelecting = !IsSelecting;

    [RelayCommand]
    private void SelectAll()
    {
        var selectable = Applications.Where(a => a.CanSelect).ToList();
        var select = selectable.Any(a => !a.IsSelected);
        foreach (var item in selectable)
        {
            item.IsSelected = select;
        }
    }

    [RelayCommand]
    private void RequestBulkUninstall() => IsConfirmingBulk = true;

    [RelayCommand]
    private void CancelBulkUninstall() => IsConfirmingBulk = false;

    [RelayCommand]
    private async Task ConfirmBulkUninstallAsync()
    {
        IsConfirmingBulk = false;
        var selected = _items.Values.Where(i => i.IsSelected).OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        foreach (var item in selected)
        {
            item.IsWorking = true;
        }

        IsBusy = ++_runningOperations > 0;
        var finished = false;
        var progress = new Progress<BatchProgress>(p =>
        {
            if (!Volatile.Read(ref finished))
            {
                SetStatus($"Removing {p.Position} of {p.Total} · {p.ItemName}{(p.Line is null ? string.Empty : $" — {p.Line}")}", isError: false);
            }
        });

        try
        {
            var result = await _batches.UninstallManyAsync(selected.Select(i => i.Key).ToList(), progress);
            Volatile.Write(ref finished, true);
            SetStatus($"Bulk uninstall: {result.Summary}", isError: result.Failed > 0);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogOperationFailed(ex, "Bulk uninstall");
            SetStatus($"Bulk uninstall failed: {ex.Message}", isError: true);
        }
        finally
        {
            foreach (var item in selected)
            {
                item.IsWorking = false;
                item.IsSelected = false;
            }

            IsBusy = --_runningOperations > 0;
            IsSelecting = false;
        }

        await LoadAsync();
        await OfferLeftoversAsync(selected.Select(i => i.Model).ToList());
    }

    [RelayCommand]
    private async Task UpgradeAllAsync()
    {
        var items = Applications.Where(a => a.CanUpgrade).ToList();
        if (items.Count == 0)
        {
            return;
        }

        // One batch, not one operation per app: with "administrator rights once per batch" on,
        // that is one UAC prompt for all of them.
        foreach (var item in items)
        {
            item.IsWorking = true;
        }

        IsBusy = ++_runningOperations > 0;
        try
        {
            var result = await _batches.UpgradeManyAsync(
                items.Select(i => i.Key).ToList(),
                new Progress<BatchProgress>(p => SetStatus($"Updating {p.Position} of {p.Total} · {p.ItemName}{(p.Line is null ? string.Empty : $" — {p.Line}")}", isError: false)));
            SetStatus($"Update all: {result.Summary}", isError: result.Failed > 0);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogOperationFailed(ex, "Update all");
            SetStatus($"Update all failed: {ex.Message}", isError: true);
        }
        finally
        {
            foreach (var item in items)
            {
                item.IsWorking = false;
            }

            IsBusy = --_runningOperations > 0;
        }

        await LoadAsync();
    }

    /// <summary>
    /// The unattended run behind "Scheduled updates": refreshes, then upgrades every actionable app
    /// (pinned apps and skipped versions excluded) in one queue.
    /// </summary>
    public async Task<BatchResult> RunScheduledUpdatesAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _inventory.GetSnapshotAsync(forceRefresh: true, cancellationToken);
        Apply(snapshot);
        var rules = _settings.Current.UpdateRules;
        var keys = snapshot.Applications.Where(a => a.CanUpgrade && rules.IsActionable(a)).Select(a => a.Key).ToList();
        if (keys.Count == 0)
        {
            return new BatchResult([]);
        }

        IsBusy = ++_runningOperations > 0;
        try
        {
            var result = await _operations.UpgradeManyAsync(keys, new Progress<BatchProgress>(p => SetStatus($"Scheduled update {p.Position} of {p.Total} · {p.ItemName}", false)), cancellationToken);
            SetStatus($"Scheduled updates: {result.Summary}", result.Failed > 0);
            return result;
        }
        finally
        {
            IsBusy = --_runningOperations > 0;
            await LoadAsync();
        }
    }

    /// <summary>Pins, unpins or skips a version, then re-evaluates what counts as an update.</summary>
    internal void ChangeRules(Func<UpdateRules, UpdateRules> change)
    {
        _settings.Update(s => s.WithRules(change(s.UpdateRules)));
        if (_snapshot is not null)
        {
            Apply(_snapshot);
        }
    }

    internal Task UpgradeAsync(ApplicationItemViewModel item) =>
        RunAsync(item, i => i.IsWorking = true, i => i.IsWorking = false, $"Updating {item.Name}…", (progress, ct) => _operations.UpgradeAsync(item.Key, progress, ct));

    internal async Task UninstallAsync(ApplicationItemViewModel item)
    {
        var application = item.Model;
        await RunAsync(item, i => i.IsWorking = true, i => i.IsWorking = false, $"Removing {item.Name}…", (progress, ct) => _operations.UninstallAsync(item.Key, UninstallMode.Silent, progress, ct));
        await OfferLeftoversAsync([application]);
    }

    internal Task InstallAsync(CatalogItemViewModel item) =>
        RunAsync(
            item,
            i => i.IsWorking = true,
            i => i.IsWorking = false,
            $"Installing {item.Name}…",
            async (progress, ct) =>
            {
                var result = await _operations.InstallAsync(new InstallRequest(item.Package.Id, Source: item.Package.Source), progress, ct);
                item.IsInstalled = result.IsSuccess;
                return result;
            });

    internal void OnSelectionChanged() => SelectedCount = _items.Values.Count(i => i.IsSelected);

    internal async Task OpenRestoreAsync(SoftwareManifest manifest)
    {
        IsLoading = true;
        try
        {
            var plan = await _backup.PlanRestoreAsync(manifest, CancellationToken.None);
            Restore = new RestoreViewModel(manifest, plan, _batches, SetStatus, CloseRestore);
            Page = DockPage.Restore;
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        if (SelectedSection == DockSection.Discover)
        {
            _ = SearchCatalogAsync(value);
        }
        else
        {
            RebuildList();
        }
    }

    partial void OnSelectedSectionChanged(DockSection value)
    {
        switch (value)
        {
            case DockSection.Discover:
                IsSelecting = false;
                _ = SearchCatalogAsync(SearchText);
                break;
            case DockSection.Startup:
                IsSelecting = false;
                _ = StartupPage.LoadAsync();
                break;
            default:
                RebuildList();
                break;
        }
    }

    partial void OnIsSelectingChanged(bool value)
    {
        if (!value)
        {
            foreach (var item in _items.Values)
            {
                item.IsSelected = false;
            }

            IsConfirmingBulk = false;
        }

        foreach (var item in Applications)
        {
            item.RefreshActions();
        }
    }

    partial void OnSelectedSortChanged(SortOption value)
    {
        _settings.Update(s => s with { SortOrder = value.Order });
        RebuildList();
    }

    public void Dispose()
    {
        StopMonitoring();
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
    }

    /// <summary>Security advisories and last-use dates. Both are slow-ish and optional, so they arrive after the list.</summary>
    public async Task RefreshInsightsAsync(InventorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _lastUsed = await Task.Run(() => _insights.GetUsage(snapshot));
        _unused = await _insights.GetUnusedAsync(snapshot, UnusedApplications.DefaultThreshold, CancellationToken.None);
        _advisories = _settings.Current.SecurityAlerts
            ? await _insights.GetSecurityAdvisoriesAsync(snapshot, CancellationToken.None)
            : new Dictionary<ApplicationKey, SecurityAdvisory>();

        if (ReferenceEquals(snapshot, _snapshot))
        {
            ApplyInsights();
            if (SelectedSection == DockSection.Updates || SelectedSort.Order == SortOrder.LeastRecentlyUsed)
            {
                RebuildList();
            }
        }
    }

    private void ApplyInsights()
    {
        var now = _time.GetUtcNow();
        foreach (var item in _items.Values)
        {
            item.SecurityText = item.HasUpdate && _advisories.TryGetValue(item.Key, out var advisory) ? advisory.Summary : null;
            item.UnusedText = _unused.TryGetValue(item.Key, out var usage) ? $"Not opened for {Ago(now - usage.LastUsed)}" : null;
        }

        SecurityUpdateCount = _items.Values.Count(i => i.HasSecurityAdvisory);
    }

    private static string Ago(TimeSpan span) => span.TotalDays switch
    {
        >= 730 => $"{(int)(span.TotalDays / 365)} years",
        >= 365 => "a year",
        >= 60 => $"{(int)(span.TotalDays / 30)} months",
        _ => $"{(int)span.TotalDays} days",
    };

    /// <summary>After removals, offers to recycle what the uninstallers left behind — only for apps that are really gone.</summary>
    private async Task OfferLeftoversAsync(IReadOnlyList<InstalledApplication> removed)
    {
        if (_snapshot is null)
        {
            return;
        }

        var gone = removed.Where(a => _snapshot.Find(a.Key) is null).ToList();
        var folders = new List<LeftoverFolder>();
        foreach (var application in gone)
        {
            try
            {
                folders.AddRange(await _leftovers.FindAsync(application, _snapshot.Applications, CancellationToken.None));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogOperationFailed(ex, $"Looking for leftovers of {application.Name}");
            }
        }

        folders = folders.DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();
        if (folders.Count > 0)
        {
            var who = gone.Count == 1 ? gone[0].Name : $"{gone.Count} apps";
            LeftoverOffer = new LeftoverOfferViewModel(who, folders, _leftovers, SetStatus, () => LeftoverOffer = null);
        }
    }

    private void CloseRestore()
    {
        Page = DockPage.Main;
        Restore = null;
        _ = LoadAsync();
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(MonitorInterval);
            do
            {
                await SampleResourcesAsync();
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LogMonitoringFailed(ex);
            HasResourceData = false;
        }
    }

    private void Apply(InventorySnapshot snapshot)
    {
        var refreshed = !ReferenceEquals(_snapshot, snapshot);
        _snapshot = snapshot;
        var rules = _settings.Current.UpdateRules;
        InstalledCount = snapshot.Applications.Count;
        UpdateCount = snapshot.Applications.Count(rules.IsActionable);

        foreach (var application in snapshot.Applications)
        {
            if (_items.TryGetValue(application.Key, out var existing))
            {
                existing.Model = application;
            }
            else
            {
                _items[application.Key] = new ApplicationItemViewModel(application, this) { Usage = _usage.For(application.Key) };
            }
        }

        foreach (var gone in _items.Keys.Where(k => snapshot.Find(k) is null).ToList())
        {
            _items.Remove(gone);
        }

        foreach (var item in _items.Values)
        {
            item.IsUpdateActionable = rules.IsActionable(item.Model);
            item.IsPinned = rules.IsPinned(item.Model);
        }

        ApplyInsights();
        if (refreshed)
        {
            _ = RefreshInsightsAsync(snapshot);
        }

        if (snapshot.IsPartial)
        {
            SetStatus(string.Join(" ", snapshot.Warnings), isError: true);
        }

        OnSelectionChanged();
        RebuildList();
    }

    private void RebuildList()
    {
        if (_snapshot is null)
        {
            return;
        }

        var visible = _snapshot.Query(SearchText)
            .Select(a => _items[a.Key])
            .Where(i => SelectedSection != DockSection.Updates || i.HasUpdate);

        visible = SelectedSort.Order switch
        {
            SortOrder.Size => visible.OrderByDescending(i => i.Model.EstimatedSizeBytes ?? -1),
            SortOrder.RecentlyInstalled => visible.OrderByDescending(i => i.Model.InstallDate ?? DateOnly.MinValue),
            SortOrder.MemoryUse => visible.OrderByDescending(i => i.Usage.MemoryBytes),
            SortOrder.LeastRecentlyUsed => visible.OrderBy(i => _lastUsed.TryGetValue(i.Key, out var u) ? u.LastUsed : DateTimeOffset.MaxValue),
            _ => visible,
        };

        if (SelectedSection == DockSection.Updates)
        {
            // Actively exploited first: those are the ones worth doing now.
            visible = visible.OrderByDescending(i => i.HasSecurityAdvisory);
        }

        Applications.Clear();
        foreach (var item in visible.ToList())
        {
            Applications.Add(item);
            _ = item.EnsureIconAsync(_icons);
        }
    }

    private async Task SearchCatalogAsync(string text)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        var cancellation = _searchCancellation = new CancellationTokenSource();

        var query = text.Trim();
        if (query.Length < MinimumQueryLength)
        {
            CatalogResults.Clear();
            IsSearching = false;
            return;
        }

        IsSearching = true;
        try
        {
            await Task.Delay(SearchDebounce, cancellation.Token);
            var results = await _catalog.SearchAsync(query, 30, cancellation.Token);
            var installedIds = _snapshot?.Applications.Select(a => a.PackageId).OfType<PackageId>().ToHashSet() ?? [];

            CatalogResults.Clear();
            foreach (var result in results)
            {
                CatalogResults.Add(new CatalogItemViewModel(result, installedIds.Contains(result.Id), this));
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            LogSearchFailed(ex, query);
            CatalogResults.Clear();
            SetStatus(ex is PackageManagerUnavailableException ? ex.Message : $"Search failed: {ex.Message}", isError: true);
        }

        if (cancellation == _searchCancellation)
        {
            IsSearching = false;
        }
    }

    private async Task RunAsync<TItem>(
        TItem item,
        Action<TItem> begin,
        Action<TItem> end,
        string description,
        Func<IProgress<string>, CancellationToken, Task<OperationResult>> operation)
    {
        begin(item);
        IsBusy = ++_runningOperations > 0;
        SetStatus(description, isError: false);

        // Created on the UI thread, so installer output lines are marshalled back to it. Progress
        // is delivered asynchronously: a line posted just before the operation returns can arrive
        // after it, and must not overwrite the result.
        var finished = false;
        var progress = new Progress<string>(line =>
        {
            if (!Volatile.Read(ref finished))
            {
                SetStatus(line, isError: false);
            }
        });
        try
        {
            var result = await operation(progress, CancellationToken.None);
            Volatile.Write(ref finished, true);
            SetStatus(result.Message, isError: !result.IsSuccess);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogOperationFailed(ex, description);
            SetStatus(ex is PackageManagerUnavailableException ? ex.Message : $"{description} failed: {ex.Message}", isError: true);
        }
        finally
        {
            end(item);
            IsBusy = --_runningOperations > 0;
        }

        await LoadAsync();
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }

    private void SelectIf(bool selected, DockSection section)
    {
        if (selected)
        {
            SelectedSection = section;
        }
    }

    private void OnListChanged()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Loading the inventory failed")]
    private partial void LogLoadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Catalog search for '{Query}' failed")]
    private partial void LogSearchFailed(Exception exception, string query);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Description} threw")]
    private partial void LogOperationFailed(Exception exception, string description);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Resource monitoring stopped")]
    private partial void LogMonitoringFailed(Exception exception);
}
