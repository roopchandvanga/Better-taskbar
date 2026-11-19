using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using Facet.Controls;
using Facet.Core.Settings;
using Facet.Infrastructure;
using Facet.Popovers;
using Facet.Services;
using Facet.SettingsUi;
using Facet.Shell;
using Facet.Theme;

namespace Facet.Widgets;

/// <summary>
/// The quick controls tile: the buttons turned on in <see cref="FacetSettings.ConnectItems"/>, in that order.
/// A button whose hardware is missing (no Bluetooth adapter, no microphone, no battery) stays out of sight.
/// </summary>
public partial class ConnectWidget : UserControl
{
    private static readonly Brush ChargingFill = ThemeManager.Brush("#34C759");
    private const string DndGlyphText = "";
    private const string ScreenshotGlyphText = "";
    private const double Dimmed = 0.45;

    private readonly Dictionary<string, Button> _items;
    private bool _batteryHasOwnRing;

    public ConnectWidget()
    {
        InitializeComponent();
        _items = Items.Children.OfType<Button>().ToDictionary(b => (string)b.Tag);
        BluetoothGlyph.Glyph = ConnectivityState.BluetoothGlyph;
        ProjectGlyph.Glyph = ConnectivityState.ProjectGlyph;
        DndGlyph.Glyph = DndGlyphText;
        ScreenshotGlyph.Glyph = ScreenshotGlyphText;
        Arrange();

        AppServices.Settings.Changed += Arrange;
        AppServices.Connectivity.PropertyChanged += OnChanged;
        AppServices.Audio.PropertyChanged += OnChanged;
        AppServices.Alerts.PropertyChanged += OnAlertsChanged;
        AppServices.System.PropertyChanged += OnSystemChanged;
        PopoverWindow.CurrentChanged += OnPopoverChanged;
        Unloaded += (_, _) =>
        {
            AppServices.Settings.Changed -= Arrange;
            AppServices.Connectivity.PropertyChanged -= OnChanged;
            AppServices.Audio.PropertyChanged -= OnChanged;
            AppServices.Alerts.PropertyChanged -= OnAlertsChanged;
            AppServices.System.PropertyChanged -= OnSystemChanged;
            PopoverWindow.CurrentChanged -= OnPopoverChanged;
        };
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void OnAlertsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AlertService.DoNotDisturb) or nameof(AlertService.QuietNow)) Update();
    }

    private void OnSystemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemMonitor.BatteryPercent) or nameof(SystemMonitor.Charging) or nameof(SystemMonitor.HasBattery))
            Update();
    }

    /// <summary>Show the chosen buttons in the chosen order.</summary>
    private void Arrange()
    {
        var s = AppServices.Settings.Current;
        var wanted = s.ConnectItems
            .Where(i => i.Enabled && _items.ContainsKey(i.Id))
            .Select(i => _items[i.Id])
            .ToList();
        if (!wanted.SequenceEqual(Items.Children.OfType<Button>()))
        {
            Items.Children.Clear();
            foreach (var b in wanted) Items.Children.Add(b);
        }
        _batteryHasOwnRing = wanted.Contains(Battery);
        // the panel opens away from the dock: up from a bottom dock, down from a top one
        ExpandGlyph.Glyph = s.Edge == DockEdge.Top ? "" : "";
        Update();
    }

    private void Update()
    {
        var net = AppServices.Connectivity.State;
        var power = AppServices.System;
        var audio = AppServices.Audio;
        var alerts = AppServices.Alerts;

        // network: its icon inside a ring. The ring is the battery (white, red when low, green while charging),
        // unless the battery has a ring of its own in this tile; then, and on a PC without a battery, a wireless
        // connection's signal strength fills it.
        var batteryInRing = power.HasBattery && !_batteryHasOwnRing;
        var ring = batteryInRing || net.HasSignal;
        NetworkRing.Visibility = ring ? Visibility.Visible : Visibility.Collapsed;
        NetworkGlyph.Visibility = ring ? Visibility.Collapsed : Visibility.Visible;
        var glyphOpacity = net.NetworkDimmed ? Dimmed : net.Kind != NetworkKind.None && !net.HasInternet ? 0.6 : 1;

        if (batteryInRing)
            ShowBattery(NetworkRing, power);
        else if (net.HasSignal)
        {
            NetworkRing.Value = net.SignalPercent;
            NetworkRing.Label = net.SignalPercent.ToString();
            NetworkRing.Fill = null;
            NetworkRing.WarnAt = 25;
        }
        NetworkRing.Glyph = net.NetworkGlyph;
        NetworkRing.GlyphOpacity = glyphOpacity;
        NetworkGlyph.Glyph = net.NetworkGlyph;
        NetworkGlyph.Opacity = glyphOpacity;
        Network.ToolTip = batteryInRing ? $"{power.BatterySummary}\n{net.NetworkTooltip}" : net.NetworkTooltip;

        // battery on its own: the same ring, a battery inside
        Battery.Visibility = power.HasBattery ? Visibility.Visible : Visibility.Collapsed;
        if (power.HasBattery)
        {
            ShowBattery(BatteryRing, power);
            // Segoe Fluent vertical battery glyphs in tenths: F5F2.. discharging, F5FD.. charging
            var tenth = (int)Math.Round(Math.Clamp(power.BatteryPercent, 0, 100) / 10.0);
            BatteryRing.Glyph = ((char)((power.Charging ? 0xF5FD : 0xF5F2) + tenth)).ToString();
            Battery.ToolTip = $"{power.BatterySummary}\nClick for power & battery settings";
        }

        Bluetooth.Visibility = net.HasBluetooth ? Visibility.Visible : Visibility.Collapsed;
        Bluetooth.Opacity = net.BluetoothOn ? 1 : Dimmed;
        Bluetooth.ToolTip = net.BluetoothTooltip;

        Volume.Visibility = audio.HasSpeakers ? Visibility.Visible : Visibility.Collapsed;
        VolumeGlyph.Glyph = audio.VolumeGlyph;
        Volume.Opacity = audio.Muted ? Dimmed : 1;
        Volume.ToolTip = audio.Muted
            ? "Sound is muted · click to unmute, scroll to change the volume"
            : $"Volume {audio.Volume:0}% · click to mute, scroll to change";

        Mic.Visibility = audio.HasMic ? Visibility.Visible : Visibility.Collapsed;
        MicGlyph.Glyph = audio.MicGlyph;
        Mic.Opacity = audio.MicMuted ? Dimmed : 1;
        Mic.ToolTip = audio.MicMuted ? "Microphone muted · click to unmute" : "Microphone on · click to mute";

        Dnd.Opacity = alerts.DoNotDisturb ? 1 : Dimmed;
        Dnd.ToolTip = alerts.DoNotDisturb ? "Do not disturb is on · click to turn it off"
            : alerts.QuietNow ? "Quiet hours: Facet alerts are held · click to turn on do not disturb"
            : "Do not disturb is off · click to hold Facet alerts";

        // nothing to show (everything turned off, or only hardware this PC lacks): no empty tile on the dock
        Visibility = Items.Children.OfType<UIElement>().Any(c => c.Visibility == Visibility.Visible)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static void ShowBattery(Gauge gauge, SystemMonitor power)
    {
        gauge.Value = power.BatteryPercent;
        gauge.Label = power.BatteryPercent.ToString();
        gauge.Fill = power.Charging ? ChargingFill : null;
        gauge.WarnAt = 20;
    }

    /// <summary>Turn the chevron over while this tile's panel is open.</summary>
    private void OnPopoverChanged()
    {
        var open = PopoverWindow.IsOpenFor(Btn);
        ExpandTurn.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(open ? 180 : 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase() });
    }

    private void Network_Click(object sender, RoutedEventArgs e) => AppServices.Connectivity.OpenNetworkList();

    private void Battery_Click(object sender, RoutedEventArgs e) => AppServices.Open("ms-settings:batterysaver");

    // like Wi-Fi: the device list (it has its own switch); on/off is also in the expanded panel
    private void Bluetooth_Click(object sender, RoutedEventArgs e) => AppServices.Connectivity.OpenBluetoothDevices();

    private void Project_Click(object sender, RoutedEventArgs e) => AppServices.Connectivity.OpenProject();

    private void Volume_Click(object sender, RoutedEventArgs e) => AppServices.Audio.ToggleMute();

    private void Volume_Wheel(object sender, MouseWheelEventArgs e)
    {
        AppServices.Audio.Step(e.Delta / 120.0 * 2); // 2% a notch
        e.Handled = true;
    }

    private void Mic_Click(object sender, RoutedEventArgs e) => AppServices.Audio.ToggleMic();

    private void Dnd_Click(object sender, RoutedEventArgs e) => AppServices.Alerts.DoNotDisturb = !AppServices.Alerts.DoNotDisturb;

    private void Screenshot_Click(object sender, RoutedEventArgs e) => AppServices.Open("ms-screenclip:");

    /// <summary>
    /// Clicks bubble: this sees the tile's own clicks, the expand button's, and (ignored) those of the
    /// other buttons. The panel is anchored on the whole tile either way.
    /// </summary>
    private void Btn_Click(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, Btn) && !ReferenceEquals(e.Source, Expand)) return;
        PopoverWindow.Toggle(Btn, () => new QuickPanel());
    }

    /// <summary>
    /// Right-click anywhere on the tile: shortcuts for the button under the cursor, then which buttons the
    /// tile shows (stays open, so several can be switched), then the settings page for their order.
    /// </summary>
    private void Tile_RightClick(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = Btn };
        var net = AppServices.Connectivity;
        switch (ItemUnder(e.OriginalSource as DependencyObject))
        {
            case "network":
                Add(menu, "Wi-Fi networks", net.OpenNetworkList);
                Add(menu, "Network settings…", net.OpenNetworkSettings);
                if (AppServices.System.HasBattery && !_batteryHasOwnRing)
                    Add(menu, "Power & battery settings…", () => AppServices.Open("ms-settings:batterysaver"));
                break;
            case "battery":
                Add(menu, "Power & battery settings…", () => AppServices.Open("ms-settings:batterysaver"));
                break;
            case "bluetooth":
                Add(menu, "Bluetooth devices", net.OpenBluetoothDevices);
                Add(menu, "Bluetooth settings…", net.OpenBluetoothSettings);
                break;
            case "volume":
            case "mic":
                Add(menu, "Sound settings…", () => AppServices.Open("ms-settings:sound"));
                break;
            case "dnd":
                Add(menu, "Alerts & quiet hours…", () => SettingsWindow.Open("alerts"));
                break;
        }
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());

        ConnectItems.AddChoices(menu);
        menu.Items.Add(new Separator());
        Add(menu, "Customize this tile…", () => SettingsWindow.Open("widgets"));

        MenuGuard.Watch(menu);
        menu.IsOpen = true;
        e.Handled = true; // not the dock's own menu
    }

    /// <summary>The id of the tile button that <paramref name="source"/> is part of, if any.</summary>
    private string? ItemUnder(DependencyObject? source)
    {
        for (var d = source; d is not null && !ReferenceEquals(d, Btn);
             d = d is Visual or Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is Button { Tag: string id } && _items.ContainsKey(id)) return id;
        return null;
    }

    private static void Add(ContextMenu menu, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }
}
