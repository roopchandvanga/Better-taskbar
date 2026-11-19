using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Threading;
using Facet.Interop;

namespace Facet.Shell;

/// <summary>
/// Context menus opened from the dock never belong to the foreground app (the dock doesn't take focus),
/// so WPF doesn't see clicks in other windows and the menu would stay open. This closes the menu as soon
/// as any mouse button goes down outside Facet's own windows, or when another app takes the foreground.
/// </summary>
internal static class MenuGuard
{
    private const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, VK_MBUTTON = 0x04;

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Native.POINT point);

    /// <summary>Call before setting <c>IsOpen = true</c>.</summary>
    public static void Watch(ContextMenu menu)
    {
        var ownPid = (uint)Environment.ProcessId;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        var armed = false; // ignore the button press that opened the menu
        var foregroundAtOpen = IntPtr.Zero;

        timer.Tick += (_, _) =>
        {
            if (!menu.IsOpen)
            {
                timer.Stop();
                return;
            }

            // another app came to the front (Alt+Tab, Win key, a notification click…)
            var fg = Native.GetForegroundWindow();
            if (fg != foregroundAtOpen)
            {
                Native.GetWindowThreadProcessId(fg, out var fgPid);
                if (fgPid != ownPid)
                {
                    menu.IsOpen = false;
                    return;
                }
            }

            var down = IsDown(VK_LBUTTON) || IsDown(VK_RBUTTON) || IsDown(VK_MBUTTON);
            if (!down)
            {
                armed = true;
                return;
            }
            if (!armed) return;

            Native.GetCursorPos(out var p);
            Native.GetWindowThreadProcessId(WindowFromPoint(p), out var pid);
            if (pid != ownPid) menu.IsOpen = false; // clicks on Facet's own windows are WPF's to handle
        };

        menu.Opened += (_, _) =>
        {
            armed = false;
            foregroundAtOpen = Native.GetForegroundWindow();
            timer.Start();
        };
        menu.Closed += (_, _) => timer.Stop();
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
}
