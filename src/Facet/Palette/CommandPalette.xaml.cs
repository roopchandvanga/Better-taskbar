using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Facet.Core.Palette;
using Facet.Core.Settings;
using Facet.Glass;
using Facet.Infrastructure;
using Facet.Interop;
using Facet.Popovers;
using Facet.Services;
using Facet.SettingsUi;
using Facet.Shell;
using Facet.Theme;

namespace Facet.Palette;

public sealed class PaletteItem : ObservableObject
{
    private ImageSource? _icon;

    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Glyph { get; init; }
    public string? Hint { get; init; }
    public string? IconSource { get; init; }
    public required Action Execute { get; init; }
    public int Score { get; init; }
    public ImageSource? Icon { get => _icon; set => Set(ref _icon, value); }
}

/// <summary>Keyboard launcher: apps, dock actions, math, shell commands, folders, URLs, quick todos.</summary>
public partial class CommandPalette : Window
{
    private static CommandPalette? _open;
    private static readonly Dictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _closing;

    private DateTime _shownAt;

    public static bool IsOpen => _open is not null;

    public static void Toggle()
    {
        if (_open is not null)
        {
            _open.Close();
            return;
        }

        // Activate the palette *before* any open popover goes away: the popover closes itself when it
        // loses activation, and focus stays inside Facet instead of bouncing to another app.
        _open = new CommandPalette { _shownAt = DateTime.Now };
        _open.Show();
        var hwnd = new WindowInteropHelper(_open).Handle;
        Native.ActivateWindow(hwnd);
        _open.Activate();
        _open.Query.Focus();
        PopoverWindow.CloseCurrent();
    }

