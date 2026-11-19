using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Facet.Interop;

namespace Facet.Glass;

/// <summary>
/// Content-less window that sits exactly behind the dock and draws only its glass (compositor blur +
/// tint, clipped to a smooth rounded rectangle). The dock window itself is transparent and owned by
/// this one, so Windows always keeps the dock directly on top of its glass.
/// </summary>
internal sealed class DockGlassWindow : Window
{
    private const long WS_EX_TRANSPARENT = 0x00000020; // clicks fall through to whatever is underneath
    private CompositionBackdrop? _glass;
    private IntPtr _hwnd;

    public DockGlassWindow()
    {
        Title = "Facet Dock Glass";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Transparent;
        Left = -32000;
        Top = -32000;
        Width = 1;
        Height = 1;
    }

    public IntPtr Handle => _hwnd;
    public bool HasGlass => _glass is not null;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        Native.AddExStyle(_hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | WS_EX_TRANSPARENT);
        HwndSource.FromHwnd(_hwnd)?.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool handled) =>
        {
            if (msg != Native.WM_MOUSEACTIVATE) return IntPtr.Zero;
            handled = true;
            return new IntPtr(Native.MA_NOACTIVATE);
        });
        _glass = CompositionBackdrop.TryCreate(_hwnd);
    }

    /// <summary>Put the glass exactly under <paramref name="dock"/> (physical pixels) and shape/tint it.</summary>
    public void Match(IntPtr dock, bool visible, double radiusPx, Color tint)
    {
        if (_hwnd == IntPtr.Zero || _glass is null || !Native.GetWindowRect(dock, out var r)) return;
        var flags = Native.SWP_NOACTIVATE | SWP_NOZORDER | (visible ? Native.SWP_SHOWWINDOW : SWP_HIDEWINDOW);
        Native.SetWindowPos(_hwnd, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, flags);
        _glass.Update(r.Width, r.Height, radiusPx, tint);
    }

    private const uint SWP_HIDEWINDOW = 0x0080;
    private const uint SWP_NOZORDER = 0x0004;
}
