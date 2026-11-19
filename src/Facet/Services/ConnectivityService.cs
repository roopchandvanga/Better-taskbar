using System.Diagnostics;
using System.Windows.Threading;
using Facet.Infrastructure;
using Facet.Interop;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;

namespace Facet.Services;

public enum NetworkKind { None, WiFi, Ethernet, Cellular }

/// <summary>One snapshot of the radios and the active network; compared as a whole so listeners update once.</summary>
public sealed record ConnectivityState(
    bool CanControlRadios,
    bool HasWifi, bool WifiOn,
    bool HasBluetooth, bool BluetoothOn,
    NetworkKind Kind, int SignalBars, int SignalPercent, string? NetworkName, bool HasInternet)
{
    /// <summary>Wireless connection whose strength the dock shows as a gauge.</summary>
    public bool HasSignal => Kind is NetworkKind.WiFi or NetworkKind.Cellular;

    // Segoe Fluent Icons
    public const string WifiFullGlyph = "";
    public const string BluetoothGlyph = "";
    public const string ProjectGlyph = "";

    /// <summary>The network icon (the gauge around it shows the strength of a wireless connection).</summary>
    public string NetworkGlyph => Kind switch
    {
        NetworkKind.Ethernet => "",
        NetworkKind.WiFi or NetworkKind.Cellular => WifiFullGlyph,
        _ => !HasWifi ? "" : WifiOn ? "" : WifiFullGlyph,
    };

    /// <summary>Nothing connected because Wi-Fi is off (or there's no Wi-Fi and no cable): draw the icon dimmed.</summary>
    public bool NetworkDimmed => Kind == NetworkKind.None && !(HasWifi && WifiOn);

    public string NetworkSummary => Kind switch
    {
        NetworkKind.WiFi => Join(NetworkName ?? "Connected", HasInternet ? null : "No internet"),
        NetworkKind.Cellular => Join(NetworkName ?? "Cellular", HasInternet ? null : "No internet"),
        NetworkKind.Ethernet => HasInternet ? "Ethernet" : "Ethernet · No internet",
        _ => !HasWifi ? "Not connected" : WifiOn ? "Not connected" : "Off",
    };

    public string NetworkTooltip => (Kind switch
    {
        NetworkKind.WiFi => $"Wi-Fi · {NetworkSummary} · signal {SignalPercent}%",
        NetworkKind.Cellular => $"Cellular · {NetworkSummary} · signal {SignalPercent}%",
        NetworkKind.Ethernet => NetworkSummary,
        _ => HasWifi ? "Wi-Fi · " + NetworkSummary : "Network · Not connected",
    }) + (HasWifi ? " · click for networks" : " · click for network settings");

    public string BluetoothTooltip => (BluetoothOn ? "Bluetooth is on" : "Bluetooth is off")
        + " · click for devices, right-click for settings";

    private static string Join(string a, string? b) => b is null ? a : $"{a} · {b}";
}

/// <summary>Wi-Fi and Bluetooth radios plus the active connection, for the dock's connect tile and quick settings.</summary>
public sealed class ConnectivityService : ObservableObject
{
    private static readonly TimeSpan RadioRescan = TimeSpan.FromSeconds(30);

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private Radio? _wifi, _bluetooth;
    private bool _canControl;
    private DateTime _lastRadioScan;
    private ConnectivityState _state = new(false, false, false, false, false, NetworkKind.None, 0, 0, null, false);

    public ConnectivityState State { get => _state; private set => Set(ref _state, value); }

    public void Start()
    {
        _timer.Tick += (_, _) => OnTick();
        NetworkInformation.NetworkStatusChanged += _ => _ui.BeginInvoke(Refresh);
        _ = ScanRadiosAsync();
        Refresh();
        _timer.Start();
    }

    /// <summary>Windows' own Wi-Fi network list (the quick-settings flyout).</summary>
    public void OpenNetworkList() =>
        AppServices.Open(State.HasWifi ? "ms-availablenetworks:" : "ms-settings:network-status");

    public void OpenNetworkSettings() =>
        AppServices.Open(State.HasWifi ? "ms-settings:network-wifi" : "ms-settings:network-status");

    public void OpenBluetoothSettings() => AppServices.Open("ms-settings:bluetooth");

    /// <summary>
    /// Windows' Bluetooth device list (quick settings' Bluetooth page, with its own on/off switch), the
    /// counterpart of <see cref="OpenNetworkList"/>. Same deep-link family the system tray uses for its
    /// cast / studio-effects pages.
    /// </summary>
    public void OpenBluetoothDevices() => AppServices.Open("ms-actioncenter:controlcenter/bluetooth");

