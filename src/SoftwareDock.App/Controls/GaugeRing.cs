using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SoftwareDock.App.Controls;

/// <summary>A circular gauge: a faint track and a rounded arc that fills clockwise from 12 o'clock.</summary>
public sealed class GaugeRing : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<GaugeRing, double>(nameof(Value), coerce: (_, v) => double.IsNaN(v) ? 0 : Math.Clamp(v, 0, 1));

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<GaugeRing, double>(nameof(Thickness), 5);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<GaugeRing, IBrush?>(nameof(Foreground), Brushes.MediumPurple);

    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<GaugeRing, IBrush?>(nameof(Track), new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)));

    static GaugeRing() => AffectsRender<GaugeRing>(ValueProperty, ThicknessProperty, ForegroundProperty, TrackProperty);

    /// <summary>Fill, from 0 to 1.</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var size = Math.Min(Bounds.Width, Bounds.Height);
        var radius = (size - Thickness) / 2;
        if (radius <= 0)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        context.DrawEllipse(null, new Pen(Track, Thickness), center, radius, radius);

        var value = Value;
        if (value <= 0.001)
        {
            return;
        }

        var pen = new Pen(Foreground, Thickness, lineCap: PenLineCap.Round);
        if (value >= 0.999)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var angle = (value * 360) - 90;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(
            center.X + (radius * Math.Cos(angle * Math.PI / 180)),
            center.Y + (radius * Math.Sin(angle * Math.PI / 180)));

        var arc = new StreamGeometry();
        using (var geometry = arc.Open())
        {
            geometry.BeginFigure(start, isFilled: false);
            geometry.ArcTo(end, new Size(radius, radius), 0, isLargeArc: value > 0.5, SweepDirection.Clockwise);
            geometry.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, pen, arc);
    }
}
