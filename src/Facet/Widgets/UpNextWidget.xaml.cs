using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Facet.Infrastructure;
using Facet.Popovers;
using Facet.Services;
using Facet.Theme;

namespace Facet.Widgets;

public partial class UpNextWidget : UserControl
{
    private readonly System.Windows.Threading.DispatcherTimer _iconRetry = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _iconAttempts;

    public UpNextWidget()
    {
        InitializeComponent();
        Update();
        AppServices.Calendar.PropertyChanged += OnChanged;
        Unloaded += (_, _) =>
        {
            AppServices.Calendar.PropertyChanged -= OnChanged;
            _iconRetry.Stop();
        };

        // the Start-menu catalog loads in the background; look for the Calendar app once it's there
        _iconRetry.Tick += (_, _) =>
        {
            if (TryShowCalendarAppIcon() || ++_iconAttempts > 20) _iconRetry.Stop();
        };
        Loaded += (_, _) =>
        {
            if (!TryShowCalendarAppIcon()) _iconRetry.Start();
        };
    }

    /// <summary>Use the Windows Calendar app's own icon (exact "Calendar" entry in Start, else one containing it).</summary>
    private bool TryShowCalendarAppIcon()
    {
        var apps = AppServices.Catalog.Apps;
        if (apps.Count == 0) return false;
        var app = apps.FirstOrDefault(a => a.Name.Equals("Calendar", StringComparison.OrdinalIgnoreCase))
                  ?? apps.FirstOrDefault(a => a.Name.Contains("Calendar", StringComparison.OrdinalIgnoreCase));
        if (app is null) return true; // catalog loaded, no Calendar app: keep the drawn page

        var icon = Facet.Interop.ShellInterop.GetShellIcon(app.ParsingName, ThemeManager.IconPixels * 2);
        if (icon is null) return true;
        CalendarAppIcon.Source = icon;
        CalendarAppIcon.Visibility = Visibility.Visible;
        MiniCalendar.Visibility = Visibility.Collapsed;
        return true;
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void Update()
    {
        var today = DateTime.Today;
        Dow.Text = today.ToString("ddd", CultureInfo.CurrentCulture).ToUpperInvariant();
        DayNumber.Text = today.Day.ToString(CultureInfo.CurrentCulture);

        var next = AppServices.Calendar.NextEvent;
        ShowJoin(next);
        if (next is null)
        {
            EventTitle.Text = "No more events";
            When.Text = "Click to add one";
            Bar.Visibility = Visibility.Collapsed;
            Btn.ToolTip = "Calendar";
        }
        else
        {
            EventTitle.Text = next.Title;
            When.Text = AppServices.Calendar.NextWhen;
            Bar.Visibility = Visibility.Visible;
            Bar.Background = ThemeManager.Brush(next.Color ?? CalendarService.LocalColor);
            Btn.ToolTip = $"{next.Title}\n{next.Start:t} – {next.End:t}{(string.IsNullOrWhiteSpace(next.Location) ? "" : "\n" + next.Location)}";
        }
    }

    private static readonly TimeSpan JoinLead = TimeSpan.FromMinutes(10);
    private string? _joinUrl;

    /// <summary>A Join button while the next event is an online meeting about to start (or under way).</summary>
    private void ShowJoin(Core.Calendar.CalendarEvent? next)
    {
        var url = next is null ? null : next.MeetingUrl ?? Core.Calendar.MeetingLink.Find(next.Location);
        var show = url is not null && next!.Start - DateTime.Now <= JoinLead && next.End > DateTime.Now;
        _joinUrl = show ? url : null;
        var wasShown = JoinButton.Visibility == Visibility.Visible;
        JoinButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        JoinButton.ToolTip = $"Join on {Core.Calendar.MeetingLink.Service(url!)}";
        if (!wasShown && IsLoaded)
        {
            Motion.To(JoinScale, System.Windows.Media.ScaleTransform.ScaleXProperty, 1, Motion.Slow, Motion.Spring, from: 0.4);
            Motion.To(JoinScale, System.Windows.Media.ScaleTransform.ScaleYProperty, 1, Motion.Slow, Motion.Spring, from: 0.4);
        }
    }

    private void Join_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // not the tile's own click (the calendar panel)
        if (_joinUrl is not null) AppServices.Open(_joinUrl);
    }

    private void Btn_Click(object sender, RoutedEventArgs e) => PopoverWindow.Toggle(Btn, () => new CalendarPanel());
}