    /// <summary>Windows' "Project" flyout (duplicate / extend / second screen only), same as Win+P.</summary>
    public void OpenProject() => Native.PressWindowsShortcut(0x50 /* P */);

    public Task SetWifiAsync(bool on) => SetRadioAsync(_wifi, on, "ms-settings:network-wifi");

    public Task SetBluetoothAsync(bool on) => SetRadioAsync(_bluetooth, on, "ms-settings:bluetooth");

    private async Task SetRadioAsync(Radio? radio, bool on, string fallbackSettings)
    {
        if (radio is null || !_canControl)
        {
            // radio control is blocked (privacy setting "Let desktop apps control device radios", or no adapter)
            AppServices.Open(fallbackSettings);
            return;
        }
        try
        {
            var result = await radio.SetStateAsync(on ? RadioState.On : RadioState.Off);
            if (result != RadioAccessStatus.Allowed) AppServices.Open(fallbackSettings);
        }
        catch (Exception ex)
        {
            // WinRT surfaces driver/radio failures as assorted exception types; fall back to Windows' own page
            Debug.WriteLine($"Radio switch failed: {ex.Message}");
            AppServices.Open(fallbackSettings);
        }
        Refresh();
    }

    private void OnTick()
    {
        // pick up a Bluetooth dongle / Wi-Fi adapter that appears later, and a changed privacy setting
        if ((_wifi is null || _bluetooth is null || !_canControl) && DateTime.UtcNow - _lastRadioScan > RadioRescan)
            _ = ScanRadiosAsync();
        Refresh();
    }

    private async Task ScanRadiosAsync()
    {
        _lastRadioScan = DateTime.UtcNow;
        try
        {
            _canControl = await Radio.RequestAccessAsync() == RadioAccessStatus.Allowed;
            var radios = await Radio.GetRadiosAsync();
            Attach(ref _wifi, radios.FirstOrDefault(r => r.Kind == RadioKind.WiFi));
            Attach(ref _bluetooth, radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Radio scan failed: {ex.Message}");
        }
        Refresh();
    }

    private void Attach(ref Radio? field, Radio? radio)
    {
        if (ReferenceEquals(field, radio)) return;
        if (field is not null) field.StateChanged -= OnRadioChanged;
        field = radio;
        if (field is not null) field.StateChanged += OnRadioChanged;
    }

    private void OnRadioChanged(Radio sender, object args) => _ui.BeginInvoke(Refresh);

    private void Refresh()
    {
        var kind = NetworkKind.None;
        var bars = 0;
        int? percent = null;
        string? name = null;
        var internet = false;
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            internet = profile?.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;

            if (profile is { IsWwanConnectionProfile: true })
            {
                kind = NetworkKind.Cellular;
                bars = profile.GetSignalBars() ?? 0;
                name = profile.ProfileName;
            }
            else
            {
                // the internet profile can be a VPN/tunnel riding on Wi-Fi: then show the Wi-Fi underneath
                var isCable = profile?.NetworkAdapter?.IanaInterfaceType == 6; // ethernetCsmacd
                var wlan = profile is { IsWlanConnectionProfile: true } ? profile : isCable ? null : ConnectedWlan();
                if (wlan is not null)
                {
                    kind = NetworkKind.WiFi;
                    bars = wlan.GetSignalBars() ?? 0;
                    percent = WlanInterop.ConnectedSignalQuality();
                    var ssid = wlan.WlanConnectionProfileDetails?.GetConnectedSsid();
                    name = string.IsNullOrEmpty(ssid) ? wlan.ProfileName : ssid;
                }
                else if (profile is not null)
                {
                    kind = NetworkKind.Ethernet;
                    name = profile.ProfileName;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Network query failed: {ex.Message}");
        }

        State = new ConnectivityState(
            _canControl,
            _wifi is not null, _wifi?.State == RadioState.On,
            _bluetooth is not null, _bluetooth?.State == RadioState.On,
            kind, Math.Clamp(bars, 0, 5), percent ?? Math.Clamp(bars, 0, 5) * 20,
            string.IsNullOrWhiteSpace(name) ? null : name, internet);
    }

    private static ConnectionProfile? ConnectedWlan() =>
        NetworkInformation.GetConnectionProfiles().FirstOrDefault(p =>
            p.IsWlanConnectionProfile && p.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None);
}
