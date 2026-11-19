using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Facet.Apps;
using Facet.Core.Settings;
using Facet.Glass;
using Facet.Infrastructure;
using Facet.Interop;
using Facet.Theme;

namespace Facet.Shell;

/// <summary>
/// Live previews of a running app's windows above its dock icon, like the Windows taskbar: each window is a
/// card with its title, a close button and a real-time thumbnail that the compositor (DWM) draws into this
/// window. Click a card to switch to that window; × or a middle-click closes it. Never takes focus.
/// </summary>
internal sealed class WindowPreview : Window
{
    private const double MaxThumbWidth = 220;
    private const double MaxThumbHeight = 140;
    private const int MaxWindows = 6;

    private static WindowPreview? _current;

    private readonly AppItem _item;
    private readonly FrameworkElement _anchor;
    private readonly Border _frame;
    private readonly StackPanel _cards = new() { Orientation = Orientation.Horizontal };
    private readonly List<Card> _thumbs = [];
    private IntPtr _hwnd;
    private bool _closing;

    private sealed record Card(IntPtr Source, IntPtr Thumb, FrameworkElement Root, Border Slot);

    private static bool DockedAtTop => AppServices.Settings?.Current.Edge == DockEdge.Top;

    public static bool IsOpen => _current is not null;
    public static AppItem? ShownFor => _current?._item;

    /// <summary>The cursor is over the preview (screen pixels), so it should stay.</summary>
    public static bool Contains(Native.POINT p) =>
        _current is { _hwnd: var h } && h != IntPtr.Zero && Native.GetWindowRect(h, out var r)
        && p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;

    public static void ShowFor(AppItem item, FrameworkElement anchor)
    {
        if (ReferenceEquals(_current?._item, item)) return;
        _current?.CloseNow();
        var windows = item.Windows.Where(Native.IsWindow).Take(MaxWindows).ToList();
        if (windows.Count == 0) return;
        _current = new WindowPreview(item, anchor, windows);
        _current.Show();
    }

    public static void HideCurrent() => _current?.CloseNow();

    private readonly List<IntPtr> _sources;