    private CommandPalette()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (Facet.Shell.Hotkeys.Active is { } shortcut) Footer.Text += $" · {shortcut} toggles";
            Place();
            Search("");
            Keyboard.Focus(Query);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Native.AddExStyle(new WindowInteropHelper(this).Handle, Native.WS_EX_TOOLWINDOW);
        if (!Backdrop.TryApply(this, !ThemeManager.IsLightSurface(AppServices.Settings.Current.Theme)))
            Frame.SetResourceReference(Border.BackgroundProperty, "SurfaceSolid");
    }

    private void Place()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var mon = Native.GetPrimaryMonitor().rcMonitor;
        Left = mon.Left / dpi.DpiScaleX + (mon.Width / dpi.DpiScaleX - ActualWidth) / 2;
        Top = mon.Top / dpi.DpiScaleY + mon.Height / dpi.DpiScaleY * 0.2;
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (_closing) return;
        if ((DateTime.Now - _shownAt).TotalMilliseconds < 400)
        {
            // a stray activation from the window we just replaced: take focus back
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { Activate(); Query.Focus(); });
            return;
        }
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _open = null;
    }

    // ───────────────────────── input ─────────────────────────

    private void Query_TextChanged(object sender, TextChangedEventArgs e)
    {
        Hint.Visibility = Query.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Search(Query.Text);
    }

    private void Query_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Down:
                Move(1);
                e.Handled = true;
                break;
            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                Run(Results.SelectedItem as PaletteItem);
                e.Handled = true;
                break;
        }
    }

    private void Move(int delta)
    {
        if (Results.Items.Count == 0) return;
        var i = Math.Clamp(Results.SelectedIndex + delta, 0, Results.Items.Count - 1);
        Results.SelectedIndex = i;
        Results.ScrollIntoView(Results.SelectedItem);
    }

    private void Results_Click(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is PaletteItem item) Run(item);
    }

    private void Run(PaletteItem? item)
    {
        if (item is null) return;
        Close();
        try { item.Execute(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            App.Log(ex);
        }
    }

    // ───────────────────────── search ─────────────────────────

    private void Search(string raw)
    {
        var q = raw.Trim();
        var results = new List<PaletteItem>();

        if (q.StartsWith('>'))
        {
            var cmd = q[1..].Trim();
            if (cmd.Length > 0)
                results.Add(new PaletteItem
                {
                    Title = $"Run: {cmd}", Subtitle = "Opens a terminal and runs the command", Glyph = "", Hint = "Enter", Score = int.MaxValue,
                    Execute = () => AppServices.Open("cmd.exe", $"/k {cmd}"),
                });
            Show(results);
            return;
        }

        if (q.StartsWith("todo ", StringComparison.OrdinalIgnoreCase) && q.Length > 5)
        {
            var text = q[5..].Trim();
            results.Add(new PaletteItem
            {
                Title = $"Add todo: {text}", Glyph = "", Hint = "Enter", Score = int.MaxValue,
                Execute = () => AppServices.Todos.Add(text),
            });
            Show(results);
            return;
        }

        if (q.StartsWith('?') && q.Length > 1)
        {
            var term = q[1..].Trim();
            results.Add(WebSearch(term, int.MaxValue));
            Show(results);
            return;
        }

        var expr = q.StartsWith('=') ? q[1..] : q;
        var isMath = MathEvaluator.LooksLikeMath(expr) || (q.StartsWith('=') && MathEvaluator.TryEvaluate(expr, out _));
        if (isMath)
        {
            var value = MathEvaluator.Evaluate(expr);
            var text = value.ToString("G12", CultureInfo.CurrentCulture);
            results.Add(new PaletteItem
            {
                Title = $"= {text}", Subtitle = $"{expr.Trim()} · Enter copies the result", Glyph = "", Hint = "Copy", Score = int.MaxValue,
                Execute = () => Clipboard.SetText(text),
            });
        }

        if (LooksLikePath(q))
        {
            var path = Environment.ExpandEnvironmentVariables(q.Trim('"'));
            if (Directory.Exists(path) || File.Exists(path))
                results.Add(new PaletteItem
                {
                    Title = Directory.Exists(path) ? $"Open folder {path}" : $"Open {Path.GetFileName(path)}",
                    Subtitle = path, IconSource = path, Hint = "Enter", Score = int.MaxValue - 1,
                    Execute = () => AppServices.Open(path),
                });
        }

        if (LooksLikeUrl(q))
        {
            var url = q.Contains("://", StringComparison.Ordinal) ? q : "https://" + q;
            results.Add(new PaletteItem { Title = $"Open {url}", Glyph = "", Hint = "Enter", Score = int.MaxValue - 2, Execute = () => AppServices.Open(url) });
        }

        if (q.Length == 0)
        {
            foreach (var app in AppServices.Apps.Items.Where(a => a.IsRunning).Take(5))
            {
                var captured = app;
                results.Add(new PaletteItem
                {
                    Title = app.Name, Subtitle = "Running · switch to it", Hint = "App", Score = 1000,
                    Execute = () => AppServices.Apps.Click(captured),
                    Icon = app.Icon,
                });
            }
            results.AddRange(Actions().Take(6));
            Show(results);
            return;
        }

        foreach (var app in AppServices.Catalog.Apps)
        {
            var score = FuzzyMatcher.Score(q, app.Name);
            if (score is null) continue;
            var captured = app;
            results.Add(new PaletteItem
            {
                Title = app.Name, Hint = "App", IconSource = app.ParsingName, Score = score.Value + 300,
                Execute = () =>
                {
                    if (captured.ParsingName.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) AppServices.Open("explorer.exe", captured.ParsingName);
                    else AppServices.Open(captured.ParsingName);
                },
            });
        }

        foreach (var action in Actions())
        {
            var score = FuzzyMatcher.Score(q, action.Title);
            if (score is null) continue;
            results.Add(new PaletteItem
            {
                Title = action.Title, Subtitle = action.Subtitle, Glyph = action.Glyph, Hint = "Action",
                Score = score.Value, Execute = action.Execute,
            });
        }

        if (!isMath && results.Count < 8 && q.Length > 1) results.Add(WebSearch(q, int.MinValue));
        Show(results);
    }

    private void Show(List<PaletteItem> results)
    {
        var top = results.OrderByDescending(r => r.Score).Take(9).ToList();
        Results.ItemsSource = top;
        Results.SelectedIndex = top.Count > 0 ? 0 : -1;
        Results.Visibility = top.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Separator.Visibility = Results.Visibility;

        foreach (var item in top.Where(i => i.Icon is null && i.IconSource is not null))
        {
            if (IconCache.TryGetValue(item.IconSource!, out var cached))
            {
                item.Icon = cached;
                continue;
            }
            var captured = item;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                var icon = ShellInterop.GetShellIcon(captured.IconSource!, 48);
                IconCache[captured.IconSource!] = icon;
                captured.Icon = icon;
            });
        }
    }

    private static PaletteItem WebSearch(string term, int score) => new()
    {
        Title = $"Search the web for “{term}”", Glyph = "", Hint = "Web", Score = score,
        Execute = () => AppServices.Open("https://duckduckgo.com/?q=" + Uri.EscapeDataString(term)),
    };

    private static bool LooksLikePath(string q) =>
        q.Length > 2 && (q[1] == ':' || q.StartsWith(@"\\", StringComparison.Ordinal) || q.StartsWith('%') || q.StartsWith('~'));

    private static bool LooksLikeUrl(string q) =>
        !q.Contains(' ') && (q.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || q.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                             || (q.Contains('.') && q.Length > 4 && !q.Contains('\\') && char.IsLetter(q[^1]) && q.IndexOf('.') > 0
                                 && !MathEvaluator.LooksLikeMath(q)));

    private static IEnumerable<PaletteItem> Actions()
    {
        var s = AppServices.Settings.Current;
        PaletteItem A(string title, string glyph, Action run, string? sub = null) =>
            new() { Title = title, Glyph = glyph, Execute = run, Subtitle = sub };

        yield return A("Facet settings", "", () => SettingsWindow.Open());
        yield return A(AppServices.Pomodoro.Running ? "Pause focus timer" : "Start focus session", "", AppServices.Pomodoro.Toggle,
            $"{s.PomodoroFocusMinutes} min focus · {s.PomodoroBreakMinutes} min break");
        yield return A(AppServices.Alerts.DoNotDisturb ? "Turn off do not disturb" : "Turn on do not disturb", "",
            () => AppServices.Alerts.DoNotDisturb = !AppServices.Alerts.DoNotDisturb);
        yield return A("Notification center", "", AppServices.Notifications.OpenCenter, "Win+N");
        yield return A("Open calendar", "", () => DockWindow.Instance?.OpenCalendar());
        yield return A("Open todos", "", () => DockWindow.Instance?.OpenTodos());
        yield return A(s.HideTaskbar ? "Show Windows taskbar" : "Hide Windows taskbar", "", () =>
        {
            s.HideTaskbar = !s.HideTaskbar;
            AppServices.Settings.Save();
        });
        yield return A(s.Edge == DockEdge.Bottom ? "Move dock to top" : "Move dock to bottom", "", () =>
        {
            s.Edge = s.Edge == DockEdge.Bottom ? DockEdge.Top : DockEdge.Bottom;
            AppServices.Settings.Save();
        });
        foreach (var theme in Enum.GetValues<DockTheme>().Where(t => t != s.Theme))
        {
            var t = theme;
            yield return A($"Theme: {DockWindow.ThemeLabel(t)}", "", () =>
            {
                s.Theme = t;
                AppServices.Settings.Save();
            });
        }
        yield return A("Refresh weather", "", () => _ = AppServices.Weather.RefreshAsync(force: true));
        yield return A("Downloads", "", () => AppServices.Open("shell:Downloads"));
        yield return A("Documents", "", () => AppServices.Open("shell:Personal"));
        yield return A("Recycle Bin", "", () => AppServices.Open("shell:RecycleBinFolder"));
        yield return A("Task Manager", "", () => AppServices.Open("taskmgr.exe"));
        yield return A("Windows Settings", "", () => AppServices.Open("ms-settings:"));
        yield return A("Lock PC", "", () => AppServices.Open("rundll32.exe", "user32.dll,LockWorkStation"));
        yield return A("Quit Facet", "", () => Application.Current.Shutdown());
    }
}
