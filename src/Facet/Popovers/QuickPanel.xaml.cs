using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Facet.Infrastructure;
using Facet.Services;
using Facet.SettingsUi;

namespace Facet.Popovers;

/// <summary>The dock's expanded controls: volume, microphone, do not disturb, Wi-Fi, Bluetooth, project, battery.</summary>
public partial class QuickPanel : UserControl
{
    private bool _syncing;

    public QuickPanel()
    {
        InitializeComponent();
        Sync();
        AppServices.Audio.PropertyChanged += OnChanged;
        AppServices.Alerts.PropertyChanged += OnChanged;
        AppServices.Connectivity.PropertyChanged += OnChanged;
        AppServices.System.PropertyChanged += OnSystemChanged;
        Unloaded += (_, _) =>
        {
            AppServices.Audio.PropertyChanged -= OnChanged;
            AppServices.Alerts.PropertyChanged -= OnChanged;
            AppServices.Connectivity.PropertyChanged -= OnChanged;
            AppServices.System.PropertyChanged -= OnSystemChanged;
        };
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => Sync();

    private void OnSystemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemMonitor.BatteryPercent) or nameof(SystemMonitor.Charging) or nameof(SystemMonitor.HasBattery))
            Sync();
    }

    private void Sync()
    {
        _syncing = true;
        var audio = AppServices.Audio;
        if (!VolumeSlider.IsMouseCaptureWithin) VolumeSlider.Value = audio.Volume;
        VolumeValue.Text = audio.Muted ? "Off" : $"{audio.Volume:0}%";
        MuteButton.Content = audio.VolumeGlyph;

        MicTile.Tag = audio.MicGlyph;
        MicTile.IsEnabled = audio.HasMic;
        MicTile.IsChecked = audio.HasMic && !audio.MicMuted;
        MicState.Text = !audio.HasMic ? "Not found" : audio.MicMuted ? "Muted" : "On";

        var alerts = AppServices.Alerts;
        DndTile.IsChecked = alerts.DoNotDisturb;
        DndState.Text = alerts.DoNotDisturb ? "On" : alerts.QuietNow ? "Quiet hours" : "Off";

        var net = AppServices.Connectivity.State;
        WifiTile.IsEnabled = net.HasWifi;
        WifiTile.IsChecked = net.HasWifi && net.WifiOn;
        WifiState.Text = !net.HasWifi ? "No adapter" : net.NetworkSummary;
        WifiTile.ToolTip = net.Kind == NetworkKind.WiFi ? $"{net.NetworkSummary} · signal {net.SignalPercent}%" : null;
        BluetoothTile.IsEnabled = net.HasBluetooth;
        BluetoothTile.IsChecked = net.HasBluetooth && net.BluetoothOn;
        BluetoothState.Text = !net.HasBluetooth ? "No adapter" : net.BluetoothOn ? "On" : "Off";

        var power = AppServices.System;
        BatteryTile.Visibility = power.HasBattery ? Visibility.Visible : Visibility.Collapsed;
        // Segoe Fluent battery glyphs in tenths: EBA0.. discharging, EBAB.. charging
        var tenth = (int)Math.Round(Math.Clamp(power.BatteryPercent, 0, 100) / 10.0);
        BatteryTile.Tag = ((char)((power.Charging ? 0xEBAB : 0xEBA0) + tenth)).ToString();
        BatteryState.Text = $"{power.BatteryPercent}%{(power.Charging ? " · Charging" : "")}";
        _syncing = false;
    }

    private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing) return;
        AppServices.Audio.SetVolume(e.NewValue);
        VolumeValue.Text = $"{e.NewValue:0}%";
    }

    private void Mute_Click(object sender, RoutedEventArgs e) => AppServices.Audio.ToggleMute();

    private void Mic_Click(object sender, RoutedEventArgs e)
    {
        AppServices.Audio.ToggleMic();
        Sync();
    }

    private void Dnd_Click(object sender, RoutedEventArgs e)
    {
        AppServices.Alerts.DoNotDisturb = DndTile.IsChecked == true;
        Sync();
    }

    // re-sync afterwards: if Windows refused the switch, the tile flips back to the real state
    private async void Wifi_Click(object sender, RoutedEventArgs e)
    {
        await AppServices.Connectivity.SetWifiAsync(WifiTile.IsChecked == true);
        Sync();
    }

    private async void Bluetooth_Click(object sender, RoutedEventArgs e)
    {
        await AppServices.Connectivity.SetBluetoothAsync(BluetoothTile.IsChecked == true);
        Sync();
    }

    /// <summary>The Wi-Fi tile's ">" end: Windows' network list. Handled, so the tile itself doesn't switch.</summary>
    private void WifiNetworks_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        PopoverWindow.CloseCurrent();
        AppServices.Connectivity.OpenNetworkList();
    }

    /// <summary>The Bluetooth tile's ">" end: Windows' device list. Handled, so the tile itself doesn't switch.</summary>
    private void BluetoothDevices_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        PopoverWindow.CloseCurrent();
        AppServices.Connectivity.OpenBluetoothDevices();
    }

    private void Project_Click(object sender, RoutedEventArgs e)
    {
        ProjectTile.IsChecked = false; // an action, not a toggle
        PopoverWindow.CloseCurrent();
        AppServices.Connectivity.OpenProject();
    }

    private void Battery_Click(object sender, RoutedEventArgs e)
    {
        BatteryTile.IsChecked = false;
        PopoverWindow.CloseCurrent();
        AppServices.Open("ms-settings:batterysaver");
    }

    private void Network_Click(object sender, RoutedEventArgs e) => AppServices.Connectivity.OpenNetworkSettings();
    private void BluetoothSettings_Click(object sender, RoutedEventArgs e) => AppServices.Connectivity.OpenBluetoothSettings();
    private void Sound_Click(object sender, RoutedEventArgs e) => AppServices.Open("ms-settings:sound");

    private void FacetSettings_Click(object sender, RoutedEventArgs e)
    {
        PopoverWindow.CloseCurrent();
        SettingsWindow.Open();
    }
}
