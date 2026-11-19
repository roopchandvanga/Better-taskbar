using System.Runtime.InteropServices;
using Facet.Core.Settings;
using Facet.Interop;

namespace Facet.Shell;

/// <summary>
/// Hides the Windows taskbar while Facet runs and always puts it back: on quit, on crash handlers,
/// and — if the process died without cleanup — at the next launch (state.json "dirty" flag).
/// </summary>
public sealed class TaskbarController(string dataDir)
{
    private const uint ABM_GETSTATE = 4;
    private const uint ABM_SETSTATE = 10;
    private const int ABS_AUTOHIDE = 1;

    private sealed class State
    {
        public bool TaskbarHidden { get; set; }
        public int OriginalState { get; set; }
    }

    private readonly string _statePath = Path.Combine(dataDir, "state.json");
    private readonly object _gate = new();

    public bool IsHidden { get; private set; }

    /// <summary>Restore the taskbar if a previous session hid it and never cleaned up.</summary>
    public void RecoverIfDirty()
    {
        var state = JsonStore.Load<State>(_statePath);
        if (!state.TaskbarHidden) return;
        Apply(visible: true, state.OriginalState);
        Save(new State());
    }

    public void Hide()
    {
        lock (_gate)
        {
            if (IsHidden) return;
            var tray = Native.FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero) return;

            var data = NewData(tray);
            var original = (int)Native.SHAppBarMessage(ABM_GETSTATE, ref data);
            Save(new State { TaskbarHidden = true, OriginalState = original });

            Apply(visible: false, original | ABS_AUTOHIDE);
            IsHidden = true;
        }
    }

    /// <summary>
    /// Explorer re-shows the taskbar on its own (after the auto-hide change settles, when the cursor
    /// hits the screen edge, after Start closes…). Call this often while hidden; it costs two cheap calls.
    /// </summary>
    public void Enforce()
    {
        if (!IsHidden) return;
        foreach (var hwnd in TrayWindows())
            if (Native.IsWindowVisible(hwnd)) Native.ShowWindow(hwnd, Native.SW_HIDE);
    }

    public void Restore()
    {
        lock (_gate)
        {
            if (!IsHidden) return;
            var state = JsonStore.Load<State>(_statePath);
            Apply(visible: true, state.TaskbarHidden ? state.OriginalState : 0);
            Save(new State());
            IsHidden = false;
        }
    }

    private static void Apply(bool visible, int appBarState)
    {
        var tray = Native.FindWindow("Shell_TrayWnd", null);
        if (tray != IntPtr.Zero)
        {
            var data = NewData(tray);
            data.lParam = new IntPtr(appBarState);
            Native.SHAppBarMessage(ABM_SETSTATE, ref data);
        }

        foreach (var hwnd in TrayWindows())
            Native.ShowWindow(hwnd, visible ? Native.SW_SHOWNOACTIVATE : Native.SW_HIDE);
    }

    private static IEnumerable<IntPtr> TrayWindows()
    {
        var main = Native.FindWindow("Shell_TrayWnd", null);
        if (main != IntPtr.Zero) yield return main;
        var next = IntPtr.Zero;
        while ((next = Native.FindWindowEx(IntPtr.Zero, next, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            yield return next;
    }

    private static Native.APPBARDATA NewData(IntPtr hwnd) =>
        new() { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = hwnd };

    private void Save(State s)
    {
        try { JsonStore.Save(_statePath, s); } catch (IOException) { }
    }
}
