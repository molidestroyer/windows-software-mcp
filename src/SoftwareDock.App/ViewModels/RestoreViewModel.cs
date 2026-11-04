using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoftwareDock.App.Services;
using SoftwareDock.Application.Operations;
using SoftwareDock.Domain.Backup;
using SoftwareDock.Domain.Packages;

namespace SoftwareDock.App.ViewModels;

/// <summary>"Restore from backup": what the backup has that this PC does not, ready to install in one go.</summary>
public sealed partial class RestoreViewModel : ObservableObject
{
    private readonly BatchRunner _batches;
    private readonly Action<string, bool> _status;
    private readonly Action _close;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    private bool _isInstalling;

    [ObservableProperty]
    private string? _progressText;

    public RestoreViewModel(SoftwareManifest manifest, RestorePlan plan, BatchRunner batches, Action<string, bool> status, Action close)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(plan);
        _batches = batches;
        _status = status;
        _close = close;

        var taken = manifest.CreatedAt == DateTimeOffset.MinValue
            ? string.Empty
            : $" · {manifest.CreatedAt.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture)}";
        Source = $"{manifest.MachineName ?? "winget export"}{taken}";
        AlreadyInstalledText = plan.AlreadyInstalled.Count == 0 ? null : $"{plan.AlreadyInstalled.Count} already installed on this PC";

        foreach (var package in plan.ToInstall)
        {
            var item = new RestoreItemViewModel(package);
            item.PropertyChanged += (_, _) => OnPropertyChanged(nameof(SelectedCount));
            Packages.Add(item);
        }

        foreach (var manual in plan.InstallManually)
        {
            Manual.Add(manual.Publisher is null ? manual.Name : $"{manual.Name}  ·  {manual.Publisher}");
        }
    }

    public string Source { get; }

    public string? AlreadyInstalledText { get; }

    public ObservableCollection<RestoreItemViewModel> Packages { get; } = [];

    public ObservableCollection<string> Manual { get; } = [];

    public bool HasPackages => Packages.Count > 0;

    public bool HasManual => Manual.Count > 0;

    public int SelectedCount => Packages.Count(p => p.IsSelected);

    public bool CanInstall => !IsInstalling;

    public string InstallLabel => $"Install selected";

    [RelayCommand]
    private void SelectAll(bool selected)
    {
        foreach (var package in Packages)
        {
            package.IsSelected = selected;
        }
    }

    [RelayCommand]
    private void Cancel() => _close();

    [RelayCommand]
    private async Task InstallAsync()
    {
        var selected = Packages.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        IsInstalling = true;
        try
        {
            var progress = new Progress<BatchProgress>(p => ProgressText = $"Installing {p.Position} of {p.Total} · {p.ItemName}{(p.Line is null ? string.Empty : $" — {p.Line}")}");
            var result = await _batches.InstallManyAsync(
                selected.Select(p => new InstallRequest(p.Package.Id, Source: p.Package.Source)).ToList(),
                progress);

            for (var i = 0; i < selected.Count; i++)
            {
                selected[i].Outcome = result.Items[i].Result is { } r ? (r.IsSuccess ? "Installed" : r.Message) : "Skipped";
                selected[i].IsSelected = !(result.Items[i].Result?.IsSuccess ?? false);
            }

            ProgressText = null;
            _status($"Restore: {result.Summary}", result.Failed > 0);
        }
        finally
        {
            IsInstalling = false;
        }
    }
}

public sealed partial class RestoreItemViewModel(ManifestPackage package) : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private string? _outcome;

    public ManifestPackage Package { get; } = package;

    public string Name => Package.Name;

    public string Subtitle => string.Join("  ·  ", new[] { Package.Id.Value, Package.Source }.OfType<string>());
}
