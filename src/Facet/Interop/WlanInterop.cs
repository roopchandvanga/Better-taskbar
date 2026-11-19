using System.Runtime.InteropServices;

namespace Facet.Interop;

/// <summary>
/// Native WLAN API, only for the connected network's signal quality (0-100 %): WinRT exposes just 0-5 bars.
/// </summary>
internal static class WlanInterop
{
    private const uint ClientVersion = 2;               // Vista and later
    private const int InterfaceInfoSize = 16 + 512 + 4; // GUID, WCHAR[256] description, state
    private const int StateConnected = 1;               // wlan_interface_state_connected
    private const int OpcodeCurrentConnection = 7;      // wlan_intf_opcode_current_connection
    // WLAN_CONNECTION_ATTRIBUTES: state, mode, WCHAR[256] profile, then WLAN_ASSOCIATION_ATTRIBUTES
    // (DOT11_SSID 36, BSS type 4, BSSID 6 + 2 padding, PHY type 4, PHY index 4) -> wlanSignalQuality
    private const int SignalQualityOffset = 4 + 4 + 512 + 36 + 4 + 8 + 4 + 4;

    private static IntPtr _handle;
    private static bool _unavailable;

    /// <summary>Signal quality of the connected Wi-Fi interface, or null if there's none or it can't be read.</summary>
    public static int? ConnectedSignalQuality()
    {
        if (_unavailable) return null;
        try
        {
            if (_handle == IntPtr.Zero && WlanOpenHandle(ClientVersion, IntPtr.Zero, out _, out _handle) != 0)
            {
                _handle = IntPtr.Zero;
                _unavailable = true; // WLAN service not running (no Wi-Fi hardware)
                return null;
            }
            if (WlanEnumInterfaces(_handle, IntPtr.Zero, out var list) != 0) return null;
            try
            {
                var count = Marshal.ReadInt32(list);
                for (var i = 0; i < count; i++)
                {
                    var info = list + 8 + i * InterfaceInfoSize;
                    if (Marshal.ReadInt32(info + 16 + 512) != StateConnected) continue;
                    var id = Marshal.PtrToStructure<Guid>(info);
                    if (WlanQueryInterface(_handle, ref id, OpcodeCurrentConnection, IntPtr.Zero, out var size, out var data, out _) != 0)
                        continue;
                    try
                    {
                        if (size >= SignalQualityOffset + 4) return Math.Clamp(Marshal.ReadInt32(data + SignalQualityOffset), 0, 100);
                    }
                    finally { WlanFreeMemory(data); }
                }
            }
            finally { WlanFreeMemory(list); }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _unavailable = true;
        }
        return null;
    }

    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr handle);
    [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr interfaceList);
    [DllImport("wlanapi.dll")]
    private static extern uint WlanQueryInterface(IntPtr handle, ref Guid interfaceGuid, int opCode, IntPtr reserved,
        out int dataSize, out IntPtr data, out int opcodeValueType);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr memory);
}
