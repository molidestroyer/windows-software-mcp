using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoftwareDock.App.Platform;
using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Monitoring;

namespace SoftwareDock.App.ViewModels;

/// <summary>One installed application in the list. Survives refreshes so icons and in-flight state are kept.</summary>
public sealed partial class ApplicationItemViewModel : ObservableObject
{
    private readonly DockViewModel _owner;
    private bool _iconRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(Subtitle), nameof(UpdateText), nameof(HasUpdate), nameof(CanUpgrade), nameof(ShowUninstall), nameof(ShowUpgrade), nameof(SizeText), nameof(SkipLabel), nameof(CanSkip))]
    private InstalledApplication _model;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    private Bitmap? _icon;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUninstall), nameof(ShowUpgrade), nameof(ShowActions))]
    private bool _isConfirmingUninstall;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUninstall), nameof(ShowUpgrade), nameof(ShowActions))]
    private bool _isWorking;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUpgrade), nameof(UpdateText), nameof(HasUpdate))]
    private bool _isUpdateActionable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPin), nameof(CanSkip))]
    private bool _isPinned;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSecurityAdvisory))]
    private string? _securityText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnused))]
    private string? _unusedText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(UsageText), nameof(CpuFraction), nameof(CpuBrush))]
    private ResourceUsage _usage = ResourceUsage.None;

    public ApplicationItemViewModel(InstalledApplication model, DockViewModel owner)
    {
        _model = model;
        _owner = owner;
        Initials = Avatar.Initials(model.Name);
        AvatarBrush = Avatar.BrushFor(model.Name);
    }

    public ApplicationKey Key => Model.Key;

    public string Name => Model.Name;

    public string Initials { get; }

    public IBrush AvatarBrush { get; }

    public bool HasIcon => Icon is not null;

    public string Subtitle => string.Join("  ·  ", new[] { Model.Publisher, Model.Version is null ? null : $"v{Model.Version}" }.OfType<string>());

    /// <summary>An update the user still wants to see: not pinned, not a skipped version.</summary>
    public bool HasUpdate => Model.HasUpdate && IsUpdateActionable;

    public bool HasSecurityAdvisory => SecurityText is not null;

    public bool IsUnused => UnusedText is not null;

    public bool CanPin => !IsPinned;

    public bool CanSkip => Model.HasUpdate && IsUpdateActionable;

    public string SkipLabel => $"Skip version {Model.AvailableVersion}";

    public bool CanUpgrade => Model.CanUpgrade && IsUpdateActionable;

    public string? UpdateText => HasUpdate ? $"→ {Model.AvailableVersion}" : null;

    public string? SizeText => Model.EstimatedSizeBytes is { } bytes ? Format.Bytes(bytes) : null;

    public bool ShowActions => !IsWorking && !IsConfirmingUninstall && !_owner.IsSelecting;

    public bool IsRunning => Usage.IsRunning;

    public string UsageText => $"{Format.Percent(Usage.CpuPercent)} CPU  ·  {Format.Bytes(Usage.MemoryBytes)}";

    /// <summary>CPU drawn against one core's worth, so a single busy app reads as busy on a 16-core PC.</summary>
    public double CpuFraction => Math.Min(1, Usage.CpuPercent * Environment.ProcessorCount / 100);

    public Avalonia.Media.IBrush CpuBrush => Format.Load(CpuFraction);

    public bool CanSelect => Model.CanUninstall;

    public bool ShowUpgrade => ShowActions && Model.CanUpgrade && IsUpdateActionable;

    public bool ShowUninstall => ShowActions && Model.CanUninstall;

    partial void OnIsSelectedChanged(bool value) => _owner.OnSelectionChanged();

    /// <summary>Selection mode toggled: action buttons hide and checkboxes show.</summary>
    internal void RefreshActions()
    {
        OnPropertyChanged(nameof(ShowActions));
        OnPropertyChanged(nameof(ShowUpgrade));
        OnPropertyChanged(nameof(ShowUninstall));
    }

    internal async Task EnsureIconAsync(IIconProvider icons)
    {
        if (_iconRequested)
        {
            return;
        }

        _iconRequested = true;
        Icon = await icons.GetIconAsync(Model, CancellationToken.None);
    }

    [RelayCommand]
    private Task UpgradeAsync() => _owner.UpgradeAsync(this);

    [RelayCommand]
    private void RequestUninstall() => IsConfirmingUninstall = true;

    [RelayCommand]
    private void Pin() => _owner.ChangeRules(r => r.Pin(Key));

    [RelayCommand]
    private void Unpin() => _owner.ChangeRules(r => r.Unpin(Key));

    [RelayCommand]
    private void SkipVersion()
    {
        if (Model.AvailableVersion is { } version)
        {
            _owner.ChangeRules(r => r.Skip(Key, version));
        }
    }

    [RelayCommand]
    private void CancelUninstall() => IsConfirmingUninstall = false;

    [RelayCommand]
    private Task ConfirmUninstallAsync()
    {
        IsConfirmingUninstall = false;
        return _owner.UninstallAsync(this);
    }
}
