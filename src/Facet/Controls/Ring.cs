using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Facet.Theme;

namespace Facet.Controls;

/// <summary>Circular progress ring (0..100), drawn directly for minimal visual-tree cost.</summary>
public sealed class Ring : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(Ring), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(Ring), new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(Ring), new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(Ring), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? Track { get => (Brush?)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var t = StrokeThickness;
        var r = (size - t) / 2;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);

        var track = Track ?? (Brush)(TryFindResource("DockHover") ?? Brushes.Gray);
        dc.DrawEllipse(null, new Pen(track, t), c, r, r);

        var v = Math.Clamp(Value, 0, 100);
        if (v <= 0) return;
        var pen = new Pen(Fill, t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (v >= 99.9)
        {
            dc.DrawEllipse(null, pen, c, r, r);
            return;
        }

        var angle = v / 100 * 2 * Math.PI;
        var start = new Point(c.X, c.Y - r);
        var end = new Point(c.X + r * Math.Sin(angle), c.Y - r * Math.Cos(angle));
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(r, r), 0, angle > Math.PI, SweepDirection.Clockwise, true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }
}

/// <summary>Maps a load percentage to green / orange / red.</summary>
public sealed class LoadToBrushConverter : IValueConverter
{
    private static readonly Brush Low = ThemeManager.Brush("#30D158");
    private static readonly Brush Mid = ThemeManager.Brush("#FF9F0A");
    private static readonly Brush High = ThemeManager.Brush("#FF453A");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var v = value is IConvertible c ? c.ToDouble(CultureInfo.InvariantCulture) : 0;
        var inverted = parameter as string == "battery";
        if (inverted) return v <= 15 ? High : v <= 30 ? Mid : Low;
        return v >= 85 ? High : v >= 60 ? Mid : Low;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class NullToCollapsedConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is not null && (value is not string s || s.Length > 0);
        if (Invert) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value is true;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
