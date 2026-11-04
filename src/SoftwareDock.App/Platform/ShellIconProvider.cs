using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace SoftwareDock.App.Platform;

/// <summary>Extracts application icons from <c>.exe</c>/<c>.dll</c>/<c>.ico</c> files, once per path.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class ShellIconProvider(ILogger<ShellIconProvider> logger) : IIconProvider, IDisposable
{
    // Two hundred icons extracted at once is a measurable stall on a cold disk.
    private readonly SemaphoreSlim _throttle = new(4, 4);
    private readonly ConcurrentDictionary<string, Task<AvaloniaBitmap?>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public Task<AvaloniaBitmap?> GetIconAsync(string? displayIcon, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
        {
            return Task.FromResult<AvaloniaBitmap?>(null);
        }

        return _cache.GetOrAdd(displayIcon, key => LoadAsync(key, cancellationToken));
    }

    public void Dispose() => _throttle.Dispose();

    /// <summary>Splits <c>"C:\App\app.exe",2</c> into path and icon index.</summary>
    internal static (string Path, int Index) ParseDisplayIcon(string displayIcon)
    {
        var text = displayIcon.Trim();
        var index = 0;
        var comma = text.LastIndexOf(',');
        if (comma > 0 && int.TryParse(text[(comma + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            index = parsed;
            text = text[..comma];
        }

        return (text.Trim().Trim('"'), index);
    }

    private async Task<AvaloniaBitmap?> LoadAsync(string displayIcon, CancellationToken cancellationToken)
    {
        await _throttle.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => Extract(displayIcon), cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or ExternalException or UnauthorizedAccessException)
        {
            LogExtractionFailed(ex, displayIcon);
            return null;
        }
        finally
        {
            _throttle.Release();
        }
    }

    private static AvaloniaBitmap? Extract(string displayIcon)
    {
        var (path, index) = ParseDisplayIcon(displayIcon);
        if (!File.Exists(path))
        {
            return null;
        }

        using var bitmap = path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)
            ? new Icon(path, 32, 32).ToBitmap()
            : ExtractFromModule(path, index);
        if (bitmap is null)
        {
            return null;
        }

        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        png.Position = 0;
        return new AvaloniaBitmap(png);
    }

    private static Bitmap? ExtractFromModule(string path, int index)
    {
        if (NativeMethods.ExtractIconEx(path, index, out var large, out var small, 1) == 0 || large == 0)
        {
            return null;
        }

        try
        {
            using var icon = Icon.FromHandle(large);
            return icon.ToBitmap();
        }
        finally
        {
            NativeMethods.DestroyIcon(large);
            if (small != 0)
            {
                NativeMethods.DestroyIcon(small);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not extract the icon {DisplayIcon}")]
    private partial void LogExtractionFailed(Exception exception, string displayIcon);
}
