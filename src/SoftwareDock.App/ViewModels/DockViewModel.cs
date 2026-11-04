using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoftwareDock.App.Platform;
using SoftwareDock.App.Shell;
using SoftwareDock.Application.Abstractions;
using SoftwareDock.Application.Inventory;
using SoftwareDock.Application.Operations;
using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Operations;
using SoftwareDock.Domain.Packages;

namespace SoftwareDock.App.ViewModels;

/// <summary>State and behaviour of the dock panel. Knows nothing about windows or the tray.</summary>
public sealed partial class DockViewModel : ObservableObject, IDisposable
{
    private const int MinimumQueryLength = 2;
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(350);

    private readonly ISoftwareInventory _inventory;
    private readonly ISoftwareOperations _operations;
    private readonly IPackageManager _catalog;
    private readonly IIconProvider _icons;
    private readonly ILogger<DockViewModel> _logger;
    private readonly Dictionary<ApplicationKey, ApplicationItemViewModel> _items = [];

    private InventorySnapshot? _snapshot;
    private CancellationTokenSource? _searchCancellation;
    private int _runningOperations;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstalledSection), nameof(IsUpdatesSection), nameof(IsDiscoverSection), nameof(IsEmpty), nameof(EmptyMessage), nameof(SearchWatermark), nameof(ShowUpdateAll))]
    private DockSection _selectedSection;

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

    public DockViewModel(
        ISoftwareInventory inventory,
        ISoftwareOperations operations,
        IPackageManager catalog,
        IIconProvider icons,
        IOptions<DockOptions> options,
        ILogger<DockViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _inventory = inventory;
        _operations = operations;
        _catalog = catalog;
        _icons = icons;
        _logger = logger;
        HotkeyText = HotkeyGesture.TryParse(options.Value.Hotkey, out var gesture) ? gesture!.Display : null;

        Applications.CollectionChanged += (_, _) => OnListChanged();
        CatalogResults.CollectionChanged += (_, _) => OnListChanged();
    }

    /// <summary>Raised when the panel should hide (Esc, close button).</summary>
    public event EventHandler? CloseRequested;

    public ObservableCollection<ApplicationItemViewModel> Applications { get; } = [];

    public ObservableCollection<CatalogItemViewModel> CatalogResults { get; } = [];

    public string? HotkeyText { get; }

    public string Summary => UpdateCount > 0
        ? $"{InstalledCount} installed · {UpdateCount} update{(UpdateCount == 1 ? string.Empty : "s")}"
        : $"{InstalledCount} installed · all up to date";

    public bool HasUpdates => UpdateCount > 0;

    public bool ShowUpdateAll => SelectedSection == DockSection.Updates && HasUpdates;

    public string UpdateBanner => $"{UpdateCount} update{(UpdateCount == 1 ? string.Empty : "s")} ready to install";

    public bool IsActivityVisible => IsLoading || IsSearching || IsBusy;

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

    public string SearchWatermark => SelectedSection == DockSection.Discover
        ? "Search WinGet and the Microsoft Store…"
        : "Filter installed apps…";

    public bool IsEmpty => !IsLoading && !IsSearching && (SelectedSection == DockSection.Discover ? CatalogResults.Count == 0 : Applications.Count == 0);

    public string EmptyMessage => SelectedSection switch
    {
        DockSection.Discover when SearchText.Trim().Length < MinimumQueryLength => "Type to search for something new to install.",
        DockSection.Discover => $"Nothing in the catalog matches “{SearchText.Trim()}”.",
        DockSection.Updates when string.IsNullOrWhiteSpace(SearchText) => "Everything is up to date.",
        _ when !string.IsNullOrWhiteSpace(SearchText) => $"No installed app matches “{SearchText.Trim()}”.",
        _ => "No applications found.",
    };

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

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(forceRefresh: true);

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void TogglePin() => IsPinned = !IsPinned;

    [RelayCommand]
    private async Task UpgradeAllAsync()
    {
        foreach (var item in Applications.Where(a => a.CanUpgrade).ToList())
        {
            await UpgradeAsync(item);
        }
    }

    internal Task UpgradeAsync(ApplicationItemViewModel item) =>
        RunAsync(item, i => i.IsWorking = true, i => i.IsWorking = false, $"Updating {item.Name}…", (progress, ct) => _operations.UpgradeAsync(item.Key, progress, ct));

    internal Task UninstallAsync(ApplicationItemViewModel item) =>
        RunAsync(item, i => i.IsWorking = true, i => i.IsWorking = false, $"Removing {item.Name}…", (progress, ct) => _operations.UninstallAsync(item.Key, UninstallMode.Silent, progress, ct));

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
        if (value == DockSection.Discover)
        {
            _ = SearchCatalogAsync(SearchText);
        }
        else
        {
            RebuildList();
        }
    }

    public void Dispose()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
    }

    private void Apply(InventorySnapshot snapshot)
    {
        _snapshot = snapshot;
        InstalledCount = snapshot.Applications.Count;
        UpdateCount = snapshot.Applications.Count(a => a.HasUpdate);

        foreach (var application in snapshot.Applications)
        {
            if (_items.TryGetValue(application.Key, out var existing))
            {
                existing.Model = application;
            }
            else
            {
                _items[application.Key] = new ApplicationItemViewModel(application, this);
            }
        }

        foreach (var gone in _items.Keys.Where(k => snapshot.Find(k) is null).ToList())
        {
            _items.Remove(gone);
        }

        if (snapshot.IsPartial)
        {
            SetStatus(string.Join(" ", snapshot.Warnings), isError: true);
        }

        RebuildList();
    }

    private void RebuildList()
    {
        if (_snapshot is null)
        {
            return;
        }

        var visible = _snapshot.Query(SearchText)
            .Where(a => SelectedSection != DockSection.Updates || a.HasUpdate)
            .Select(a => _items[a.Key])
            .ToList();

        Applications.Clear();
        foreach (var item in visible)
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
}
