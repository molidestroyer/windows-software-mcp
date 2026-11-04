using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoftwareDock.Domain.Packages;

namespace SoftwareDock.App.ViewModels;

/// <summary>One search result in Discover.</summary>
public sealed partial class CatalogItemViewModel : ObservableObject
{
    private readonly DockViewModel _owner;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInstall))]
    private bool _isInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInstall))]
    private bool _isWorking;

    public CatalogItemViewModel(PackageSearchResult package, bool isInstalled, DockViewModel owner)
    {
        Package = package;
        _isInstalled = isInstalled;
        _owner = owner;
        Initials = Avatar.Initials(package.Name);
        AvatarBrush = Avatar.BrushFor(package.Name);
    }

    public PackageSearchResult Package { get; }

    public string Name => Package.Name;

    public string Subtitle => string.Join("  ·  ", new[] { Package.Id.Value, Package.Version is null ? null : $"v{Package.Version}", Package.Source }.OfType<string>());

    public string Initials { get; }

    public IBrush AvatarBrush { get; }

    public bool ShowInstall => !IsInstalled && !IsWorking;

    [RelayCommand]
    private Task InstallAsync() => _owner.InstallAsync(this);
}
