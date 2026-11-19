using System.Windows;
using System.Windows.Media;
using Facet.Core.Settings;

namespace Facet.Theme;

/// <summary>Pushes theme brushes and size metrics into Application resources (consumed via DynamicResource).</summary>
internal static class ThemeManager
{
    private static ResourceDictionary R => Application.Current.Resources;

    /// <summary>Physical pixel size app icons are drawn at (and requested from the shell at).</summary>
    public static int IconPixels { get; private set; } = 40;

    /// <summary>
    /// Sizes snap to whole physical pixels for the monitor's scale, so icons are drawn 1:1 instead of
    /// being resampled to fractional sizes (e.g. 37.5 px at 125 %), which is what made them look soft.
    /// </summary>
    public static void ApplySize(DockSize size, double scale = 1.0)
    {
        if (scale <= 0) scale = 1;
        var item = size switch { DockSize.Small => 40.0, DockSize.Large => 56.0, _ => 48.0 };
        int Px(double dip)
        {
            var p = (int)Math.Round(dip * scale);
            return p % 2 == 1 ? p + 1 : p; // even, so centring stays on whole pixels
        }

        var itemPx = Px(item);
        // a standard icon size closest to ~2/3 of the tile, leaving room for the running indicator below it
        int[] standard = [24, 32, 40, 48, 64, 80, 96, 128];
        var iconPx = standard.OrderBy(s => Math.Abs(s - itemPx * 0.66)).First();
        IconPixels = iconPx;
        R["ItemSize"] = itemPx / scale;
        R["IconSize"] = iconPx / scale;
        // the Start logo is a solid square, which reads bigger than a round or folder icon of the same width:
        // 90 % of the app-icon size gives it the same visual weight as the icons beside it
        var logoPx = (int)Math.Round(iconPx * 0.9);
        if (logoPx % 2 == 1) logoPx++;
        R["StartLogoSize"] = logoPx / scale;
        // icons stay centred; the dot / active line sits at the very bottom of the tile, below the icon
        var dotPx = Math.Max(3, (int)Math.Round(3.2 * scale));  // running dot: 4 px at 125 %
        var barPx = Math.Max(2, (int)Math.Round(2.4 * scale));  // active bar: 3 px tall
        R["DotSize"] = dotPx / scale;
        R["DotRadius"] = new CornerRadius(dotPx / scale / 2);
        R["BarWidth"] = Math.Round(14 * scale) / scale;
        R["BarHeight"] = barPx / scale;
        R["BarRadius"] = new CornerRadius(barPx / scale / 2);
        R["DotGap"] = new Thickness(0, 0, 0, Math.Max(1, (int)Math.Round(1.6 * scale)) / scale);
        R["AppSpacing"] = new Thickness(Px(3) / scale, 0, Px(3) / scale, 0);
        R["RingSize"] = Px(item * 0.76) / scale;
        var gauge = Px(item * 0.8) / scale;
        R["GaugeSize"] = gauge;
        // width of each plain icon button in the connect tile: about the clear space inside the gauge's ring
        // plus a small gap, so the icons are spaced like the Wi-Fi icon sits in its ring
        var slotPx = (int)Math.Floor((Controls.Gauge.InnerDiameterFor(gauge) - 1.5) * scale) + Px(3);
        if (slotPx % 2 == 1) slotPx--; // even, so icons centre on whole pixels
        R["IconSlot"] = slotPx / scale;
        R["ThumbSize"] = Px(item * 0.72) / scale;
        R["DockCorner"] = new CornerRadius(Math.Round((item + 12) * 0.3)); // ≈ macOS dock proportions
        R["DockCornerPx"] = (int)Math.Round((item + 12) * 0.3 * scale);
        R["TileCorner"] = new CornerRadius(Math.Round(item * 0.24));
        R["DockFont"] = size switch { DockSize.Small => 11.5, DockSize.Large => 13.5, _ => 12.5 };
        R["DockSmallFont"] = size switch { DockSize.Small => 10.0, DockSize.Large => 11.5, _ => 10.5 };
        R["DockBigFont"] = size switch { DockSize.Small => 13.0, DockSize.Large => 16.0, _ => 14.5 };
    }

    public static bool UsesDarkText(DockTheme theme) => theme is DockTheme.Frost or DockTheme.White;

