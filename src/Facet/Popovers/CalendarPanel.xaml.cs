using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Facet.Core.Calendar;
using Facet.Infrastructure;
using Facet.Services;
using Facet.Theme;

namespace Facet.Popovers;

public partial class CalendarPanel : UserControl
{
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _selected = DateTime.Today;

    public CalendarPanel()
    {
        InitializeComponent();
        BuildWeekdayHeader();
        Render();
        AppServices.Calendar.Changed += OnCalendarChanged;
        Unloaded += (_, _) => AppServices.Calendar.Changed -= OnCalendarChanged;
        Loaded += (_, _) => NewTitle.Focus();
    }

    private void OnCalendarChanged() => Dispatcher.BeginInvoke(Render);

    private void BuildWeekdayHeader()
    {
        var names = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;
        for (var i = 0; i < 7; i++)
        {
            WeekdayHeader.Children.Add(new TextBlock
            {
                Text = names[(i + 1) % 7].ToUpperInvariant(),
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("SurfaceSubtle"),
            });
        }
    }

    private void Render()
    {
        MonthTitle.Text = _month.ToString("MMMM, yyyy", CultureInfo.CurrentCulture);

        // Monday-first grid of 6 weeks
        var offset = ((int)_month.DayOfWeek + 6) % 7;
        var gridStart = _month.AddDays(-offset);
        var gridEnd = gridStart.AddDays(42);
        var events = AppServices.Calendar.GetEvents(gridStart, gridEnd);

        DayGrid.Children.Clear();
        for (var i = 0; i < 42; i++)
        {
            var day = gridStart.AddDays(i);
            var dayEvents = events.Where(e => OccursOn(e, day)).ToList();
            DayGrid.Children.Add(BuildDayCell(day, dayEvents));
        }

        BuildLegend(events);
        var status = AppServices.Calendar.FeedStatus;
        SyncStatus.Text = string.IsNullOrEmpty(status) ? "Add iCal links in Settings → Calendar to see Google / Outlook / iCloud events." : status;
        RenderAgenda();
    }

    private static bool OccursOn(CalendarEvent e, DateTime day) =>
        e.Start.Date <= day && (e.End > day || e.Start.Date == day) && !(e.AllDay && e.End.Date == day && e.Start.Date != day);

    private FrameworkElement BuildDayCell(DateTime day, List<CalendarEvent> dayEvents)
    {
        var isToday = day == DateTime.Today;
        var isSelected = day == _selected;
        var inMonth = day.Month == _month.Month;

        var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Height = 6 };
        foreach (var color in dayEvents.Select(e => e.Color ?? CalendarService.LocalColor).Distinct().Take(3))
            dots.Children.Add(new Border { Width = 5, Height = 5, CornerRadius = new CornerRadius(2.5), Margin = new Thickness(1.5, 0, 1.5, 0), Background = ThemeManager.Brush(color) });
        if (dayEvents.Count > 3)
            dots.Children.Add(new TextBlock { Text = "+", FontSize = 8, Margin = new Thickness(1, -3, 0, 0), Foreground = (Brush)FindResource("SurfaceSubtle") });

