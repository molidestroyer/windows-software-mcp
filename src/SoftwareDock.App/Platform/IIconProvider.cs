using Avalonia.Media.Imaging;
using SoftwareDock.Domain.Applications;

namespace SoftwareDock.App.Platform;

/// <summary>Finds and draws an application's icon.</summary>
public interface IIconProvider
{
    /// <returns>The icon, or <see langword="null"/> to fall back to the initials avatar.</returns>
    Task<Bitmap?> GetIconAsync(InstalledApplication application, CancellationToken cancellationToken);
}

/// <summary>No icons: every application gets its initials avatar.</summary>
public sealed class InitialsOnlyIconProvider : IIconProvider
{
    public Task<Bitmap?> GetIconAsync(InstalledApplication application, CancellationToken cancellationToken) => Task.FromResult<Bitmap?>(null);
}
