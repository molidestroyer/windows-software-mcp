using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoftwareDock.Application.Abstractions;

namespace SoftwareDock.App.ViewModels;

/// <summary>"Discord left 3 folders behind (240 MB)" — offered after an uninstall, cleaned only on request.</summary>
public sealed partial class LeftoverOfferViewModel(
    string applications,
    IReadOnlyList<LeftoverFolder> folders,
    ILeftoverCleaner cleaner,
    Action<string, bool> status,
    Action dismiss) : ObservableObject
{
    [ObservableProperty]
    private bool _isCleaning;

    public IReadOnlyList<LeftoverFolder> Folders { get; } = folders;

    public string Text =>
        $"{applications} left {Folders.Count} folder{(Folders.Count == 1 ? string.Empty : "s")} behind ({Format.Bytes(Folders.Sum(f => f.SizeBytes))})";

    public string Details => string.Join(Environment.NewLine, Folders.Select(f => f.Path));

    [RelayCommand]
    private async Task CleanAsync()
    {
        IsCleaning = true;
        try
        {
            var result = await cleaner.RecycleAsync(Folders, CancellationToken.None);
            status(result.Message, !result.IsSuccess);
        }
        finally
        {
            IsCleaning = false;
            dismiss();
        }
    }

    [RelayCommand]
    private void Keep() => dismiss();
}
