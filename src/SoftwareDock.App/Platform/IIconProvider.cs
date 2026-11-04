using Avalonia.Media.Imaging;

namespace SoftwareDock.App.Platform;

/// <summary>Turns a registry <c>DisplayIcon</c> into something the dock can draw.</summary>
public interface IIconProvider
{
    /// <returns>The icon, or <see langword="null"/> to fall back to the initials avatar.</returns>
    Task<Bitmap?> GetIconAsync(string? displayIcon, CancellationToken cancellationToken);
}

/// <summary>No icons: every application gets its initials avatar.</summary>
public sealed class InitialsOnlyIconProvider : IIconProvider
{
    public Task<Bitmap?> GetIconAsync(string? displayIcon, CancellationToken cancellationToken) => Task.FromResult<Bitmap?>(null);
}
