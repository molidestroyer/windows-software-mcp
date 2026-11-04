using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoftwareDock.App.Platform;
using SoftwareDock.Domain.Applications;

namespace SoftwareDock.App.ViewModels;

/// <summary>One installed application in the list. Survives refreshes so icons and in-flight state are kept.</summary>
public sealed partial class ApplicationItemViewModel : ObservableObject
{
    private readonly DockViewModel _owner;
    private bool _iconRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(Subtitle), nameof(UpdateText), nameof(HasUpdate), nameof(CanUpgrade), nameof(ShowUninstall), nameof(ShowUpgrade), nameof(SizeText))]
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

    public bool HasUpdate => Model.HasUpdate;

    public bool CanUpgrade => Model.CanUpgrade;

    public string? UpdateText => Model.HasUpdate ? $"→ {Model.AvailableVersion}" : null;

    public string? SizeText => Model.EstimatedSizeBytes is { } bytes ? FormatSize(bytes) : null;

    public bool ShowActions => !IsWorking && !IsConfirmingUninstall;

    public bool ShowUpgrade => ShowActions && Model.CanUpgrade;

    public bool ShowUninstall => ShowActions && Model.CanUninstall;

    internal async Task EnsureIconAsync(IIconProvider icons)
    {
        if (_iconRequested || Model.IconPath is null)
        {
            return;
        }

        _iconRequested = true;
        Icon = await icons.GetIconAsync(Model.IconPath, CancellationToken.None);
    }

    [RelayCommand]
    private Task UpgradeAsync() => _owner.UpgradeAsync(this);

    [RelayCommand]
    private void RequestUninstall() => IsConfirmingUninstall = true;

    [RelayCommand]
    private void CancelUninstall() => IsConfirmingUninstall = false;

    [RelayCommand]
    private Task ConfirmUninstallAsync()
    {
        IsConfirmingUninstall = false;
        return _owner.UninstallAsync(this);
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => string.Create(CultureInfo.CurrentCulture, $"{bytes / (double)(1L << 30):0.#} GB"),
        >= 1L << 20 => string.Create(CultureInfo.CurrentCulture, $"{bytes / (double)(1L << 20):0} MB"),
        _ => string.Create(CultureInfo.CurrentCulture, $"{Math.Max(1, bytes / 1024)} KB"),
    };
}
