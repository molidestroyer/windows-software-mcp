namespace SoftwareDock.App.Services;

/// <summary>What the view models need from the windowing system, without depending on a window.</summary>
public interface IShellServices
{
    Task<string?> PickSaveFileAsync(string title, string suggestedFileName);

    Task<string?> PickOpenFileAsync(string title);

    Task CopyTextAsync(string text);

    Task OpenFolderAsync(string path);
}
