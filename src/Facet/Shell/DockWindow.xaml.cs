using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Facet.Apps;
using Facet.Core.Alerts;
using Facet.Core.Settings;
using Facet.Glass;
using Facet.Infrastructure;
using Facet.Interop;
using Facet.Palette;
using Facet.Popovers;
using Facet.SettingsUi;
using Facet.Theme;
using Facet.Widgets;

namespace Facet.Shell;

public partial class DockWindow : Window
{
    private const int HotkeyId = 0xFAC7;
    private const double EdgeGap = 8;
    private const double RevealMs = 190;
    private const double ConcealMs = 150;
    private const double ConcealDelayMs = 650;

    private readonly DispatcherTimer _watchdog = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _taskbarGuard = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _autoHide = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private readonly ClockWidget _clock = new();
    private readonly NotificationsButton _bell = new();
    private readonly AppStripAnimator _appMotion;
    private readonly DockGlassWindow _glassWindow = new();
    private ForegroundTracker? _foreground;
    private AppBar? _appBar;
    private Native.RECT? _reserved;
    private IntPtr _hwnd;
    private bool _hiddenForFullscreen;
    private string _widgetSignature = "";

    // auto-hide
    private bool _concealed;
    private bool _animating;
    private double _restTop;
    private DateTime _leftAt = DateTime.MaxValue;
    private DateTime _edgeSince = DateTime.MaxValue;
    private int _notificationState;

    // edge drag
    private Point? _dragStart;
    private double _dragOriginTop;
    private bool _dragging;

    public static DockWindow? Instance { get; private set; }

    /// <summary>Real monitor scale for this window (see Native.ScaleFor).</summary>
    private double Scale => _hwnd != IntPtr.Zero ? Native.ScaleFor(_hwnd) : VisualTreeHelper.GetDpi(this).DpiScaleX;

    public DockWindow()
    {
        InitializeComponent();
        Instance = this;

        // the glass lives in its own window right behind the dock; owning windows stay below the windows they own
        _glassWindow.Show();
        Owner = _glassWindow;
        LocationChanged += (_, _) => SyncGlass();
        IsVisibleChanged += (_, _) => SyncGlass();
        AppsList.ItemsSource = AppServices.Apps.Items;
        _appMotion = new AppStripAnimator(AppArea, StartButton, StartLogo, AppsList, IndicatorLayer, ActiveBar);
        ClockHost.Content = _clock;
        NotificationsHost.Content = _bell;

        _previewDelay.Tick += (_, _) => OnPreviewDelay();
        _previewWatch.Tick += (_, _) => WatchPreview();
        _watchdog.Tick += (_, _) => Watchdog();
        _taskbarGuard.Tick += (_, _) => AppServices.Taskbar.Enforce();
        _autoHide.Tick += (_, _) => AutoHideTick();

        SizeChanged += (_, _) =>
        {
            Reposition();
            SyncGlass();
        };
        Loaded += (_, _) =>
        {
            Reposition();
            _watchdog.Start();
        };

        AppServices.Settings.Changed += ApplySettings;
        AppServices.Alerts.AlertRaised += ShowAlert;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        Native.AddExStyle(_hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);

        _foreground = new ForegroundTracker();
        _foreground.Changed += hwnd =>
        {
            AppServices.Apps.OnForegroundChanged(hwnd);
            CheckFullscreen();
        };

        _appBar = new AppBar(_hwnd);
        _appBar.Register();
        ApplySettings();
    }

    // ───────────────────────── settings & layout ─────────────────────────

