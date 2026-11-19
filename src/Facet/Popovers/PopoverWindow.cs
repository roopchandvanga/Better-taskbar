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
    private readonly TranslateTransform _slide = new();
    private bool _closing;

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
            RenderTransform = _slide,
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
        var dark = !ThemeManager.IsLightSurface(AppServices.Settings.Current.Theme);
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

    private void Appear()
    {
        Position();
        var fromBelow = AppServices.Settings.Current.Edge != DockEdge.Top;
        _slide.Y = fromBelow ? 10 : -10;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _frame.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
        _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (ShowActivated && !_closing) Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
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
