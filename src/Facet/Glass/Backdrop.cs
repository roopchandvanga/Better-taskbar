using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Facet.Interop;

namespace Facet.Glass;

/// <summary>
/// System acrylic for Facet's activatable surfaces (popovers, palette, toasts). The dock's own glass is
/// drawn by <see cref="DockGlassWindow"/> / <see cref="CompositionBackdrop"/>.
/// </summary>
internal static class Backdrop
{
    private const int DWMWCP_ROUND = 2;

    public static bool IsSupported => Environment.OSVersion.Version.Build >= 22621;

    public static bool TryApply(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return false;

        var darkValue = dark ? 1 : 0;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkValue, sizeof(int));
        var corner = DWMWCP_ROUND;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        if (!IsSupported) return false;
        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget is null) return false;
        source.CompositionTarget.BackgroundColor = Colors.Transparent;
        var margins = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        Native.DwmExtendFrameIntoClientArea(hwnd, ref margins);
        var backdrop = 3; // DWMSBT_TRANSIENTWINDOW (acrylic)
        return Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) == 0;
    }
}
