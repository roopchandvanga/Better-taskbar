using System.Runtime.InteropServices;
using Facet.Core.Settings;
using Facet.Interop;

namespace Facet.Shell;

/// <summary>Registers the dock as a shell AppBar so maximised windows stop at its edge.</summary>
internal sealed class AppBar(IntPtr hwnd)
{
    private const uint ABM_NEW = 0;
    private const uint ABM_REMOVE = 1;
    private const uint ABM_QUERYPOS = 2;
    private const uint ABM_SETPOS = 3;
    private const uint ABE_TOP = 1;
    private const uint ABE_BOTTOM = 3;
    public const int ABN_POSCHANGED = 1;
    public const int ABN_FULLSCREENAPP = 2;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    public uint CallbackMessage { get; } = RegisterWindowMessage("FacetAppBarMessage");
    public bool Registered { get; private set; }

    public bool Reserving { get; private set; }

    /// <summary>
    /// Register without reserving space. Registered appbars receive ABN_FULLSCREENAPP — the same
    /// "a fullscreen app is up" signal the Windows taskbar uses to get out of the way.
    /// </summary>
    public void Register()
    {
        if (Registered) return;
        var data = NewData();
        data.uCallbackMessage = CallbackMessage;
        Native.SHAppBarMessage(ABM_NEW, ref data);
        Registered = true;
    }

    /// <summary>Give back any reserved space but stay registered for notifications.</summary>
    public void Release()
    {
        if (!Reserving) return;
        Remove();
        Register();
    }

    /// <summary>Reserve <paramref name="thicknessPx"/> physical pixels on the primary monitor's edge.</summary>
    public Native.RECT Reserve(DockEdge edge, int thicknessPx)
    {
        Register();
        Reserving = true;
        var data = NewData();

        var monitor = Native.GetPrimaryMonitor().rcMonitor;
        data.uEdge = edge == DockEdge.Top ? ABE_TOP : ABE_BOTTOM;
        data.rc = monitor;
        if (edge == DockEdge.Top) data.rc.Bottom = data.rc.Top + thicknessPx;
        else data.rc.Top = data.rc.Bottom - thicknessPx;

        Native.SHAppBarMessage(ABM_QUERYPOS, ref data);
        if (edge == DockEdge.Top) data.rc.Bottom = data.rc.Top + thicknessPx;
        else data.rc.Top = data.rc.Bottom - thicknessPx;
        Native.SHAppBarMessage(ABM_SETPOS, ref data);
        return data.rc;
    }

    public void Remove()
    {
        if (!Registered) return;
        var data = NewData();
        Native.SHAppBarMessage(ABM_REMOVE, ref data);
        Registered = false;
        Reserving = false;
    }

    private Native.APPBARDATA NewData() => new() { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = hwnd };
}

/// <summary>Raises an event whenever the foreground window changes (system-wide WinEvent hook).</summary>
internal sealed class ForegroundTracker : IDisposable
{
    private readonly Native.WinEventProc _proc;
    private readonly IntPtr _hook;

    public event Action<IntPtr>? Changed;

    public ForegroundTracker()
    {
        _proc = (_, _, hwnd, _, _, _, _) => Changed?.Invoke(hwnd);
        _hook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _proc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) Native.UnhookWinEvent(_hook);
    }
}
