using Avalonia.Platform.Storage;
using SoftwareDock.App.Shell;

namespace SoftwareDock.App.Services;

/// <summary>
/// File pickers, clipboard and folder launching through the dock window. While a picker is open
/// the anchored panel must not auto-hide, or the picker loses its owner mid-dialog.
/// </summary>
public sealed class AvaloniaShellServices(Lazy<DockWindow> window) : IShellServices
{
    private static readonly FilePickerFileType JsonFiles = new("Backup (*.json)") { Patterns = ["*.json"], MimeTypes = ["application/json"] };

    public async Task<string?> PickSaveFileAsync(string title, string suggestedFileName)
    {
        using var _ = window.Value.SuspendAutoHide();
        var file = await window.Value.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "json",
            FileTypeChoices = [JsonFiles],
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickOpenFileAsync(string title)
    {
        using var _ = window.Value.SuspendAutoHide();
        var files = await window.Value.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [JsonFiles],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task CopyTextAsync(string text)
    {
        if (window.Value.Clipboard is { } clipboard)
        {
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
        }
    }

    public async Task OpenFolderAsync(string path)
    {
        Directory.CreateDirectory(path);
        await window.Value.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
    }
}
