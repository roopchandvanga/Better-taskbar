using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Facet.Controls;

/// <summary>
/// An icon-font glyph centred by its visible ink rather than its font box, the same way <see cref="Gauge"/>
/// centres its glyph, so a row of icons (and the round hover circles behind them) share one centre line.
/// </summary>
public sealed class GlyphIcon : FrameworkElement
{
    private static readonly Typeface Face = new(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(string),
        typeof(GlyphIcon), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(GlyphIcon),
        new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(GlyphIcon),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsNaN(Width) ? FontSize : Width,
        double.IsNaN(Height) ? FontSize : Height);

    protected override void OnRender(DrawingContext dc)
    {
        if (string.IsNullOrEmpty(Glyph)) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var text = new FormattedText(Glyph, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, FontSize,
            Foreground, dpi.PixelsPerDip);
        var ink = text.BuildGeometry(new Point()).Bounds;
        if (ink.IsEmpty) return;
        var x = ActualWidth / 2 - ink.Width / 2 - ink.X;
        var y = ActualHeight / 2 - ink.Height / 2 - ink.Y;
        dc.DrawText(text, new Point(Math.Round(x * dpi.DpiScaleX) / dpi.DpiScaleX, Math.Round(y * dpi.DpiScaleY) / dpi.DpiScaleY));
    }
}