        var number = new TextBlock
        {
            Text = day.Day.ToString(CultureInfo.CurrentCulture),
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 14,
            FontWeight = isToday ? FontWeights.Bold : FontWeights.SemiBold,
            Foreground = isToday ? Brushes.White : (Brush)FindResource("SurfaceForeground"),
        };
        var numberHost = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = isToday ? (Brush)FindResource("Accent") : Brushes.Transparent,
            Child = number,
            Padding = new Thickness(0, 3, 0, 0),
        };

        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(dots);
        content.Children.Add(numberHost);

        var cell = new Border
        {
            Height = 50,
            Margin = new Thickness(3),
            CornerRadius = new CornerRadius(12),
            Background = (Brush)FindResource(inMonth ? "SurfaceCard" : "SurfaceInput"),
            BorderThickness = new Thickness(isSelected ? 2 : 0),
            BorderBrush = (Brush)FindResource("Accent"),
            Opacity = inMonth ? 1 : 0.45,
            Child = content,
            Cursor = Cursors.Hand,
            ToolTip = dayEvents.Count == 0 ? null : string.Join("\n", dayEvents.Take(6).Select(e => $"{(e.AllDay ? "All day" : e.Start.ToString("t"))}  {e.Title}")),
        };
        cell.MouseLeftButtonUp += (_, _) =>
        {
            _selected = day;
            if (day.Month != _month.Month) _month = new DateTime(day.Year, day.Month, 1);
            Render();
        };
        return cell;
    }

    private void BuildLegend(IReadOnlyList<CalendarEvent> events)
    {
        Legend.Children.Clear();
        AddLegend(CalendarService.LocalColor, "Facet");
        var feeds = events.Where(e => e.Source.StartsWith("feed", StringComparison.Ordinal))
                          .Select(e => (e.Source, e.Color)).Distinct().OrderBy(f => f.Source);
        foreach (var (source, color) in feeds)
        {
            var index = int.TryParse(source.AsSpan(4), out var n) ? n : 0;
            var url = index < AppServices.Settings.Current.ICalUrls.Count ? AppServices.Settings.Current.ICalUrls[index] : source;
            AddLegend(color ?? "#888888", FeedLabel(url));
        }
    }

    private static string FeedLabel(string url)
    {
        if (url.Contains("google", StringComparison.OrdinalIgnoreCase)) return "Google";
        if (url.Contains("outlook", StringComparison.OrdinalIgnoreCase) || url.Contains("office", StringComparison.OrdinalIgnoreCase)) return "Outlook";
        if (url.Contains("icloud", StringComparison.OrdinalIgnoreCase)) return "iCloud";
        return Uri.TryCreate(url.Replace("webcal://", "https://"), UriKind.Absolute, out var u) ? u.Host : "Calendar";
    }

    private void AddLegend(string color, string label)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
        sp.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), Background = ThemeManager.Brush(color), VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(new TextBlock { Text = label, Margin = new Thickness(5, 0, 0, 0), FontSize = 11.5, Foreground = (Brush)FindResource("SurfaceSubtle") });
        Legend.Children.Add(sp);
    }

    private void RenderAgenda()
    {
        AgendaTitle.Text = _selected == DateTime.Today ? "Today"
            : _selected == DateTime.Today.AddDays(1) ? "Tomorrow"
            : _selected.ToString("ddd, MMM d", CultureInfo.CurrentCulture);

        var items = AppServices.Calendar.GetEvents(_selected, _selected.AddDays(1)).Where(e => OccursOn(e, _selected)).ToList();
        AgendaCount.Text = items.Count == 1 ? "1 event" : $"{items.Count} events";
        AgendaList.Children.Clear();

        if (items.Count == 0)
        {
            AgendaList.Children.Add(new TextBlock
            {
                Text = "Nothing scheduled.\nAdd something below.",
                Style = (Style)FindResource("Caption"),
                Margin = new Thickness(0, 8, 0, 0),
            });
            return;
        }

        var now = DateTime.Now;
        foreach (var e in items)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10), Background = Brushes.Transparent };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var past = !e.AllDay && e.End < now;
            row.Opacity = past ? 0.5 : 1;

            row.Children.Add(new TextBlock
            {
                Text = e.AllDay ? "All day" : e.Start.ToString("t", CultureInfo.CurrentCulture),
                FontSize = 11.5,
                Foreground = (Brush)FindResource("SurfaceSubtle"),
                Margin = new Thickness(0, 1, 0, 0),
            });

            var bar = new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Background = ThemeManager.Brush(e.Color ?? CalendarService.LocalColor) };
            Grid.SetColumn(bar, 1);
            row.Children.Add(bar);

            var text = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            text.Children.Add(new TextBlock { Text = e.Title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            var sub = e.AllDay ? e.Location : $"{e.Start:t} – {e.End:t}{(string.IsNullOrWhiteSpace(e.Location) ? "" : " · " + e.Location)}";
            if (!string.IsNullOrWhiteSpace(sub))
                text.Children.Add(new TextBlock { Text = sub, FontSize = 11.5, Foreground = (Brush)FindResource("SurfaceSubtle"), TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(text, 2);
            row.Children.Add(text);

            if (AppServices.Calendar.IsLocal(e))
            {
                var menu = new ContextMenu();
                var delete = new MenuItem { Header = "Delete event" };
                var captured = e;
                delete.Click += (_, _) => AppServices.Calendar.Remove(captured);
                menu.Items.Add(delete);
                row.ContextMenu = menu;
            }

            AgendaList.Children.Add(row);
        }
    }

    private void Today_Click(object sender, RoutedEventArgs e)
    {
        _selected = DateTime.Today;
        _month = new DateTime(_selected.Year, _selected.Month, 1);
        Render();
    }

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        _month = _month.AddMonths(-1);
        Render();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        _month = _month.AddMonths(1);
        Render();
    }

    private void NewTitle_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddEvent();
    }

    private void Add_Click(object sender, RoutedEventArgs e) => AddEvent();

    private void AddEvent()
    {
        var title = NewTitle.Text.Trim();
        if (title.Length == 0) { NewTitle.Focus(); return; }

        var timeText = string.IsNullOrWhiteSpace(NewTime.Text) ? null : NewTime.Text.Trim();
        if (timeText is null)
        {
            AppServices.Calendar.Add(title, _selected, TimeSpan.FromDays(1), allDay: true);
        }
        else if (DateTime.TryParse(timeText, CultureInfo.CurrentCulture, DateTimeStyles.NoCurrentDateDefault, out var t))
        {
            AppServices.Calendar.Add(title, _selected.Date + t.TimeOfDay, TimeSpan.FromHours(1));
        }
        else
        {
            NewTime.Focus();
            NewTime.SelectAll();
            return;
        }

        NewTitle.Clear();
        NewTime.Clear();
    }
}
