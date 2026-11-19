using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Facet.Controls;
using Facet.Infrastructure;
using Facet.Popovers;
using Facet.Services;
using Facet.Theme;

namespace Facet.Widgets;

public partial class MediaWidget : UserControl
{
    private readonly MediaService _media = AppServices.Media;

    // the tile's background: tinted with the album art's colour, strongest behind the art
    private readonly GradientStop _glowNear = new(Colors.Transparent, 0);
    private readonly GradientStop _glowFar = new(Colors.Transparent, 1);

    public MediaWidget()
    {
        InitializeComponent();
        DataContext = _media;
        UpdateGlyph();
        _media.PropertyChanged += OnChanged;
        AppServices.Settings.Changed += OnSettingsChanged;
        Loaded += (_, _) => Glow(animate: false);
        Unloaded += (_, _) =>
        {
            _media.PropertyChanged -= OnChanged;
            AppServices.Settings.Changed -= OnSettingsChanged;
        };
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaService.IsPlaying)) UpdateGlyph();
        if (e.PropertyName == nameof(MediaService.Thumbnail)) Glow(animate: IsLoaded);
    }

    private void OnSettingsChanged() => Glow(animate: false); // the theme decides how strong the tint is

    /// <summary>
    /// Tint the tile with the album art's colour (a gradient from behind the art to the far end), crossfading
    /// from the previous track's; no art: the plain tile.
    /// </summary>
    private void Glow(bool animate)
    {
        var color = ImageColor.Of(_media.Thumbnail);
        if (color is null)
        {
            Btn.SetResourceReference(BackgroundProperty, "DockTile");
            return;
        }

        var dark = !ThemeManager.UsesDarkText(ThemeManager.Current);
        var c = color.Value;
        var near = Color.FromArgb(dark ? (byte)0x70 : (byte)0x5C, c.R, c.G, c.B);
        var far = Color.FromArgb(dark ? (byte)0x1C : (byte)0x14, c.R, c.G, c.B);

        if (Btn.Background is not LinearGradientBrush brush || !brush.GradientStops.Contains(_glowNear))
        {
            // coming from the plain tile: start the fade from its colour
            var from = (Btn.Background as SolidColorBrush)?.Color ?? Colors.Transparent;
            Set(_glowNear, from);
            Set(_glowFar, from);
            Btn.Background = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5), GradientStops = { _glowNear, _glowFar } };
        }
        Fade(_glowNear, near, animate);
        Fade(_glowFar, far, animate);
    }

    private static void Set(GradientStop stop, Color color)
    {
        stop.BeginAnimation(GradientStop.ColorProperty, null);
        stop.Color = color;
    }

    private static void Fade(GradientStop stop, Color to, bool animate)
    {
        if (!animate || !Motion.Enabled)
        {
            Set(stop, to);
            return;
        }
        stop.BeginAnimation(GradientStop.ColorProperty,
            new ColorAnimation(to, TimeSpan.FromMilliseconds(500)) { EasingFunction = Motion.EaseInOut });
    }

    private void UpdateGlyph() => PlayPause.Content = _media.IsPlaying ? "" : "";

    private void PlayPause_Click(object sender, RoutedEventArgs e) => _ = _media.PlayPauseAsync();

    private void Btn_Click(object sender, RoutedEventArgs e) => PopoverWindow.Toggle(Btn, () => new MediaPanel());
}
