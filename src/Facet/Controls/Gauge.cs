using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Facet.Theme;

namespace Facet.Controls;

/// <summary>
/// Open ring gauge (battery around the network icon): the value as a number in a gap at the top of the ring, a glyph
/// in the middle, and two arcs that fill clockwise from the bottom-left (left arc first, then the right).
/// Turns red at or below <see cref="WarnAt"/>. Drawn directly, so it costs one element on the dock.
/// </summary>
public sealed class Gauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double),
        typeof(Gauge), new FrameworkPropertyMetadata(0.0, OnValueChanged));

    /// <summary>The value the arcs are drawn at: follows <see cref="Value"/>, sweeping over to a new one.</summary>
    private static readonly DependencyProperty ShownProperty = Register("Shown", typeof(double), 0.0);

    /// <summary>0..1 through a one-off glow (e.g. the charger was plugged in); 0 = none.</summary>
    private static readonly DependencyProperty PulseProperty = Register("PulseProgress", typeof(double), 0.0);

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var gauge = (Gauge)d;
        // values set while the tile is being built show at once; later changes sweep
        if (gauge.IsLoaded) Motion.To(gauge, ShownProperty, (double)e.NewValue, 500, Motion.EaseInOut);
        else Motion.Set(gauge, ShownProperty, (double)e.NewValue);
    }

    /// <summary>A soft ring of light that swells out of the gauge and fades (nothing when motion is off).</summary>
    public void Pulse()
    {
        if (!IsLoaded || !Motion.Enabled) return;
        Motion.To(this, PulseProperty, 1, 750, Motion.EaseOut, from: 0, done: () => Motion.Set(this, PulseProperty, 0));
    }
    public static readonly DependencyProperty LabelProperty = Register(nameof(Label), typeof(string), null);
    public static readonly DependencyProperty GlyphProperty = Register(nameof(Glyph), typeof(string), null);
    public static readonly DependencyProperty FillProperty = Register(nameof(Fill), typeof(Brush), null);
    public static readonly DependencyProperty WarnAtProperty = Register(nameof(WarnAt), typeof(double), -1.0);
    public static readonly DependencyProperty GlyphOpacityProperty = Register(nameof(GlyphOpacity), typeof(double), 1.0);

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(Gauge), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>0..100</summary>
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    /// <summary>Text in the top gap (usually the value).</summary>
    public string? Label { get => (string?)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    /// <summary>Icon-font glyph drawn in the centre.</summary>
    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    /// <summary>Arc colour; defaults to the foreground (red when at or below <see cref="WarnAt"/>).</summary>
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public double WarnAt { get => (double)GetValue(WarnAtProperty); set => SetValue(WarnAtProperty, value); }
    /// <summary>Dims only the centre glyph (e.g. Wi-Fi off) while the ring keeps showing its value.</summary>
    public double GlyphOpacity { get => (double)GetValue(GlyphOpacityProperty); set => SetValue(GlyphOpacityProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    private static readonly Brush WarnFill = ThemeManager.Brush("#FF453A");
    private static readonly Typeface LabelFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI"),
        FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface GlyphFace = new(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    /// <summary>Half of the opening at the bottom of the ring, in radians.</summary>
    private const double BottomHalfGap = 38 * Math.PI / 180;

    private static DependencyProperty Register(string name, Type type, object? fallback) =>
        DependencyProperty.Register(name, type, typeof(Gauge),
            new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender));

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsNaN(Width) ? 36 : Width,
        double.IsNaN(Height) ? 36 : Height);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 12 || h < 12) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var fg = Foreground;
        var fgColor = fg is SolidColorBrush s ? s.Color : Colors.White;
        var size = Math.Min(w, h);
        var stroke = Math.Max(1.6, Math.Round(StrokeFor(size) * dpi.DpiScaleX) / dpi.DpiScaleX);

        // number at the top: its ink is centred on the ring's top point, so the ring opens around it
        FormattedText? label = null;
        var labelInk = Rect.Empty;
        if (!string.IsNullOrEmpty(Label))
        {
            label = new FormattedText(Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelFace,
                LabelSizeFor(size), fg, dpi.PixelsPerDip);
            labelInk = label.BuildGeometry(new Point()).Bounds;
        }

        // the ring is centred in the element (not pushed down under the number), so the centre glyph, a hover
        // circle behind it and neighbouring icons all share one centre line
        var r = size / 2 - (label is null ? stroke / 2 : InsetFor(size, stroke));
        if (r < 4) return;
        var c = new Point(w / 2, h / 2);

        var topHalfGap = label is null ? 0 : Math.Asin(Math.Min(0.9, (labelInk.Width / 2 + stroke / 2 + 2.4) / r));
        var sweep = Math.PI - BottomHalfGap - topHalfGap;  // each side
        var leftStart = Math.PI + BottomHalfGap;            // bottom-left, running up to the top gap
        var rightStart = topHalfGap;                        // top-right, running down to the bottom gap

        var track = new Pen(new SolidColorBrush(Color.FromArgb(0x47, fgColor.R, fgColor.G, fgColor.B)), stroke)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        DrawArc(dc, track, c, r, leftStart, sweep);
        DrawArc(dc, track, c, r, rightStart, sweep);

        var pulse = (double)GetValue(PulseProperty);
        if (pulse is > 0 and < 1)
        {
            var glow = Fill ?? fg;
            var glowColor = glow is SolidColorBrush gs ? gs.Color : fgColor;
            var halo = new Pen(new SolidColorBrush(Color.FromArgb((byte)(150 * (1 - pulse)), glowColor.R, glowColor.G, glowColor.B)),
                stroke * (1 + 2.5 * pulse)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            DrawArc(dc, halo, c, r, leftStart, sweep);
            DrawArc(dc, halo, c, r, rightStart, sweep);
        }

        var value = Math.Clamp((double)GetValue(ShownProperty), 0, 100);
        if (value > 0)
        {
            var brush = Fill ?? (Math.Clamp(Value, 0, 100) <= WarnAt ? WarnFill : fg);
            var pen = new Pen(brush, stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var filled = Math.Max(0.02, value / 100 * sweep * 2);
            DrawArc(dc, pen, c, r, leftStart, Math.Min(filled, sweep));
            if (filled > sweep) DrawArc(dc, pen, c, r, rightStart, Math.Min(filled - sweep, sweep));
        }

        if (label is not null)
            dc.DrawText(label, Snap(new Point(c.X - labelInk.Width / 2 - labelInk.X, c.Y - r - labelInk.Height / 2 - labelInk.Y), dpi));

        if (!string.IsNullOrEmpty(Glyph))
        {
            var glyph = new FormattedText(Glyph, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, GlyphFace,
                Math.Round(size * 0.4), fg, dpi.PixelsPerDip);
            var ink = glyph.BuildGeometry(new Point()).Bounds;
            if (!ink.IsEmpty)
            {
                dc.PushOpacity(Math.Clamp(GlyphOpacity, 0, 1));
                dc.DrawText(glyph, Snap(new Point(c.X - ink.Width / 2 - ink.X, c.Y - ink.Height / 2 - ink.Y), dpi));
                dc.Pop();
            }
        }
    }

    private static double StrokeFor(double size) => Math.Max(1.6, size * 0.065);
    private static double LabelSizeFor(double size) => Math.Max(8, size * 0.28);

    /// <summary>Ring radius = half the size minus this: room for half the number's height above the ring's top.</summary>
    private static double InsetFor(double size, double stroke)
    {
        var digits = new FormattedText("0", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelFace,
            LabelSizeFor(size), Brushes.Black, 1.0);
        return Math.Max(stroke / 2, digits.BuildGeometry(new Point()).Bounds.Height / 2 + 0.5);
    }

    /// <summary>Clear space inside the ring of a labelled gauge of this size (for a hover circle that fits within it).</summary>
    public static double InnerDiameterFor(double size)
    {
        var stroke = StrokeFor(size);
        return 2 * (size / 2 - InsetFor(size, stroke)) - stroke;
    }

    /// <summary>Arc of <paramref name="length"/> radians clockwise from <paramref name="from"/> (0 = 12 o'clock).</summary>
    private static void DrawArc(DrawingContext dc, Pen pen, Point c, double r, double from, double length)
    {
        if (length <= 0) return;
        Point At(double a) => new(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a));
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(At(from), false, false);
            ctx.ArcTo(At(from + length), new Size(r, r), 0, length > Math.PI, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static Point Snap(Point p, DpiScale dpi) => new(
        Math.Round(p.X * dpi.DpiScaleX) / dpi.DpiScaleX,
        Math.Round(p.Y * dpi.DpiScaleY) / dpi.DpiScaleY);
}
