using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Facet.Core.Settings;
using Facet.Infrastructure;
using Facet.Interop;
using Facet.Services;
using Facet.Shell;
using Facet.Theme;
using Facet.Widgets;
using Microsoft.Win32;

namespace Facet.SettingsUi;

public partial class SettingsWindow : Window
{
    private static SettingsWindow? _instance;
    private bool _loading;

    private static FacetSettings S => AppServices.Settings.Current;

    public static void Open(string? page = null)
    {
        if (_instance is null)
        {
            _instance = new SettingsWindow();
            _instance.Show();
        }
        if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
        _instance.Activate();
        if (page is not null) _instance.Navigate(page);
    }

    private SettingsWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        VersionText.Text = $"Version {version}";
        AboutVersion.Text = $"Facet {version} · .NET {Environment.Version.ToString(2)} · WPF";
        DataPath.Text = SettingsService.DataDir;

        BuildChoices();
        LoadValues();
        Navigate("appearance");

        AppServices.Settings.Changed += OnSettingsChanged;
        Closed += (_, _) =>
        {
            AppServices.Settings.Changed -= OnSettingsChanged;
            _instance = null;
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var dark = ThemeManager.IsLightSurface(ThemeManager.Current) ? 0 : 1;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        var mica = 2; // DWMSBT_MAINWINDOW: Mica title bar on Windows 11
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref mica, sizeof(int));
    }

    private void OnSettingsChanged()
    {
        // something else (dock menu, palette) changed settings: refresh the non-text controls
        _loading = true;
        SyncChoices();
        SyncToggles();
        BuildWidgetList();
        _loading = false;
    }

    // ───────────────────────── navigation ─────────────────────────

    private void Navigate(string tag)
    {
        foreach (var page in new FrameworkElement[] { PageAppearance, PageDock, PageWidgets, PageWeather, PageAlerts, PageCustom, PageAbout })
            page.Visibility = (string)page.Tag == tag ? Visibility.Visible : Visibility.Collapsed;
        foreach (var nav in Nav.Children.OfType<RadioButton>())
            if ((string)nav.Tag == tag && nav.IsChecked != true) nav.IsChecked = true;
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag }) Navigate(tag);
    }

    // ───────────────────────── appearance choices ─────────────────────────

    private void BuildChoices()
    {
        foreach (var theme in Enum.GetValues<DockTheme>())
            ThemeChoices.Children.Add(Choice("theme", ThemeSwatch(theme), theme, () =>
            {
                S.Theme = theme;
                S.MatchWindowsMode = false; // picking a theme means this one, always
                Save();
            }));
        foreach (var size in Enum.GetValues<DockSize>())
            SizeChoices.Children.Add(Choice("size", new TextBlock { Text = size.ToString() }, size, () => { S.Size = size; Save(); }));
        LayoutChoices.Children.Add(Choice("layout", new TextBlock { Text = "Floating" }, DockLayout.Pill, () => { S.Layout = DockLayout.Pill; Save(); }));
        LayoutChoices.Children.Add(Choice("layout", new TextBlock { Text = "Full width" }, DockLayout.FullWidth, () => { S.Layout = DockLayout.FullWidth; Save(); }));
        EdgeChoices.Children.Add(Choice("edge", new TextBlock { Text = "Bottom" }, DockEdge.Bottom, () => { S.Edge = DockEdge.Bottom; Save(); }));
        EdgeChoices.Children.Add(Choice("edge", new TextBlock { Text = "Top" }, DockEdge.Top, () => { S.Edge = DockEdge.Top; Save(); }));
        foreach (var shortcut in Hotkeys.Choices)
            ShortcutChoices.Children.Add(Choice("shortcut", new TextBlock { Text = shortcut }, shortcut, () => { S.PaletteShortcut = shortcut; Save(); }));
    }

    private RadioButton Choice(string group, object content, object value, Action apply)
    {
        var rb = new RadioButton
        {
            Style = (Style)FindResource("Segment"),
            GroupName = group,
            Content = content,
            Tag = value,
            Margin = new Thickness(0, 0, 8, 8),
        };
        rb.Checked += (_, _) => { if (!_loading) apply(); };
        return rb;
    }

    private static FrameworkElement ThemeSwatch(DockTheme theme)
    {
        Brush fill = theme switch
        {
            DockTheme.LiquidGlass => new LinearGradientBrush(Color.FromArgb(0xB0, 0x3A, 0x3A, 0x40), Color.FromArgb(0x80, 0x1E, 0x1E, 0x22), 30),
            DockTheme.Frost => new LinearGradientBrush(Color.FromArgb(0xE0, 0xF6, 0xF6, 0xF9), Color.FromArgb(0xB0, 0xDA, 0xDA, 0xE0), 30),
            DockTheme.Midnight => new LinearGradientBrush(Color.FromRgb(0x1B, 0x24, 0x36), Color.FromRgb(0x0C, 0x11, 0x1C), 90),
            DockTheme.Black => Brushes.Black,
            _ => Brushes.White,
        };
        var sp = new StackPanel { Width = 104 };
        sp.Children.Add(new Border
        {
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = fill,
            BorderBrush = ThemeManager.Brush("#40FFFFFF"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 8),
        });
        sp.Children.Add(new TextBlock { Text = DockWindow.ThemeLabel(theme), HorizontalAlignment = HorizontalAlignment.Center });
        return sp;
    }

    private void SyncChoices()
    {
        Check(ThemeChoices, S.Theme);
        Check(SizeChoices, S.Size);
        Check(LayoutChoices, S.Layout);
        Check(EdgeChoices, S.Edge);
        Check(ShortcutChoices, S.PaletteShortcut);
        ShortcutStatus.Text = Hotkeys.Status;
    }

    private static void Check(Panel panel, object value)
    {
        foreach (var rb in panel.Children.OfType<RadioButton>()) rb.IsChecked = Equals(rb.Tag, value);
    }

    // ───────────────────────── values ─────────────────────────

    private void LoadValues()
    {
        _loading = true;
        SyncChoices();
        SyncToggles();

        LeadMinutes.Text = S.Alerts.EventLeadMinutes.ToString();
        BatteryThreshold.Text = S.Alerts.BatteryThreshold.ToString();
        CpuThreshold.Text = S.Alerts.CpuThreshold.ToString();
        QuietStart.Text = S.QuietStart;
        QuietEnd.Text = S.QuietEnd;
        FocusMinutes.Text = S.PomodoroFocusMinutes.ToString();
        BreakMinutes.Text = S.PomodoroBreakMinutes.ToString();
        City.Text = S.WeatherCity ?? "";
        CityStatus.Text = S.WeatherLatitude is double lat && S.WeatherLongitude is double lon
            ? $"Using {S.WeatherCity} ({lat:0.00}, {lon:0.00})"
            : "No city set — Facet will try your Windows location.";
        ICalUrls.Text = string.Join(Environment.NewLine, S.ICalUrls);
        CalendarStatus.Text = AppServices.Calendar.FeedStatus;

        BuildWidgetList();
        ReloadCustomList();
        _loading = false;
    }

    private void SyncToggles()
    {
        Clock24.IsChecked = S.Clock24;
        ShowNotifications.IsChecked = S.ShowNotifications;
        Animations.IsChecked = S.Animations;
        Magnify.IsChecked = S.Magnify;
        WindowPreviews.IsChecked = S.WindowPreviews;
        MatchWindowsMode.IsChecked = S.MatchWindowsMode;
        WindowsAccent.IsChecked = S.WindowsAccent;
        AutoHide.IsChecked = S.AutoHide;
        HideTaskbar.IsChecked = S.HideTaskbar;
        HideInFullscreen.IsChecked = S.HideInFullscreen;
        FollowPointerDisplay.IsChecked = S.FollowPointerDisplay;
        LaunchAtStartup.IsChecked = S.LaunchAtStartup;
        PaletteHotkey.IsChecked = S.PaletteHotkey;
        Fahrenheit.IsChecked = S.Fahrenheit;
        EventSoon.IsChecked = S.Alerts.EventSoon;
        BatteryLow.IsChecked = S.Alerts.BatteryLow;
        CpuHot.IsChecked = S.Alerts.CpuHot;
        QuietEnabled.IsChecked = S.QuietHoursEnabled;
        QuietDuringFocus.IsChecked = S.QuietDuringFocus;
        HiddenPinsText.Text = S.HiddenPins.Count == 0 ? "None hidden" : $"{S.HiddenPins.Count} hidden from the dock";
    }

    private void Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        S.Clock24 = Clock24.IsChecked == true;
        S.ShowNotifications = ShowNotifications.IsChecked == true;
        S.Animations = Animations.IsChecked == true;
        S.Magnify = Magnify.IsChecked == true;
        S.WindowPreviews = WindowPreviews.IsChecked == true;
        S.MatchWindowsMode = MatchWindowsMode.IsChecked == true;
        S.WindowsAccent = WindowsAccent.IsChecked == true;
        S.AutoHide = AutoHide.IsChecked == true;
        S.HideTaskbar = HideTaskbar.IsChecked == true;
        S.HideInFullscreen = HideInFullscreen.IsChecked == true;
        S.FollowPointerDisplay = FollowPointerDisplay.IsChecked == true;
        S.LaunchAtStartup = LaunchAtStartup.IsChecked == true;
        S.PaletteHotkey = PaletteHotkey.IsChecked == true;
        S.Fahrenheit = Fahrenheit.IsChecked == true;
        S.Alerts.EventSoon = EventSoon.IsChecked == true;
        S.Alerts.BatteryLow = BatteryLow.IsChecked == true;
        S.Alerts.CpuHot = CpuHot.IsChecked == true;
        S.QuietHoursEnabled = QuietEnabled.IsChecked == true;
        S.QuietDuringFocus = QuietDuringFocus.IsChecked == true;

        if (int.TryParse(LeadMinutes.Text, out var lead)) S.Alerts.EventLeadMinutes = lead;
        if (int.TryParse(BatteryThreshold.Text, out var bat)) S.Alerts.BatteryThreshold = bat;
        if (int.TryParse(CpuThreshold.Text, out var cpu)) S.Alerts.CpuThreshold = cpu;
        if (int.TryParse(FocusMinutes.Text, out var focus)) S.PomodoroFocusMinutes = focus;
        if (int.TryParse(BreakMinutes.Text, out var brk)) S.PomodoroBreakMinutes = brk;
        S.QuietStart = QuietStart.Text.Trim();
        S.QuietEnd = QuietEnd.Text.Trim();
        Save();

        // reflect clamping / fallback parsing back into the fields
        _loading = true;
        LeadMinutes.Text = S.Alerts.EventLeadMinutes.ToString();
        BatteryThreshold.Text = S.Alerts.BatteryThreshold.ToString();
        CpuThreshold.Text = S.Alerts.CpuThreshold.ToString();
        FocusMinutes.Text = S.PomodoroFocusMinutes.ToString();
        BreakMinutes.Text = S.PomodoroBreakMinutes.ToString();
        QuietStart.Text = S.QuietStartTime.ToString(@"hh\:mm");
        QuietEnd.Text = S.QuietEndTime.ToString(@"hh\:mm");
        _loading = false;
    }

    private static void Save() => AppServices.Settings.Save();

    private void RestorePins_Click(object sender, RoutedEventArgs e)
    {
        S.HiddenPins.Clear();
        Save();
    }

    // ───────────────────────── widgets ─────────────────────────

    private void BuildWidgetList()
    {
        BuildSlotList(WidgetList, S.Widgets, id => new TextBlock { Text = WidgetFactory.DisplayName(id), VerticalAlignment = VerticalAlignment.Center });
        BuildSlotList(ConnectItemList, S.ConnectItems, id =>
        {
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = ConnectItems.Name(id), FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = ConnectItems.About(id), Style = (Style)FindResource("Caption"), TextWrapping = TextWrapping.Wrap });
            return text;
        });
    }

    /// <summary>One row per slot: an on/off switch, and arrows that move it earlier or later in the list.</summary>
    private void BuildSlotList(Panel host, List<WidgetSlot> slots, Func<string, FrameworkElement> label)
    {
        host.Children.Clear();
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var index = i;
            var row = new Border { Style = (Style)FindResource("Row"), Padding = new Thickness(14, 8, 10, 8) };
            var dock = new DockPanel();

            var down = GlyphButton("", "Move later", index < slots.Count - 1, () => Move(slots, index, 1));
            var up = GlyphButton("", "Move earlier", index > 0, () => Move(slots, index, -1));
            DockPanel.SetDock(down, Dock.Right);
            DockPanel.SetDock(up, Dock.Right);
            dock.Children.Add(down);
            dock.Children.Add(up);

            var toggle = new CheckBox
            {
                Style = (Style)FindResource("Switch"),
                Content = label(slot.Id),
                IsChecked = slot.Enabled,
                Margin = new Thickness(0, 0, 12, 0),
            };
            toggle.Click += (_, _) =>
            {
                slot.Enabled = toggle.IsChecked == true;
                Save();
            };
            dock.Children.Add(toggle);
            row.Child = dock;
            host.Children.Add(row);
        }
    }

    private Button GlyphButton(string glyph, string tip, bool enabled, Action action)
    {
        var b = new Button
        {
            Style = (Style)FindResource("SurfaceGlyphButton"),
            Content = glyph,
            ToolTip = tip,
            IsEnabled = enabled,
            Margin = new Thickness(4, 0, 0, 0),
        };
        b.Click += (_, _) => action();
        return b;
    }

    private static void Move(List<WidgetSlot> slots, int index, int delta)
    {
        var target = index + delta;
        if (target < 0 || target >= slots.Count) return;
        (slots[index], slots[target]) = (slots[target], slots[index]);
        Save();
    }

    // ───────────────────────── weather & calendar ─────────────────────────

    private async void FindCity_Click(object sender, RoutedEventArgs e) => await FindCityAsync();

    private async void City_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await FindCityAsync();
    }

    private async Task FindCityAsync()
    {
        var query = City.Text.Trim();
        if (query.Length == 0) return;
        CityStatus.Text = "Searching…";
        try
        {
            var result = await WeatherService.GeocodeAsync(query);
            if (result is not { } r)
            {
                CityStatus.Text = $"Couldn't find “{query}”. Try adding the country.";
                return;
            }
            S.WeatherCity = r.Name;
            S.WeatherLatitude = r.Lat;
            S.WeatherLongitude = r.Lon;
            City.Text = r.Name;
            CityStatus.Text = $"Using {r.Name} ({r.Lat:0.00}, {r.Lon:0.00})";
            Save();
            _ = AppServices.Weather.RefreshAsync(force: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            CityStatus.Text = "Couldn't reach the geocoding service. Check your connection.";
        }
    }

    private void UseLocation_Click(object sender, RoutedEventArgs e)
    {
        S.WeatherCity = null;
        S.WeatherLatitude = null;
        S.WeatherLongitude = null;
        City.Text = "";
        CityStatus.Text = "Using your Windows location (if location access is allowed for desktop apps).";
        Save();
        _ = AppServices.Weather.RefreshAsync(force: true);
    }

    private void ICal_LostFocus(object sender, RoutedEventArgs e) => SaveICal();

    private void SaveICal()
    {
        var urls = ICalUrls.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Where(u => u.StartsWith("http", StringComparison.OrdinalIgnoreCase) || u.StartsWith("webcal", StringComparison.OrdinalIgnoreCase))
                                .Distinct()
                                .ToList();
        if (urls.SequenceEqual(S.ICalUrls)) return;
        S.ICalUrls = urls;
        Save();
    }

    private async void SyncCalendars_Click(object sender, RoutedEventArgs e)
    {
        SaveICal();
        CalendarStatus.Text = "Syncing…";
        await AppServices.Calendar.RefreshFeedsAsync();
        CalendarStatus.Text = string.IsNullOrEmpty(AppServices.Calendar.FeedStatus) ? "No calendars added." : AppServices.Calendar.FeedStatus;
    }

    // ───────────────────────── custom widgets ─────────────────────────

    private void ReloadCustomList(CustomWidgetConfig? select = null)
    {
        CustomList.ItemsSource = null;
        CustomList.ItemsSource = S.CustomWidgets;
        if (select is not null) CustomList.SelectedItem = select;
        CustomEditor.IsEnabled = CustomList.SelectedItem is not null;
    }

    private CustomWidgetConfig? SelectedCustom => CustomList.SelectedItem as CustomWidgetConfig;

    private void CustomList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var c = SelectedCustom;
        CustomEditor.IsEnabled = c is not null;
        CwTestResult.Text = "";
        if (c is null) return;
        CwName.Text = c.Name;
        CwKindCommand.IsChecked = c.Kind == CustomWidgetKind.Command;
        CwKindUrl.IsChecked = c.Kind == CustomWidgetKind.Url;
        CwSource.Text = c.Source;
        CwPath.Text = c.JsonPath ?? "";
        CwInterval.Text = c.IntervalSeconds.ToString();
        CwPrefix.Text = c.Prefix ?? "";
        CwSuffix.Text = c.Suffix ?? "";
        CwClick.Text = c.ClickTarget ?? "";
    }

    private CustomWidgetConfig ReadEditor(CustomWidgetConfig into)
    {
        into.Name = string.IsNullOrWhiteSpace(CwName.Text) ? "Custom" : CwName.Text.Trim();
        into.Kind = CwKindUrl.IsChecked == true ? CustomWidgetKind.Url : CustomWidgetKind.Command;
        into.Source = CwSource.Text.Trim();
        into.JsonPath = string.IsNullOrWhiteSpace(CwPath.Text) ? null : CwPath.Text.Trim();
        into.IntervalSeconds = int.TryParse(CwInterval.Text, out var sec) ? sec : 60;
        into.Prefix = string.IsNullOrEmpty(CwPrefix.Text) ? null : CwPrefix.Text;
        into.Suffix = string.IsNullOrEmpty(CwSuffix.Text) ? null : CwSuffix.Text;
        into.ClickTarget = string.IsNullOrWhiteSpace(CwClick.Text) ? null : CwClick.Text.Trim();
        return into;
    }

    private void AddCustom_Click(object sender, RoutedEventArgs e)
    {
        var c = new CustomWidgetConfig { Name = "Disk C:", Kind = CustomWidgetKind.Command,
            Source = "powershell -NoProfile -Command \"'{0:N0} GB free' -f ((Get-PSDrive C).Free/1GB)\"", IntervalSeconds = 300 };
        S.CustomWidgets.Add(c);
        Save();
        ReloadCustomList(c);
    }

    private void AddExample_Click(object sender, RoutedEventArgs e)
    {
        var c = new CustomWidgetConfig
        {
            Name = "BTC",
            Kind = CustomWidgetKind.Url,
            Source = "https://api.coinbase.com/v2/prices/BTC-USD/spot",
            JsonPath = "data.amount",
            Prefix = "$",
            IntervalSeconds = 120,
            ClickTarget = "https://www.coinbase.com/price/bitcoin",
        };
        S.CustomWidgets.Add(c);
        Save();
        ReloadCustomList(c);
    }

    private void RemoveCustom_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCustom is not { } c) return;
        S.CustomWidgets.Remove(c);
        Save();
        ReloadCustomList();
    }

    private void SaveCustom_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCustom is not { } c) return;
        ReadEditor(c);
        Save();
        ReloadCustomList(c);
        CwTestResult.Text = "Saved.";
    }

    private async void TestCustom_Click(object sender, RoutedEventArgs e)
    {
        var probe = ReadEditor(new CustomWidgetConfig());
        CwTestResult.Text = "Running…";
        var vm = new CustomWidgetViewModel(probe);
        await vm.RefreshAsync();
        CwTestResult.Text = $"→ {vm.Value}";
        CwTestResult.ToolTip = vm.Tooltip;
    }

    // ───────────────────────── data ─────────────────────────

    private void OpenData_Click(object sender, RoutedEventArgs e) => AppServices.Open(SettingsService.DataDir);

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { FileName = "facet-settings.json", Filter = "JSON|*.json" };
        if (dlg.ShowDialog(this) == true) AppServices.Settings.Export(dlg.FileName);
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "JSON|*.json" };
        if (dlg.ShowDialog(this) != true) return;
        if (AppServices.Settings.Import(dlg.FileName)) LoadValues();
    }

    private void Quit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