    private WindowPreview(AppItem item, FrameworkElement anchor, List<IntPtr> sources)
    {
        _item = item;
        _anchor = anchor;
        _sources = sources;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;
        UseLayoutRounding = true;
        FontFamily = (FontFamily)FindResource("UiFont");
        FontSize = 12;
        Left = -32000;
        Top = -32000;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            GlassFrameThickness = new Thickness(-1),
            ResizeBorderThickness = new Thickness(0),
            UseAeroCaptionButtons = false,
        });

        _frame = new Border { Padding = new Thickness(8), Child = _cards, Opacity = 0 };
        _frame.SetResourceReference(Border.BackgroundProperty, "SurfaceBackground");
        _frame.SetResourceReference(TextElement.ForegroundProperty, "SurfaceForeground");
        Content = _frame;
        Loaded += (_, _) => Appear();
        SizeChanged += (_, _) => { Position(); PlaceThumbs(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        Native.AddExStyle(_hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        HwndSource.FromHwnd(_hwnd)?.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool handled) =>
        {
            if (msg != Native.WM_MOUSEACTIVATE) return IntPtr.Zero;
            handled = true;
            return new IntPtr(Native.MA_NOACTIVATE);
        });
        if (!Backdrop.TryApply(this, !ThemeManager.IsLightSurface(ThemeManager.Current)))
            _frame.SetResourceReference(Border.BackgroundProperty, "SurfaceSolid");

        // thumbnails need this window's handle: register them now, sized to each window's shape
        foreach (var source in _sources)
        {
            if (DwmRegisterThumbnail(_hwnd, source, out var thumb) != 0) continue;
            var size = DwmQueryThumbnailSourceSize(thumb, out var s) == 0 && s.cx > 0 && s.cy > 0
                ? new Size(s.cx, s.cy)
                : new Size(16, 10);
            var scale = Math.Min(MaxThumbWidth / size.Width, MaxThumbHeight / size.Height);
            var (w, h) = (Math.Max(60, Math.Round(size.Width * scale)), Math.Max(40, Math.Round(size.Height * scale)));
            _thumbs.Add(BuildCard(source, thumb, w, h));
        }
        if (_thumbs.Count == 0) Dispatcher.BeginInvoke(CloseNow);
    }

    private Card BuildCard(IntPtr source, IntPtr thumb, double w, double h)
    {
        var slot = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(4) };
        slot.SetResourceReference(Border.BackgroundProperty, "SurfaceCard");

        var title = new TextBlock
        {
            Text = Native.GetWindowTitle(source) is { Length: > 0 } t ? t : _item.Name,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 6, 0),
        };
        var close = new Button
        {
            Content = "",
            Style = (Style)FindResource("SurfaceGlyphButton"),
            Width = 22,
            Height = 22,
            FontSize = 9,
            ToolTip = "Close window",
            Opacity = 0,
        };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6), Width = w };
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(title);

        var body = new StackPanel();
        body.Children.Add(head);
        body.Children.Add(slot);

        var hover = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8), Margin = new Thickness(2, 0, 2, 0),
            Background = Brushes.Transparent, Child = body, Cursor = Cursors.Hand };
        hover.MouseEnter += (_, _) => { hover.SetResourceReference(Border.BackgroundProperty, "SurfaceHover"); close.Opacity = 1; };
        hover.MouseLeave += (_, _) => { hover.Background = Brushes.Transparent; close.Opacity = 0; };
        hover.MouseLeftButtonUp += (_, e) =>
        {
            if (e.Handled) return;
            Native.ActivateWindow(source);
            CloseNow();
        };
        hover.MouseUp += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            CloseSource(source);
            e.Handled = true;
        };
        // handled here, so the release doesn't also count as a click on the card (which would switch to it)
        close.PreviewMouseLeftButtonUp += (_, e) => { CloseSource(source); e.Handled = true; };

        _cards.Children.Add(hover);
        return new Card(source, thumb, hover, slot);
    }

    /// <summary>Ask the window to close (it may prompt to save) and drop its card.</summary>
    private void CloseSource(IntPtr source)
    {
        Native.PostMessage(source, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        var card = _thumbs.FirstOrDefault(c => c.Source == source);
        if (card is null) return;
        DwmUnregisterThumbnail(card.Thumb);
        _thumbs.Remove(card);
        _cards.Children.Remove(card.Root);
        if (_thumbs.Count == 0) CloseNow();
    }

    /// <summary>Above the icon (below it when docked at the top), centred on it, inside the screen.</summary>
    private void Position()
    {
        if (PresentationSource.FromVisual(_anchor) is null || ActualWidth <= 0 || Window.GetWindow(_anchor) is not { } dock) return;
        var dpi = VisualTreeHelper.GetDpi(_anchor);
        var origin = _anchor.PointToScreen(new Point(0, 0));
        double ax = origin.X / dpi.DpiScaleX, ay = origin.Y / dpi.DpiScaleY;
        var monitor = Native.GetMonitorFor(new WindowInteropHelper(dock).Handle).rcMonitor;
        double monLeft = monitor.Left / dpi.DpiScaleX, monRight = monitor.Right / dpi.DpiScaleX;
        Left = Math.Clamp(ax + _anchor.ActualWidth / 2 - ActualWidth / 2, monLeft + 8, Math.Max(monLeft + 8, monRight - ActualWidth - 8));
        Top = DockedAtTop ? ay + _anchor.ActualHeight + 14 : ay - ActualHeight - 14;
    }

    /// <summary>Tell the compositor where in this window to draw each thumbnail (physical pixels).</summary>
    private void PlaceThumbs(byte opacity = 255)
    {
        if (_hwnd == IntPtr.Zero || PresentationSource.FromVisual(this)?.CompositionTarget is not { } target) return;
        var toDevice = target.TransformToDevice;
        foreach (var card in _thumbs)
        {
            if (!IsAncestorOf(card.Slot)) continue;
            var topLeft = toDevice.Transform(card.Slot.TransformToAncestor(this).Transform(new Point()));
            var bottomRight = toDevice.Transform(card.Slot.TransformToAncestor(this).Transform(new Point(card.Slot.ActualWidth, card.Slot.ActualHeight)));
            var props = new DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY,
                rcDestination = new Native.RECT
                {
                    Left = (int)Math.Round(topLeft.X), Top = (int)Math.Round(topLeft.Y),
                    Right = (int)Math.Round(bottomRight.X), Bottom = (int)Math.Round(bottomRight.Y),
                },
                fVisible = true,
                opacity = opacity,
                fSourceClientAreaOnly = false,
            };
            DwmUpdateThumbnailProperties(card.Thumb, ref props);
        }
    }

    /// <summary>Rise into place like the dock's panels; thumbnails (drawn by DWM, not WPF) fade in alongside.</summary>
    private void Appear()
    {
        Position();
        PlaceThumbs(0);
        var rest = Top;
        var fromBelow = !DockedAtTop;
        Motion.To(_frame, OpacityProperty, 1, Motion.Fast);
        Motion.To(this, TopProperty, rest, Motion.Normal, Motion.EaseOut, from: rest + (fromBelow ? 8 : -8), done: () =>
        {
            BeginAnimation(TopProperty, null);
            if (!_closing) Position();
        });
        if (!Motion.Enabled)
        {
            PlaceThumbs();
            return;
        }
        var started = DateTime.UtcNow;
        EventHandler? fade = null;
        fade = (_, _) =>
        {
            var t = Math.Min(1, (DateTime.UtcNow - started).TotalMilliseconds / 180);
            if (_closing) t = 1;
            PlaceThumbs((byte)(255 * t));
            if (t >= 1) CompositionTarget.Rendering -= fade;
        };
        CompositionTarget.Rendering += fade;
    }

    private void CloseNow()
    {
        if (_closing) return;
        _closing = true;
        foreach (var card in _thumbs) DwmUnregisterThumbnail(card.Thumb);
        _thumbs.Clear();
        if (ReferenceEquals(_current, this)) _current = null;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (ReferenceEquals(_current, this)) _current = null;
    }

    // ───────────── DWM thumbnails ─────────────

    private const int DWM_TNP_RECTDESTINATION = 0x1;
    private const int DWM_TNP_VISIBLE = 0x8;
    private const int DWM_TNP_OPACITY = 0x4;
    private const int DWM_TNP_SOURCECLIENTAREAONLY = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_THUMBNAIL_PROPERTIES
    {
        public int dwFlags;
        public Native.RECT rcDestination;
        public Native.RECT rcSource;
        public byte opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
        [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(IntPtr thumb);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref DWM_THUMBNAIL_PROPERTIES props);
    [DllImport("dwmapi.dll")] private static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out Native.SIZE size);
}
