using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace SoftwareDock.App.ViewModels;

internal static class Format
{
    private static readonly IBrush Calm = new ImmutableSolidColorBrush(Color.Parse("#22C55E"));
    private static readonly IBrush Busy = new ImmutableSolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush Hot = new ImmutableSolidColorBrush(Color.Parse("#EF4444"));

    public static string Bytes(long bytes) => bytes switch
    {
        >= 1L << 30 => string.Create(CultureInfo.CurrentCulture, $"{bytes / (double)(1L << 30):0.0} GB"),
        >= 1L << 20 => string.Create(CultureInfo.CurrentCulture, $"{bytes / (double)(1L << 20):0} MB"),
        _ => string.Create(CultureInfo.CurrentCulture, $"{Math.Max(0, bytes / 1024)} KB"),
    };

    public static string Percent(double percent) =>
        percent is > 0 and < 10
            ? percent.ToString("0.0", CultureInfo.CurrentCulture) + "%"
            : percent.ToString("0", CultureInfo.CurrentCulture) + "%";

    /// <summary>Green while there is headroom, amber when it gets busy, red near the limit.</summary>
    public static IBrush Load(double fraction) => fraction switch
    {
        < 0.6 => Calm,
        < 0.85 => Busy,
        _ => Hot,
    };
}
