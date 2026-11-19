using System.Windows;
using System.Windows.Media;
using Facet.Services;
using Facet.Theme;

namespace Facet.Controls;

/// <summary>Colour vector weather icons (WPF can't render colour emoji fonts).</summary>
public sealed class WeatherIcon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(WeatherKind), typeof(WeatherIcon), new FrameworkPropertyMetadata(WeatherKind.Clear, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsDayProperty = DependencyProperty.Register(
        nameof(IsDay), typeof(bool), typeof(WeatherIcon), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public WeatherKind Kind { get => (WeatherKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public bool IsDay { get => (bool)GetValue(IsDayProperty); set => SetValue(IsDayProperty, value); }

    private static readonly Brush Sun = ThemeManager.Brush("#FFD60A");
    private static readonly Brush SunCore = ThemeManager.Brush("#FFB800");
    private static readonly Brush Moon = ThemeManager.Brush("#E5E5EA");
    private static readonly Brush CloudLight = ThemeManager.Brush("#F2F2F7");
    private static readonly Brush CloudGrey = ThemeManager.Brush("#C7C7CC");
    private static readonly Brush CloudDark = ThemeManager.Brush("#8E8E93");
    private static readonly Brush Rain = ThemeManager.Brush("#64D2FF");
    private static readonly Brush Snow = ThemeManager.Brush("#FFFFFF");
    private static readonly Brush Bolt = ThemeManager.Brush("#FFD60A");
    private static readonly Brush Outline = ThemeManager.Brush("#33000000");

    protected override void OnRender(DrawingContext dc)
    {
        var s = Math.Min(ActualWidth, ActualHeight);
        if (s <= 0) return;
        dc.PushTransform(new TranslateTransform((ActualWidth - s) / 2, (ActualHeight - s) / 2));
        dc.PushTransform(new ScaleTransform(s / 24.0, s / 24.0));

        switch (Kind)
        {
            case WeatherKind.Clear:
                if (IsDay) DrawSun(dc, 12, 12, 5); else DrawMoon(dc, 12, 12, 7);
                break;
            case WeatherKind.PartlyCloudy:
                if (IsDay) DrawSun(dc, 15.5, 8.5, 3.6); else DrawMoon(dc, 15.5, 8.5, 5);
                DrawCloud(dc, 1.5, 9, CloudLight);
                break;
            case WeatherKind.Cloudy:
                DrawCloud(dc, 4.5, 5.5, CloudGrey, 0.75);
                DrawCloud(dc, 1.5, 8.5, CloudLight);
                break;
            case WeatherKind.Fog:
                var fogPen = new Pen(CloudGrey, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(fogPen, new Point(4, 8), new Point(20, 8));
                dc.DrawLine(fogPen, new Point(2, 12.5), new Point(18, 12.5));
                dc.DrawLine(fogPen, new Point(6, 17), new Point(22, 17));
                break;
            case WeatherKind.Drizzle:
                DrawCloud(dc, 1.5, 3, CloudLight);
                foreach (var x in new[] { 7.0, 12.0, 17.0 }) dc.DrawEllipse(Rain, null, new Point(x, 19.5), 1.2, 1.2);
                break;
            case WeatherKind.Rain:
                DrawCloud(dc, 1.5, 3, CloudGrey);
                var rainPen = new Pen(Rain, 1.7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                foreach (var x in new[] { 7.5, 12.0, 16.5 }) dc.DrawLine(rainPen, new Point(x, 17), new Point(x - 1.6, 21.5));
                break;
            case WeatherKind.Snow:
                DrawCloud(dc, 1.5, 3, CloudLight);
                foreach (var (x, y) in new[] { (7.0, 18.5), (12.0, 20.5), (17.0, 18.5) })
                {
                    dc.DrawEllipse(Snow, new Pen(Outline, 0.4), new Point(x, y), 1.6, 1.6);
                }
                break;
            case WeatherKind.Thunder:
                DrawCloud(dc, 1.5, 2.5, CloudDark);
                var bolt = Geometry.Parse("M 12.5,13 L 9,19 H 11.8 L 10.5,23.5 L 15.5,16.8 H 12.6 L 14,13 Z");
                dc.DrawGeometry(Bolt, null, bolt);
                break;
        }

        dc.Pop();
        dc.Pop();
    }

    private static void DrawSun(DrawingContext dc, double cx, double cy, double r)
    {
        var rayPen = new Pen(Sun, r * 0.32) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        for (var i = 0; i < 8; i++)
        {
            var a = i * Math.PI / 4;
            dc.DrawLine(rayPen,
                new Point(cx + Math.Cos(a) * r * 1.45, cy + Math.Sin(a) * r * 1.45),
                new Point(cx + Math.Cos(a) * r * 1.9, cy + Math.Sin(a) * r * 1.9));
        }
        var grad = new RadialGradientBrush(((SolidColorBrush)Sun).Color, ((SolidColorBrush)SunCore).Color);
        dc.DrawEllipse(grad, null, new Point(cx, cy), r, r);
    }

    private static void DrawMoon(DrawingContext dc, double cx, double cy, double r)
    {
        var full = new EllipseGeometry(new Point(cx, cy), r, r);
        var cut = new EllipseGeometry(new Point(cx + r * 0.55, cy - r * 0.35), r * 0.85, r * 0.85);
        dc.DrawGeometry(Moon, null, new CombinedGeometry(GeometryCombineMode.Exclude, full, cut));
    }

    private static void DrawCloud(DrawingContext dc, double x, double y, Brush fill, double scale = 1)
    {
        var g = new GeometryGroup { FillRule = FillRule.Nonzero };
        g.Children.Add(new EllipseGeometry(new Point(x + 6 * scale, y + 9 * scale), 4.5 * scale, 4.5 * scale));
        g.Children.Add(new EllipseGeometry(new Point(x + 11.5 * scale, y + 6.5 * scale), 6 * scale, 6 * scale));
        g.Children.Add(new EllipseGeometry(new Point(x + 16.5 * scale, y + 9.5 * scale), 4 * scale, 4 * scale));
        g.Children.Add(new RectangleGeometry(new Rect(x + 6 * scale, y + 8 * scale, 10.5 * scale, 5.5 * scale), 2, 2));
        var flat = g.GetFlattenedPathGeometry();
        dc.DrawGeometry(fill, new Pen(Outline, 0.5), flat);
    }
}
