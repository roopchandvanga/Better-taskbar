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

    private void Btn_Click(object sender, RoutedEventArgs e) => PopoverWindow.Toggle(Btn, () => new CalendarPanel());
}