    /// <summary>
    /// Tint laid over the live blur. Alpha decides how much of what's behind shows through:
    /// the glass themes are mostly see-through, the solid themes barely.
    /// </summary>
    public static Color DockTint(DockTheme theme) => theme switch
    {
        DockTheme.LiquidGlass => Color.FromArgb(0x70, 0x1E, 0x1E, 0x22),
        DockTheme.Frost => Color.FromArgb(0x8C, 0xF4, 0xF4, 0xF7),
        DockTheme.Midnight => Color.FromArgb(0xE0, 0x14, 0x1B, 0x2A),
        DockTheme.Black => Color.FromArgb(0xEE, 0x0B, 0x0B, 0x0C),
        _ => Color.FromArgb(0xE8, 0xF7, 0xF7, 0xF9),
    };

    /// <summary>Dock brushes for text, tiles and hover, readable on the theme's tint.</summary>
    public static void ApplyDock(DockTheme theme)
    {
        if (UsesDarkText(theme))
        {
            Set("DockForeground", "#1C1C1E");
            Set("DockSubtle", "#8C1C1C1E");
            Set("DockTile", "#0F000000");
            Set("DockHover", "#1A000000");
            Set("DockDivider", "#26000000");
            R["StartLogo"] = LogoGradient("#2A9BF5", "#0058C4");
        }
        else
        {
            Set("DockForeground", "#FFFFFF");
            Set("DockSubtle", "#B3FFFFFF");
            Set("DockTile", "#14FFFFFF");
            Set("DockHover", "#24FFFFFF");
            Set("DockDivider", "#33FFFFFF");
            R["StartLogo"] = LogoGradient("#7FD7FF", "#2F9BF0");
        }

        // used only when the compositor can't blur (e.g. transparency effects turned off)
        var tint = DockTint(theme);
        R["DockSolid"] = Frozen(new SolidColorBrush(Color.FromArgb(0xF2, tint.R, tint.G, tint.B)));
    }

    /// <summary>Popover / palette / toast surfaces. Translucent when DWM acrylic sits behind them.</summary>
    public static void ApplySurface(DockTheme theme)
    {
        var light = theme == DockTheme.White || theme == DockTheme.Frost;
        if (light)
        {
            Set("SurfaceBackground", "#99F6F6F8");
            Set("SurfaceSolid", "#FAF6F6F8");
            Set("SurfaceForeground", "#1C1C1E");
            Set("SurfaceSubtle", "#8A3C3C43");
            Set("SurfaceCard", "#0D000000");
            Set("SurfaceCardStrong", "#17000000");
            Set("SurfaceBorder", "#1A000000");
            Set("SurfaceHover", "#12000000");
            Set("SurfaceInput", "#0F000000");
            Set("MenuBackground", "#FAF9F9FB");
            Set("MenuForeground", "#1C1C1E");
            Set("MenuHover", "#14000000");
            Set("SettingsBackground", "#FFF3F3F5");
        }
        else
        {
            Set("SurfaceBackground", "#8C1C1C1E");
            Set("SurfaceSolid", "#F71C1C1E");
            Set("SurfaceForeground", "#F5F5F7");
            Set("SurfaceSubtle", "#A3EBEBF5");
            Set("SurfaceCard", "#14FFFFFF");
            Set("SurfaceCardStrong", "#24FFFFFF");
            Set("SurfaceBorder", "#24FFFFFF");
            Set("SurfaceHover", "#1FFFFFFF");
            Set("SurfaceInput", "#1AFFFFFF");
            Set("MenuBackground", "#FA2A2A2D");
            Set("MenuForeground", "#F5F5F7");
            Set("MenuHover", "#24FFFFFF");
            Set("SettingsBackground", "#FF1C1C1E");
        }

        Set("Accent", "#0A84FF");
        Set("AccentSoft", "#330A84FF");
        Set("Good", "#30D158");
        Set("Warn", "#FF9F0A");
        Set("Bad", "#FF453A");
        Set("Focus", "#FF6B5E");
        Set("OnAccent", "#FFFFFF");
    }

    public static bool IsLightSurface(DockTheme theme) => theme is DockTheme.White or DockTheme.Frost;

    public static SolidColorBrush Brush(string hex) =>
        Frozen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)));

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private static void Set(string key, string hex) => R[key] = Brush(hex);

    /// <summary>Top-left light to bottom-right deep blue, like the Windows 11 logo.</summary>
    private static LinearGradientBrush LogoGradient(string from, string to) => Frozen(new LinearGradientBrush(
        (Color)ColorConverter.ConvertFromString(from), (Color)ColorConverter.ConvertFromString(to), new Point(0, 0), new Point(1, 1)));
}
