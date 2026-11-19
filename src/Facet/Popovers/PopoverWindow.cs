using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using Facet.Core.Settings;
using Facet.Glass;
using Facet.Infrastructure;
using Facet.Interop;
using Facet.Theme;

namespace Facet.Popovers;

/// <summary>
/// Acrylic panel that opens from a dock element (calendar, todos, media…). Closes on deactivation or Esc.
/// Only one is open at a time; clicking the same anchor again closes it.
/// </summary>
public class PopoverWindow : Window
{
    private static PopoverWindow? _current;
    private static (FrameworkElement? Anchor, DateTime At) _lastClosed;

    private readonly FrameworkElement? _anchor;
    private readonly Border _frame;
    private readonly ScaleTransform _scale = new(1, 1);
    private bool _closing;
    private bool _exitDone;
    private bool _closed;

    public static bool IsOpen => _current is not null;

    /// <summary>Raised when a panel opens or closes.</summary>
    public static event Action? CurrentChanged;

    public static bool IsOpenFor(FrameworkElement anchor) => _current is { _closing: false } open && ReferenceEquals(open._anchor, anchor);

    public static void Toggle(FrameworkElement anchor, Func<FrameworkElement> content)
    {
        if (_current is { } open)
        {
            var same = ReferenceEquals(open._anchor, anchor);
            open.Close();
            if (same) return;
        }

        // the click that deactivated (and closed) the popover also lands on its anchor: don't reopen it
        if (ReferenceEquals(_lastClosed.Anchor, anchor) && (DateTime.Now - _lastClosed.At).TotalMilliseconds < 400) return;

        var window = new PopoverWindow(anchor, content());
        _current = window;
        window.Show();
        window.Activate();
        window.EnsureActivated();
        CurrentChanged?.Invoke();
    }

    /// <summary>
    /// A shell surface that's still closing (Start, Search) can swallow the first activation; retry
    /// briefly so the panel ends up in front with keyboard focus instead of stranded behind it.
    /// </summary>
    private void EnsureActivated()
    {
        var attempts = 0;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        timer.Tick += (_, _) =>
        {
            if (_closing || IsActive || ++attempts > 6)
            {
                timer.Stop();
                return;
            }
            Native.ActivateWindow(new WindowInteropHelper(this).Handle);
            Activate();
        };
        timer.Start();
    }

    public static void CloseCurrent() => _current?.Close();

    protected PopoverWindow(FrameworkElement? anchor, FrameworkElement content, bool activates = true)
    {
        _anchor = anchor;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = activates;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;
        UseLayoutRounding = true;
        FontFamily = (FontFamily)FindResource("UiFont");
        FontSize = 13;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            GlassFrameThickness = new Thickness(-1),
            ResizeBorderThickness = new Thickness(0),
            UseAeroCaptionButtons = false,
            CornerRadius = new CornerRadius(0),
        });

        _frame = new Border
        {
            Padding = new Thickness(16),
            Child = content,
            Opacity = 0,
            RenderTransform = _scale,
        };
        _frame.SetResourceReference(Border.BackgroundProperty, "SurfaceBackground");
        _frame.SetResourceReference(TextElement.ForegroundProperty, "SurfaceForeground");
        Content = _frame;

        Loaded += (_, _) => Appear();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.AddExStyle(hwnd, Native.WS_EX_TOOLWINDOW);
        var dark = !ThemeManager.IsLightSurface(ThemeManager.Current);
        if (!Backdrop.TryApply(this, dark)) _frame.SetResourceReference(Border.BackgroundProperty, "SurfaceSolid");
    }

    protected virtual void Position()
    {
        if (_anchor is null || PresentationSource.FromVisual(_anchor) is null) return;
        var dpi = VisualTreeHelper.GetDpi(_anchor);
        var origin = _anchor.PointToScreen(new Point(0, 0));
        double ax = origin.X / dpi.DpiScaleX, ay = origin.Y / dpi.DpiScaleY;
        var monitor = Native.GetPrimaryMonitor().rcMonitor;
        double monLeft = monitor.Left / dpi.DpiScaleX, monRight = monitor.Right / dpi.DpiScaleX;

        var left = ax + _anchor.ActualWidth / 2 - ActualWidth / 2;
        Left = Math.Clamp(left, monLeft + 10, Math.Max(monLeft + 10, monRight - ActualWidth - 10));
        Top = AppServices.Settings.Current.Edge == DockEdge.Top
            ? ay + _anchor.ActualHeight + 18
            : ay - ActualHeight - 18;
    }

    private const double AppearMs = 280;
    private const double ExitMs = 120;

    private static bool FromBelow => AppServices.Settings.Current.Edge != DockEdge.Top;

    /// <summary>
    /// Rise out of the dock and grow from the element that opened it. The acrylic is drawn by Windows for the
    /// whole window and can't fade, so the window itself slides while its content fades and scales.
    /// </summary>
    private void Appear()
    {
        Position();
        var rest = Top;
        _frame.RenderTransformOrigin = new Point(AnchorFraction(), FromBelow ? 1 : 0);
        Motion.To(_frame, OpacityProperty, 1, Motion.Fast);
        Motion.To(_scale, ScaleTransform.ScaleXProperty, 1, AppearMs, Motion.Spring, from: 0.94);
        Motion.To(_scale, ScaleTransform.ScaleYProperty, 1, AppearMs, Motion.Spring, from: 0.94);
        Motion.To(this, TopProperty, rest, AppearMs, Motion.Spring, from: rest + (FromBelow ? 10 : -10), done: () =>
        {
            BeginAnimation(TopProperty, null);
            if (!_closing) Position();
        });
    }

    /// <summary>Where the anchor's centre falls across this window (0..1), so the panel grows out of it.</summary>
    private double AnchorFraction()
    {
        if (_anchor is null || PresentationSource.FromVisual(_anchor) is null || ActualWidth <= 0) return 0.5;
        var dpi = VisualTreeHelper.GetDpi(_anchor);
        var x = _anchor.PointToScreen(new Point(_anchor.ActualWidth / 2, 0)).X / dpi.DpiScaleX;
        return Math.Clamp((x - Left) / ActualWidth, 0.1, 0.9);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (ShowActivated && !_closing) Close();
    }

    /// <summary>The first Close plays the exit (sink toward the dock, fade, shrink a little), then really closes.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_exitDone && IsVisible && Motion.Enabled && !Dispatcher.HasShutdownStarted)
        {
            e.Cancel = true;
            if (_closing) return; // already on its way out
            _closing = true;
            if (ReferenceEquals(_current, this)) CurrentChanged?.Invoke();
            Motion.To(_frame, OpacityProperty, 0, ExitMs, Motion.EaseIn);
            Motion.To(_scale, ScaleTransform.ScaleXProperty, 0.97, ExitMs, Motion.EaseIn);
            Motion.To(_scale, ScaleTransform.ScaleYProperty, 0.97, ExitMs, Motion.EaseIn);
            Motion.To(this, TopProperty, Top + (FromBelow ? 6 : -6), ExitMs, Motion.EaseIn, done: () =>
            {
                _exitDone = true;
                if (!_closed) Close();
            });
            return;
        }
        _closing = true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        base.OnClosed(e);
        if (ReferenceEquals(_current, this))
        {
            _current = null;
            _lastClosed = (_anchor, DateTime.Now);
            CurrentChanged?.Invoke();
        }
    }

    /// <summary>Re-run placement after content size changes.</summary>
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (IsLoaded) Position();
    }
}
