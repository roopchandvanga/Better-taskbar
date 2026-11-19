using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Facet.Core.Alerts;
using Facet.Core.Settings;
using Facet.Infrastructure;
using Facet.Interop;
using Facet.Theme;

namespace Facet.Popovers;

/// <summary>Non-activating acrylic alert card stacked above the dock's right end.</summary>
public sealed class ToastWindow : PopoverWindow
{
    private static readonly List<ToastWindow> Open = [];
    private readonly DispatcherTimer _dismiss = new() { Interval = TimeSpan.FromSeconds(9) };
    private readonly FrameworkElement _dockAnchor;

    public static void Show(Alert alert, FrameworkElement dockAnchor, Action<AlertAction> onAction)
    {
        if (Open.Count >= 3) Open[0].Close();
        var toast = new ToastWindow(alert, dockAnchor, onAction);
        Open.Add(toast);
        toast.Show();
    }

    private ToastWindow(Alert alert, FrameworkElement dockAnchor, Action<AlertAction> onAction)
        : base(null, BuildContent(alert, onAction, out var close), activates: false)
    {
        _dockAnchor = dockAnchor;
        close.Click += (_, _) => Close();
        _dismiss.Tick += (_, _) => { if (!IsMouseOver) Close(); };
        _dismiss.Start();
        Closed += (_, _) =>
        {
            _dismiss.Stop();
            Open.Remove(this);
            foreach (var t in Open) t.Position();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Native.AddExStyle(new WindowInteropHelper(this).Handle, Native.WS_EX_NOACTIVATE);
    }

    protected override void Position()
    {
        if (PresentationSource.FromVisual(_dockAnchor) is null) return;
        var dpi = VisualTreeHelper.GetDpi(_dockAnchor);
        var origin = _dockAnchor.PointToScreen(new Point(0, 0));
        var right = (origin.X / dpi.DpiScaleX) + _dockAnchor.ActualWidth;
        var top = origin.Y / dpi.DpiScaleY;
        var bottom = top + _dockAnchor.ActualHeight;
        var index = Open.IndexOf(this);
        var offset = Open.Take(Math.Max(0, index)).Sum(t => t.ActualHeight + 10);

        Left = right - ActualWidth;
        Top = AppServices.Settings.Current.Edge == DockEdge.Top
            ? bottom + 12 + offset
            : top - ActualHeight - 12 - offset;
    }

    private static FrameworkElement BuildContent(Alert alert, Action<AlertAction> onAction, out Button close)
    {
        var (glyph, color) = alert.Kind switch
        {
            AlertKind.Event => ("", "#0A84FF"),
            AlertKind.Battery => ("", alert.Critical ? "#FF453A" : "#FF9F0A"),
            AlertKind.Cpu => ("", "#FF9F0A"),
            AlertKind.Pomodoro => ("", "#FF6B5E"),
            _ => ("", "#0A84FF"),
        };

        var root = new Grid { Width = 340 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(10),
            Background = ThemeManager.Brush(color),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = glyph,
                FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
                FontSize = 16,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        root.Children.Add(icon);

        var text = new StackPanel { Margin = new Thickness(12, 0, 6, 0) };
        text.Children.Add(new TextBlock { Text = alert.Title, FontWeight = FontWeights.SemiBold, FontSize = 13.5, TextWrapping = TextWrapping.Wrap });
        var body = new TextBlock { Text = alert.Body, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        body.SetResourceReference(TextBlock.ForegroundProperty, "SurfaceSubtle");
        text.Children.Add(body);

        if (alert.Action != AlertAction.None)
        {
            var label = alert.Action == AlertAction.PauseMedia ? "Pause music" : "Open calendar";
            var action = new Button
            {
                Content = label,
                Style = (Style)Application.Current.FindResource("AccentPillButton"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(12, 5, 12, 5),
            };
            action.Click += (s, _) =>
            {
                onAction(alert.Action);
                Window.GetWindow((DependencyObject)s)?.Close();
            };
            text.Children.Add(action);
        }

        Grid.SetColumn(text, 1);
        root.Children.Add(text);

        close = new Button
        {
            Content = "",
            Style = (Style)Application.Current.FindResource("SurfaceGlyphButton"),
            Width = 26,
            Height = 26,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Grid.SetColumn(close, 2);
        root.Children.Add(close);
        return root;
    }
}