    private void ApplySettings()
    {
        var s = AppServices.Settings.Current;
        ThemeManager.ApplySize(s.Size, Scale);
        AppServices.Apps.EnsureIconSize(ThemeManager.IconPixels);
        var theme = ThemeManager.Effective(s);
        ThemeManager.ApplySurface(theme, s.WindowsAccent);
        ThemeManager.ApplyDock(theme);
        ApplyGlass(theme);

        if (s.Layout == DockLayout.FullWidth)
        {
            SizeToContent = SizeToContent.Height;
            SpacerColumn.Width = new GridLength(1, GridUnitType.Star);
            Divider.Visibility = Visibility.Hidden;
        }
        else
        {
            SizeToContent = SizeToContent.WidthAndHeight;
            SpacerColumn.Width = GridLength.Auto;
            Divider.Visibility = Visibility.Visible;
        }

        BuildWidgets();
        NotificationsHost.Visibility = s.ShowNotifications ? Visibility.Visible : Visibility.Collapsed;
        if (s.HideTaskbar)
        {
            AppServices.Taskbar.Hide();
            _taskbarGuard.Start();
        }
        else
        {
            _taskbarGuard.Stop();
            AppServices.Taskbar.Restore();
        }
        UpdateHotkey(s.PaletteHotkey);

        if (s.AutoHide)
        {
            _leftAt = DateTime.Now; // show briefly, then tuck away unless the cursor is on it
            _autoHide.Start();
        }
        else
        {
            _autoHide.Stop();
            _concealed = false;
            ApplyVisibility();
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            UpdateAppBar();
            Reposition();
        });
    }

    private void ApplyGlass(DockTheme theme)
    {
        if (_hwnd == IntPtr.Zero) return;
        if (_glassWindow.HasGlass)
            GlassLayer.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)); // only so empty dock space takes clicks
        else
            GlassLayer.SetResourceReference(Border.BackgroundProperty, "DockSolid"); // no compositor: solid rounded tint
        SyncGlass();
    }

    /// <summary>Keep the glass window exactly under the dock: same rect, visibility, corner radius and tint.</summary>
    private void SyncGlass()
    {
        if (_hwnd == IntPtr.Zero) return;
        var radius = TryFindResource("DockCorner") is CornerRadius cr ? cr.TopLeft * Scale : 22;
        _glassWindow.Match(_hwnd, IsVisible, radius, ThemeManager.DockTint(ThemeManager.Current));
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ApplySettings();
    }

    private void BuildWidgets()
    {
        var s = AppServices.Settings.Current;
        var signature = string.Join(",", s.Widgets.Where(w => w.Enabled).Select(w => w.Id))
                        + "|" + string.Join(",", s.CustomWidgets.Select(c => c.Id + c.Name + c.Source + c.IntervalSeconds));
        if (signature == _widgetSignature) return;
        _widgetSignature = signature;

        WidgetHost.Children.Clear();
        foreach (var slot in s.Widgets.Where(w => w.Enabled))
        {
            var widget = WidgetFactory.Create(slot.Id);
            if (widget is null) continue;
            widget.Margin = new Thickness(3, 0, 3, 0);
            widget.VerticalAlignment = VerticalAlignment.Center;
            WidgetHost.Children.Add(widget);
        }
    }

    private void UpdateAppBar()
    {
        var s = AppServices.Settings.Current;
        if (_appBar is null) return;
        if (!s.ReserveSpace || s.AutoHide)
        {
            _appBar.Release();
            _reserved = null;
            return;
        }

        var scale = Scale;
        var thickness = (int)Math.Ceiling((ActualHeight + EdgeGap * 2) * scale);
        _reserved = _appBar.Reserve(s.Edge, Math.Max(thickness, 24));
    }

    private void Reposition()
    {
        if (_hwnd == IntPtr.Zero || _dragging) return;
        var s = AppServices.Settings.Current;
        var scale = Scale;
        var monitor = Native.GetPrimaryMonitor();

        // the area the dock lives in: full monitor when the taskbar is hidden, else the work area
        var area = s.HideTaskbar ? monitor.rcMonitor : monitor.rcWork;
        if (_reserved is { } r)
        {
            area = s.Edge == DockEdge.Top
                ? new Native.RECT { Left = monitor.rcMonitor.Left, Right = monitor.rcMonitor.Right, Top = r.Top, Bottom = area.Bottom }
                : new Native.RECT { Left = monitor.rcMonitor.Left, Right = monitor.rcMonitor.Right, Top = area.Top, Bottom = r.Bottom };
        }

        double left = area.Left / scale, width = area.Width / scale;
        double top = area.Top / scale, bottom = area.Bottom / scale;

        if (s.Layout == DockLayout.FullWidth)
        {
            var target = width - EdgeGap * 2;
            if (Math.Abs(Width - target) > 0.5) Width = target;
            MaxWidth = double.PositiveInfinity;
        }
        else
        {
            MaxWidth = width - EdgeGap * 2;
        }

        Left = left + (width - ActualWidth) / 2;
        _restTop = s.Edge == DockEdge.Top ? top + EdgeGap : bottom - ActualHeight - EdgeGap;
        if (!_animating) Top = _restTop;
    }

    // ───────────────────────── auto-hide ─────────────────────────

    private void AutoHideTick()
    {
        var s = AppServices.Settings.Current;
        if (!s.AutoHide || _dragging || _animating || _hwnd == IntPtr.Zero) return;

        Native.GetCursorPos(out var p);
        var mon = Native.GetPrimaryMonitor().rcMonitor;
        var atEdge = p.X >= mon.Left && p.X < mon.Right
                     && (s.Edge == DockEdge.Top ? p.Y <= mon.Top + 1 : p.Y >= mon.Bottom - 2);

        // pressing the Windows key opens Start (or Search): bring the dock up with it
        var startOpen = StartOrSearchInFront();

        if (_concealed)
        {
            if (startOpen)
            {
                Reveal();
                return;
            }
            if (!atEdge)
            {
                _edgeSince = DateTime.MaxValue;
                return;
            }
            if (_notificationState == QUNS_RUNNING_D3D_FULL_SCREEN) return; // nothing can draw over exclusive fullscreen
            if (_hiddenForFullscreen)
            {
                // in fullscreen apps, like macOS: hold the cursor at the edge a moment to bring the dock up
                if (_edgeSince == DateTime.MaxValue) _edgeSince = DateTime.Now;
                if ((DateTime.Now - _edgeSince).TotalMilliseconds < 350) return;
            }
            _edgeSince = DateTime.MaxValue;
            Reveal();
            return;
        }

        // keep it up while the cursor is on the dock (or in the gap below it), one of its menus/panels
        // is open, or Start is open
        Native.GetWindowRect(_hwnd, out var r);
        var pad = (int)(12 * Scale);
        var overDock = p.X >= r.Left - pad && p.X <= r.Right + pad
                       && (s.Edge == DockEdge.Top ? p.Y <= r.Bottom + pad : p.Y >= r.Top - pad);
        var busy = startOpen || PopoverWindow.IsOpen || WindowPreview.IsOpen || CommandPalette.IsOpen || Mouse.Captured is not null;
        if (overDock || atEdge || busy)
        {
            _leftAt = DateTime.MaxValue;
            return;
        }

        if (_leftAt == DateTime.MaxValue) _leftAt = DateTime.Now;
        else if ((DateTime.Now - _leftAt).TotalMilliseconds >= ConcealDelayMs) Conceal();
    }

    private readonly Dictionary<uint, string> _processNames = [];

    /// <summary>True while the Start menu or Windows Search (what the Windows key opens) is in front.</summary>
    private bool StartOrSearchInFront()
    {
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        Native.GetWindowThreadProcessId(fg, out var pid);
        if (!_processNames.TryGetValue(pid, out var name))
        {
            try { name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; }
            catch (ArgumentException) { name = ""; }
            catch (InvalidOperationException) { name = ""; }
            if (_processNames.Count > 64) _processNames.Clear(); // pids get reused; keep the cache small
            _processNames[pid] = name;
        }
        return name is "StartMenuExperienceHost" or "SearchHost" or "SearchApp";
    }

    private double SlideOffset() =>
        (ActualHeight + EdgeGap + 4) * (AppServices.Settings.Current.Edge == DockEdge.Top ? -1 : 1);

    /// <summary>Slide the dock in (no-op unless auto-hide tucked it away).</summary>
    public void Reveal()
    {
        _leftAt = DateTime.MaxValue;
        if (!_concealed) return;
        _concealed = false;
        Top = _restTop + SlideOffset();
        Visibility = Visibility.Visible;
        Slide(_restTop, RevealMs, new CubicEase { EasingMode = EasingMode.EaseOut }, null);
    }

    private void Conceal()
    {
        if (_concealed) return;
        _concealed = true;
        HidePreview();
        Slide(_restTop + SlideOffset(), ConcealMs, new CubicEase { EasingMode = EasingMode.EaseIn }, () =>
        {
            ApplyVisibility();
            Top = _restTop; // keep anchors (toasts, popovers) at the dock's real position
        });
    }

    private void Slide(double to, double ms, IEasingFunction ease, Action? done)
    {
        if (!Motion.Enabled)
        {
            BeginAnimation(TopProperty, null);
            Top = to;
            done?.Invoke();
            return;
        }
        _animating = true;
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
        anim.Completed += (_, _) =>
        {
            Top = to;
            BeginAnimation(TopProperty, null);
            _animating = false;
            done?.Invoke();
        };
        BeginAnimation(TopProperty, anim);
    }

    private void ApplyVisibility()
    {
        // auto-hide: the reveal/conceal cycle decides (fullscreen apps only add a dwell to the reveal);
        // always-visible dock: step aside for fullscreen apps
        var hide = AppServices.Settings.Current.AutoHide ? _concealed : _hiddenForFullscreen;
        Visibility = hide ? Visibility.Hidden : Visibility.Visible;
    }

    // ───────────────────────── window messages ─────────────────────────

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Native.WM_MOUSEACTIVATE:
                handled = true;
                return new IntPtr(Native.MA_NOACTIVATE);
            case Native.WM_HOTKEY when wParam.ToInt32() == HotkeyId:
                CommandPalette.Toggle();
                handled = true;
                break;
            case Native.WM_SETTINGCHANGE:
                var action = (uint)wParam.ToInt64();
                if (action is Native.SPI_SETWORKAREA || action == 0)
                    Dispatcher.BeginInvoke(Reposition);
                // Windows' light/dark mode or accent colour changed
                if (lParam != IntPtr.Zero && System.Runtime.InteropServices.Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet")
                    Dispatcher.BeginInvoke(ApplySettings);
                break;
            case Native.WM_DISPLAYCHANGE:
                Dispatcher.BeginInvoke(() => { UpdateAppBar(); Reposition(); });
                break;
        }

        if (_appBar is { Registered: true } && msg == (int)_appBar.CallbackMessage)
        {
            switch (wParam.ToInt32())
            {
                case AppBar.ABN_POSCHANGED:
                    Dispatcher.BeginInvoke(() => { UpdateAppBar(); Reposition(); });
                    break;
                case AppBar.ABN_FULLSCREENAPP:
                    OnShellFullscreen(lParam != IntPtr.Zero);
                    break;
            }
        }
        return IntPtr.Zero;
    }

    private string? _registeredPreference;

    private void UpdateHotkey(bool enabled)
    {
        if (_hwnd == IntPtr.Zero) return;
        var preferred = AppServices.Settings.Current.PaletteShortcut;
        if (!enabled)
        {
            Hotkeys.Unregister(_hwnd, HotkeyId);
            _registeredPreference = null;
            return;
        }
        if (Hotkeys.Active is not null && _registeredPreference == preferred) return;
        Hotkeys.Register(_hwnd, HotkeyId, preferred);
        _registeredPreference = preferred;
        if (Hotkeys.Active != preferred) App.Log(new InvalidOperationException($"Palette shortcut: {Hotkeys.Status}"));
    }

    // ───────────────────────── fullscreen & z-order ─────────────────────────

    private void Watchdog()
    {
        CheckFullscreen();
        // don't re-stack while one of our menus is open, or the dock would cover it
        if (IsVisible && _hwnd != IntPtr.Zero && Mouse.Captured is null)
        {
            // stay above other topmost windows (e.g. the taskbar re-asserting itself); raising the glass
            // window (the owner) raises the dock with it
            var top = _glassWindow.Handle != IntPtr.Zero ? _glassWindow.Handle : _hwnd;
            Native.SetWindowPos(top, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
        if (AppServices.Settings.Current.HideTaskbar && !AppServices.Taskbar.IsHidden) AppServices.Taskbar.Hide();
    }

    // The window the shell last flagged as fullscreen (ABN_FULLSCREENAPP), remembered so switching back
    // to it hides the dock again even though the shell doesn't re-announce it.
    private IntPtr _rudeWindow;

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    private const int QUNS_BUSY = 2;                    // a fullscreen app is running
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3; // exclusive-fullscreen game
    private const int QUNS_PRESENTATION_MODE = 4;

    private void OnShellFullscreen(bool opening)
    {
        var fg = Native.GetForegroundWindow();
        _rudeWindow = opening && IsFullscreenWindow(fg) ? fg : IntPtr.Zero;
        CheckFullscreen();
    }

    /// <summary>Covers its whole monitor and isn't simply maximized (maximized windows keep the dock).</summary>
    private static bool IsFullscreenWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || Native.IsZoomed(hwnd) || !Native.GetWindowRect(hwnd, out var r)) return false;
        var cls = Native.GetClassName(hwnd);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        var m = Native.GetMonitorFor(hwnd).rcMonitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    /// <summary>
    /// Step aside only when Windows itself reports a fullscreen app (the same state that silences its
    /// notifications) *and* that app is the one in front — or for exclusive-fullscreen games / presenting.
    /// </summary>
    private void CheckFullscreen()
    {
        if (!AppServices.Settings.Current.HideInFullscreen)
        {
            SetFullscreenHidden(false);
            return;
        }

        var fg = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(fg, out var pid);
        if (pid == Environment.ProcessId) return; // our own popovers never change the decision
        if (_rudeWindow != IntPtr.Zero && !Native.IsWindow(_rudeWindow)) _rudeWindow = IntPtr.Zero;

        var state = SHQueryUserNotificationState(out var s) == 0 ? s : 0;
        _notificationState = state;
        var hide = state is QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE
                   || (state == QUNS_BUSY && (fg == _rudeWindow || IsFullscreenWindow(fg)));
        SetFullscreenHidden(hide);
    }

    private void SetFullscreenHidden(bool hide)
    {
        if (hide == _hiddenForFullscreen) return;
        _hiddenForFullscreen = hide;
        if (!_animating) ApplyVisibility();
    }

    // ───────────────────────── start & apps ─────────────────────────

    /// <summary>
    /// Toggles Start like the taskbar button: if Start was open when this click began, the press already
    /// dismissed it (Root_PreviewMouseDown) — so don't open it again.
    /// </summary>
    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_startOpenAtPress) return;
        Native.PressWindowsKey();
    }

    private bool _startOpenAtPress;

    private void App_Click(object sender, RoutedEventArgs e)
    {
        HidePreview();
        if ((sender as FrameworkElement)?.DataContext is AppItem item) AppServices.Apps.Click(item);
    }

    // ───────────────────────── window previews ─────────────────────────

    private readonly DispatcherTimer _previewDelay = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer _previewWatch = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private (AppItem Item, FrameworkElement Anchor)? _previewPending;
    private int _previewAway;

    /// <summary>Resting on a running app's icon shows live previews of its windows; moving on switches them.</summary>
    private void App_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement button || button.DataContext is not AppItem item) return;
        var previews = AppServices.Settings.Current.WindowPreviews;
        // the preview names the windows; the title tooltip would only cover it
        ToolTipService.SetIsEnabled(button, !(previews && item.IsRunning));
        _previewPending = null;
        if (!previews || !item.IsRunning || item.IsLeaving || _dragItem is not null || e.LeftButton == MouseButtonState.Pressed) return;
        if (WindowPreview.IsOpen)
        {
            ShowPreview(item, button); // already browsing previews: switch at once
            return;
        }
        _previewPending = (item, button);
        _previewDelay.Stop();
        _previewDelay.Start();
    }

    private void App_MouseLeave(object sender, MouseEventArgs e)
    {
        _previewDelay.Stop();
        _previewPending = null;
    }

    private void OnPreviewDelay()
    {
        _previewDelay.Stop();
        if (_previewPending is { } p && p.Anchor.IsMouseOver && Mouse.Captured is null) ShowPreview(p.Item, p.Anchor);
        _previewPending = null;
    }

    private void ShowPreview(AppItem item, FrameworkElement anchor)
    {
        WindowPreview.ShowFor(item, anchor);
        _previewAway = 0;
        _previewWatch.Start();
    }

    private void HidePreview()
    {
        _previewDelay.Stop();
        _previewPending = null;
        _previewWatch.Stop();
        WindowPreview.HideCurrent();
    }

    /// <summary>Keep the preview while the cursor is on it or on the dock's icons; close it shortly after it leaves both.</summary>
    private void WatchPreview()
    {
        if (!WindowPreview.IsOpen)
        {
            _previewWatch.Stop();
            return;
        }
        Native.GetCursorPos(out var p);
        if (WindowPreview.Contains(p) || AppArea.IsMouseOver)
        {
            _previewAway = 0;
            return;
        }
        if (++_previewAway >= 3) HidePreview(); // ~⅓ s of grace to cross the gap between icon and preview
    }

    private void App_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        if ((sender as FrameworkElement)?.DataContext is AppItem item) AppServices.Apps.Launch(item);
        e.Handled = true;
    }

    private void App_RightClick(object sender, MouseButtonEventArgs e)
    {
        HidePreview();
        if ((sender as FrameworkElement)?.DataContext is not AppItem item) return;
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };

        foreach (var hwnd in item.Windows.Take(10))
        {
            var title = Native.GetWindowTitle(hwnd);
            if (title.Length > 60) title = title[..57] + "…";
            var w = hwnd;
            AddItem(menu, title, () => Native.ActivateWindow(w), glyph: "");
        }
        if (item.Windows.Count > 0) menu.Items.Add(new Separator());

        AddItem(menu, item.IsRunning ? "New window" : $"Open {item.Name}", () => AppServices.Apps.Launch(item), glyph: "");
        if (item.IsPinned) AddItem(menu, "Unpin from dock", () => AppServices.Apps.Unpin(item), glyph: "");
        else AddItem(menu, "Pin to dock", () => AppServices.Apps.Pin(item), glyph: "");
        if (item.IsRunning)
            AddItem(menu, item.Windows.Count > 1 ? "Close all windows" : "Close window", () => AppServices.Apps.CloseAll(item), glyph: "");

        MenuGuard.Watch(menu);
        menu.IsOpen = true;
        e.Handled = true;
    }

    // ───────────────────────── dock menu ─────────────────────────

    private void Root_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;
        var s = AppServices.Settings.Current;
        // open above (or below, when docked at the top) the dock at the cursor, never overlapping it
        var menu = new ContextMenu
        {
            PlacementTarget = GlassLayer,
            Placement = s.Edge == DockEdge.Top
                ? System.Windows.Controls.Primitives.PlacementMode.Bottom
                : System.Windows.Controls.Primitives.PlacementMode.Top,
            HorizontalOffset = Math.Max(0, e.GetPosition(GlassLayer).X - 24),
        };

        AddItem(menu, Hotkeys.Active is { } shortcut ? $"Command palette   {shortcut}" : "Command palette", CommandPalette.Toggle, glyph: "");
        menu.Items.Add(new Separator());

        var theme = Submenu(menu, "Theme", "");
        AddChoice(theme, "Match Windows (Glass / Frosted)", s.MatchWindowsMode, () => { s.MatchWindowsMode = !s.MatchWindowsMode; AppServices.Settings.Save(); });
        theme.Items.Add(new Separator());
        foreach (var t in Enum.GetValues<DockTheme>())
            AddChoice(theme, ThemeLabel(t), !s.MatchWindowsMode && s.Theme == t, () =>
            {
                s.Theme = t;
                s.MatchWindowsMode = false;
                AppServices.Settings.Save();
            });

        var size = Submenu(menu, "Size", "");
        foreach (var z in Enum.GetValues<DockSize>())
            AddChoice(size, z.ToString(), s.Size == z, () => { s.Size = z; AppServices.Settings.Save(); });

        var edge = Submenu(menu, "Position", "");
        AddChoice(edge, "Bottom", s.Edge == DockEdge.Bottom, () => { s.Edge = DockEdge.Bottom; AppServices.Settings.Save(); });
        AddChoice(edge, "Top", s.Edge == DockEdge.Top, () => { s.Edge = DockEdge.Top; AppServices.Settings.Save(); });
        edge.Items.Add(new Separator());
        AddChoice(edge, "Floating", s.Layout == DockLayout.Pill, () => { s.Layout = DockLayout.Pill; AppServices.Settings.Save(); });
        AddChoice(edge, "Full width", s.Layout == DockLayout.FullWidth, () => { s.Layout = DockLayout.FullWidth; AppServices.Settings.Save(); });

        var widgets = Submenu(menu, "Widgets", "");
        foreach (var slot in s.Widgets)
        {
            var captured = slot;
            AddChoice(widgets, WidgetFactory.DisplayName(slot.Id), slot.Enabled, () =>
            {
                captured.Enabled = !captured.Enabled;
                AppServices.Settings.Save();
            });
        }
        widgets.Items.Add(new Separator());
        AddChoice(widgets, "Notifications button", s.ShowNotifications, () =>
        {
            s.ShowNotifications = !s.ShowNotifications;
            AppServices.Settings.Save();
        });
        if (s.Widgets.Any(w => w is { Id: "connect", Enabled: true }))
            ConnectItems.AddChoices(Submenu(menu, "Quick controls", ""));

        menu.Items.Add(new Separator());
        AddChoice(menu, "Auto-hide dock", s.AutoHide, () => { s.AutoHide = !s.AutoHide; AppServices.Settings.Save(); });
        AddChoice(menu, "Hide Windows taskbar", s.HideTaskbar, () => { s.HideTaskbar = !s.HideTaskbar; AppServices.Settings.Save(); });
        AddItem(menu, "Settings…", () => SettingsWindow.Open(), glyph: "");
        menu.Items.Add(new Separator());
        AddItem(menu, "Quit Facet", () => Application.Current.Shutdown(), glyph: "");

        MenuGuard.Watch(menu);
        menu.IsOpen = true;
        e.Handled = true;
    }

    internal static string ThemeLabel(DockTheme t) => t switch
    {
        DockTheme.LiquidGlass => "Glass",
        DockTheme.Frost => "Frosted",
        _ => t.ToString(),
    };

    private static MenuItem Submenu(ItemsControl parent, string header, string glyph)
    {
        var item = new MenuItem { Header = header, Icon = glyph };
        parent.Items.Add(item);
        return item;
    }

    private static void AddItem(ItemsControl parent, string header, Action action, string? glyph = null)
    {
        var item = new MenuItem { Header = header };
        if (glyph is not null) item.Icon = glyph;
        item.Click += (_, _) => action();
        parent.Items.Add(item);
    }

    private static void AddChoice(ItemsControl parent, string header, bool isChecked, Action action)
    {
        var item = new MenuItem { Header = header, IsChecked = isChecked };
        item.Click += (_, _) => action();
        parent.Items.Add(item);
    }

    // ───────────────────────── drag and drop ─────────────────────────

    private const double HoverActivateMs = 650;
    private readonly DispatcherTimer _dragHover = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private AppItem? _dragItem;
    private DateTime _dragSince;
    private bool _dragActivated;

    private static string[] DroppedFiles(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files ? files : [];

    private void App_DragEnter(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AppItem item) return;
        if (!ReferenceEquals(_dragItem, item))
        {
            if (_dragItem is not null) _dragItem.IsDropTarget = false;
            _dragItem = item;
            _dragSince = DateTime.Now;
            _dragActivated = false;
            item.IsDropTarget = true;
            if (!_dragHover.IsEnabled)
            {
                _dragHover.Tick -= DragHoverTick;
                _dragHover.Tick += DragHoverTick;
                _dragHover.Start();
            }
        }
        App_DragOver(sender, e);
    }

    private void App_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedFiles(e).Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void App_DragLeave(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AppItem item && ReferenceEquals(item, _dragItem)) EndDragHover();
    }

    private void App_Drop(object sender, DragEventArgs e)
    {
        EndDragHover();
        if ((sender as FrameworkElement)?.DataContext is not AppItem item) return;
        var files = DroppedFiles(e);
        if (files.Length == 0) return;
        AppServices.Apps.OpenFiles(item, files);
        e.Handled = true;
    }

    /// <summary>Like the Windows taskbar: hold a dragged file over a running app to bring its window forward.</summary>
    private void DragHoverTick(object? sender, EventArgs e)
    {
        if (_dragItem is null)
        {
            _dragHover.Stop();
            return;
        }
        if (_dragActivated || !_dragItem.IsRunning) return;
        if ((DateTime.Now - _dragSince).TotalMilliseconds < HoverActivateMs) return;
        _dragActivated = true;
        AppServices.Apps.BringToFront(_dragItem);
    }

    private void EndDragHover()
    {
        if (_dragItem is not null) _dragItem.IsDropTarget = false;
        _dragItem = null;
        _dragHover.Stop();
    }

    /// <summary>Empty dock space: programs and shortcuts can be dropped here to pin them.</summary>
    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (e.Handled) return;
        e.Effects = AppTracker.ArePinnable(DroppedFiles(e)) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void Root_Drop(object sender, DragEventArgs e)
    {
        if (e.Handled) return;
        EndDragHover();
        AppServices.Apps.PinFiles(DroppedFiles(e));
        e.Handled = true;
    }

    // ───────────────────────── Start interplay ─────────────────────────

    /// <summary>
    /// The dock never takes focus, so clicking it doesn't dismiss Start the way clicking any other window
    /// does — and whatever the click opens would land behind Start. Close Start first (Esc is how the
    /// shell itself dismisses it), then let the click proceed.
    /// </summary>
    private void Root_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _startOpenAtPress = StartOrSearchInFront();
        if (!_startOpenAtPress) return;
        const byte VK_ESCAPE = 0x1B;
        Native.keybd_event(VK_ESCAPE, 0, 0, UIntPtr.Zero);
        Native.keybd_event(VK_ESCAPE, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    // ───────────────────────── edge drag ─────────────────────────

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || _animating) return;
        Native.GetCursorPos(out var p);
        _dragStart = new Point(p.X, p.Y);
        _dragOriginTop = Top;
        Root.CaptureMouse();
    }

    private void Root_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || !Root.IsMouseCaptured) return;
        Native.GetCursorPos(out var p);
        var dy = p.Y - start.Y;
        var scale = Scale;
        if (!_dragging && Math.Abs(dy) > 14) _dragging = true;
        if (_dragging) Top = _dragOriginTop + dy / scale;
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is null) return;
        Root.ReleaseMouseCapture();
        _dragStart = null;
        if (!_dragging) return;
        _dragging = false;

        Native.GetCursorPos(out var p);
        var mon = Native.GetPrimaryMonitor().rcMonitor;
        var edge = p.Y < mon.Top + mon.Height / 2 ? DockEdge.Top : DockEdge.Bottom;
        var s = AppServices.Settings.Current;
        if (s.Edge != edge)
        {
            s.Edge = edge;
            AppServices.Settings.Save();
        }
        else
        {
            Reposition();
        }
    }

    // ───────────────────────── alerts & entry points ─────────────────────────

    private void ShowAlert(Alert alert)
    {
        if (_hiddenForFullscreen && !alert.Critical) return;
        ToastWindow.Show(alert, GlassLayer, action =>
        {
            switch (action)
            {
                case AlertAction.PauseMedia:
                    _ = AppServices.Media.PauseAsync();
                    break;
                case AlertAction.OpenCalendar:
                    OpenCalendar();
                    break;
            }
        });
    }

    public void OpenCalendar()
    {
        Reveal();
        PopoverWindow.Toggle(_clock.Anchor, () => new CalendarPanel());
    }

    public void OpenTodos()
    {
        Reveal();
        var anchor = WidgetHost.Children.OfType<FrameworkElement>().FirstOrDefault(c => c.DataContext is Services.TodoService)
                     ?? _clock.Anchor;
        PopoverWindow.Toggle(anchor, () => new TodoPanel());
    }

    public void Teardown()
    {
        _watchdog.Stop();
        _taskbarGuard.Stop();
        _autoHide.Stop();
        Hotkeys.Unregister(_hwnd, HotkeyId);
        _appBar?.Remove();
        _foreground?.Dispose();
        _glassWindow.Close();
    }
}
