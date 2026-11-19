using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using Facet.Core.Apps;
using Facet.Core.Settings;
using Facet.Infrastructure;
using Facet.Interop;
using Facet.Services;

namespace Facet.Apps;

/// <summary>
/// Merges the Windows taskbar's pinned shortcuts (+ Facet pins) with running top-level windows,
/// grouped by AppUserModelID or executable path — the same identity the taskbar uses.
/// </summary>
public sealed class AppTracker
{
    private static readonly string PinDir = Environment.ExpandEnvironmentVariables(
        @"%APPDATA%\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

    private static readonly HashSet<string> IgnoredClasses =
    [
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland",
    ];

    private sealed record PinEntry(string Name, string Launch, string? Aumid, string? ExePath, string? Shortcut, PinnedApp? Extra)
    {
        public string Key => "pin:" + (Shortcut ?? Launch).ToLowerInvariant();
    }

    private sealed record WindowIdentity(uint Pid, string? Aumid, string? ExePath, string Name);

    private readonly SettingsService _settings;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<IntPtr, WindowIdentity> _identities = [];
    private readonly Dictionary<string, long> _firstSeen = [];
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private List<PinEntry> _pins = [];
    private bool _pinsDirty = true;
    private long _seenCounter;
    private FileSystemWatcher? _watcher;

    public AppTracker(SettingsService settings)
    {
        _settings = settings;
        _settings.Changed += () => { _pinsDirty = true; Refresh(); };
    }

    public ObservableCollection<AppItem> Items { get; } = [];

    /// <summary>Most recent foreground window that is not Facet's own.</summary>
    public IntPtr Foreground { get; private set; }

    public void Start()
    {
        MigratePackagedPins();
        Refresh();
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        if (Directory.Exists(PinDir))
        {
            _watcher = new FileSystemWatcher(PinDir, "*.lnk") { EnableRaisingEvents = true };
            FileSystemEventHandler dirty = (_, _) => _pinsDirty = true;
            _watcher.Created += dirty;
            _watcher.Deleted += dirty;
            _watcher.Renamed += (_, _) => _pinsDirty = true;
        }
    }

    private int _iconPixels = 40;

    /// <summary>Icons are requested at the exact pixel size they're drawn at; reload them when it changes.</summary>
    public void EnsureIconSize(int pixels)
    {
        if (pixels <= 0 || pixels == _iconPixels) return;
        _iconPixels = pixels;
        foreach (var item in Items) item.IconLoaded = false;
        Refresh();
    }

    public void OnForegroundChanged(IntPtr hwnd)
    {
        if (!IsOwn(hwnd)) Foreground = hwnd;
        Refresh();
    }

    // ───────────────────────── actions ─────────────────────────

    public void Click(AppItem item)
    {
        var windows = item.Windows.Where(Native.IsWindow).ToList();
        if (windows.Count == 0)
        {
            Launch(item);
            return;
        }

        var fg = Native.GetForegroundWindow();
        if (windows.Count == 1)
        {
            var w = windows[0];
            if (w == fg && !Native.IsIconic(w)) Native.ShowWindow(w, Native.SW_MINIMIZE);
            else Native.ActivateWindow(w);
            return;
        }

        // several windows: cycle through them, front-most first
        var idx = windows.IndexOf(fg);
        Native.ActivateWindow(windows[(idx + 1) % windows.Count]);
    }

    public void Launch(AppItem item)
    {
        var target = item.LaunchTarget ?? item.ExePath;
        if (string.IsNullOrEmpty(target)) return;
        if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) AppServices.Open("explorer.exe", target);
        else AppServices.Open(target);

        // bounce until a new window shows up (or give up after a while: some apps open none, or reuse one)
        item.LaunchedFrom = (DateTime.UtcNow, item.Windows.Count);
        item.IsLaunching = true;
    }

    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(10);

    // ───────────────────────── drag and drop ─────────────────────────

    /// <summary>Open dropped files in this app (macOS dock style). Works for pinned apps that aren't running.</summary>
    public void OpenFiles(AppItem item, IReadOnlyList<string> files)
    {
        if (files.Count == 0) return;
        var args = string.Join(" ", files.Select(f => $"\"{f}\""));

        // File Explorer: show the dropped items
        if (string.Equals(item.Aumid, ExplorerAppId, StringComparison.OrdinalIgnoreCase) || IsExplorer(item.ExePath))
        {
            foreach (var f in files.Take(5)) AppServices.Open("explorer.exe", $"/select,\"{f}\"");
            return;
        }

        // Store / packaged apps (Notepad, Photos, Media Player…) take files through app activation
        if (item.Aumid is { Length: > 0 } && (item.ExePath is null || IsPackaged(item.ExePath))
            && AppActivation.TryOpenFiles(item.Aumid, files))
            return;

        // classic apps: the executable with the files as arguments, or the pinned shortcut (keeps its own args)
        if (item.ExePath is { Length: > 0 } && !IsPackaged(item.ExePath) && File.Exists(item.ExePath))
            AppServices.Open(item.ExePath, args);
        else if (item.PinShortcut is not null)
            AppServices.Open(item.PinShortcut, args);
        else if (item.LaunchTarget is { Length: > 0 } t && !t.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            AppServices.Open(t, args);
    }

    /// <summary>Bring the app's front-most window forward (hovering a dragged file over a running app).</summary>
    public void BringToFront(AppItem item)
    {
        var window = item.Windows.FirstOrDefault(Native.IsWindow);
        if (window == IntPtr.Zero) return;
        Native.ActivateWindow(window);
        if (Native.GetForegroundWindow() != window) Native.SwitchToThisWindow(window, true);
    }

    /// <summary>Pin dropped programs (.exe) or shortcuts (.lnk). Returns how many were added.</summary>
    public int PinFiles(IEnumerable<string> paths)
    {
        var pins = _settings.Current.ExtraPins;
        var added = 0;
        foreach (var path in paths)
        {
            var pin = PinFor(path);
            if (pin is null) continue;
            var already = pins.Any(p => string.Equals(p.Target, pin.Target, StringComparison.OrdinalIgnoreCase))
                          || _pins.Any(p => string.Equals(p.ExePath, pin.Target, StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(p.Aumid, pin.Target, StringComparison.OrdinalIgnoreCase));
            if (already) continue;
            pins.Add(pin);
            added++;
        }
        if (added > 0) _settings.Save();
        return added;
    }

    /// <summary>True if any of these paths could be pinned (programs or shortcuts).</summary>
    public static bool ArePinnable(IEnumerable<string> paths) =>
        paths.Any(p => Path.GetExtension(p).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                       || Path.GetExtension(p).Equals(".lnk", StringComparison.OrdinalIgnoreCase));

    private static PinnedApp? PinFor(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".exe" && File.Exists(path))
            return ExePin(DescribeExe(path), path);
        if (ext != ".lnk" || !File.Exists(path)) return null;

        var name = Path.GetFileNameWithoutExtension(path);
        var (target, _) = ShellInterop.ResolveShortcut(path);
        if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
            return ExePin(name, target);
        return ShellInterop.GetFileAppId(path) is { Length: > 0 } aumid
            ? new PinnedApp { Name = name, Target = aumid, IsAumid = true }
            : null;
    }

    /// <summary>A Store / MSIX app is pinned by its app ID: its exe path includes the version and dies on update.</summary>
    private static PinnedApp ExePin(string name, string exe) =>
        IsPackaged(exe) && AppIdForPackagedExe(exe) is { } aumid
            ? new PinnedApp { Name = name, Target = aumid, IsAumid = true }
            : new PinnedApp { Name = name, Target = exe };

    /// <summary>
    /// "Family!AppId" for an exe inside a package folder, looked up in the installed package's manifest. Works
    /// for a path whose version folder is already gone (updated since), as long as the app is still installed.
    /// </summary>
    private static string? AppIdForPackagedExe(string exe)
    {
        if (!PackagedApp.TryParsePath(exe, out var family, out var relativeExe)) return null;
        try
        {
            var package = new Windows.Management.Deployment.PackageManager()
                .FindPackagesForUser(string.Empty, family).FirstOrDefault();
            if (package is null) return null;
            var manifest = Path.Combine(package.InstalledLocation.Path, "AppxManifest.xml");
            return PackagedApp.AppIdFromManifest(File.ReadAllText(manifest), relativeExe) is { } appId
                ? $"{family}!{appId}"
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException
                                       or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Pins saved as a packaged app's exe path (older Facet) become app-ID pins, so updates don't break them.</summary>
    private void MigratePackagedPins()
    {
        var changed = false;
        foreach (var pin in _settings.Current.ExtraPins.Where(p => !p.IsAumid && IsPackaged(p.Target)).ToList())
        {
            if (AppIdForPackagedExe(pin.Target) is not { } aumid) continue;
            var duplicate = _settings.Current.ExtraPins.Any(p => p.IsAumid && string.Equals(p.Target, aumid, StringComparison.OrdinalIgnoreCase));
            if (duplicate) _settings.Current.ExtraPins.Remove(pin);
            else { pin.Target = aumid; pin.IsAumid = true; }
            changed = true;
        }
        if (changed) _settings.Save();
    }

    private static string DescribeExe(string exe)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exe);
            if (!string.IsNullOrWhiteSpace(info.FileDescription)) return info.FileDescription.Trim();
            if (!string.IsNullOrWhiteSpace(info.ProductName)) return info.ProductName.Trim();
        }
        catch (FileNotFoundException) { }
        return Path.GetFileNameWithoutExtension(exe);
    }

    public void CloseAll(AppItem item)
    {
        foreach (var w in item.Windows) Native.PostMessage(w, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    public void Pin(AppItem item)
    {
        if (item.IsPinned) return;
        var pin = item.Aumid is { Length: > 0 } && (item.ExePath is null || IsPackaged(item.ExePath))
            ? new PinnedApp { Name = item.Name, Target = item.Aumid, IsAumid = true }
            : item.ExePath is { Length: > 0 }
                ? new PinnedApp { Name = item.Name, Target = item.ExePath }
                : null;
        if (pin is null) return;
        _settings.Current.ExtraPins.Add(pin);
        _settings.Save();
    }

    public void Unpin(AppItem item)
    {
        if (item.ExtraPin is not null) _settings.Current.ExtraPins.Remove(item.ExtraPin);
        else if (item.PinShortcut is not null) _settings.Current.HiddenPins.Add(item.PinShortcut);
        else return;
        _settings.Save();
    }

    // ───────────────────────── refresh ─────────────────────────

    public void Refresh()
    {
        if (_pinsDirty) LoadPins();

        var fg = Native.GetForegroundWindow();
        if (!IsOwn(fg)) Foreground = fg;

        var pinLookup = new Dictionary<string, PinEntry>();
        foreach (var p in _pins)
        {
            if (IdKey(p.Aumid) is { } a) pinLookup.TryAdd(a, p);
            if (PathKey(p.ExePath) is { } x) pinLookup.TryAdd(x, p);
        }

        var groups = new Dictionary<string, (List<IntPtr> Windows, WindowIdentity Identity)>();
        foreach (var hwnd in EnumerateAppWindows())
        {
            var id = Identify(hwnd);
            if (id is null) continue;

            string key;
            if (IdKey(id.Aumid) is { } ak && pinLookup.TryGetValue(ak, out var p1)) key = p1.Key;
            else if (PathKey(id.ExePath) is { } pk && pinLookup.TryGetValue(pk, out var p2)) key = p2.Key;
            else key = IdKey(id.Aumid) ?? PathKey(id.ExePath) ?? $"hwnd:{hwnd}";

            if (!groups.TryGetValue(key, out var g))
            {
                g = ([], id);
                groups[key] = g;
            }
            g.Windows.Add(hwnd);
        }

        // forget identities of windows that no longer exist
        foreach (var dead in _identities.Keys.Where(h => !Native.IsWindow(h)).ToList()) _identities.Remove(dead);

        var existing = Items.ToDictionary(i => i.Key);
        var desired = new List<AppItem>();

        foreach (var pin in _pins)
        {
            var item = existing.GetValueOrDefault(pin.Key) ?? new AppItem(pin.Key);
            item.IsPinned = true;
            item.Name = pin.Name;
            item.LaunchTarget = pin.Launch;
            item.PinShortcut = pin.Shortcut;
            item.ExtraPin = pin.Extra;
            item.Aumid ??= pin.Aumid;
            item.ExePath ??= pin.ExePath;
            groups.TryGetValue(pin.Key, out var g);
            Update(item, g.Windows, fg);
            desired.Add(item);
        }

        var pinnedKeys = new HashSet<string>(_pins.Select(p => p.Key));
        foreach (var (key, g) in groups)
        {
            if (pinnedKeys.Contains(key)) continue;
            if (!_firstSeen.ContainsKey(key)) _firstSeen[key] = ++_seenCounter;
        }

        foreach (var (key, g) in groups.Where(kv => !pinnedKeys.Contains(kv.Key)).OrderBy(kv => _firstSeen[kv.Key]))
        {
            var item = existing.GetValueOrDefault(key) ?? new AppItem(key);
            item.IsPinned = false;
            item.PinShortcut = null;
            item.ExtraPin = null;
            item.Aumid = g.Identity.Aumid;
            item.ExePath = g.Identity.ExePath;
            item.Name = g.Identity.Name;
            item.LaunchTarget = item.Aumid is { Length: > 0 } && (item.ExePath is null || IsPackaged(item.ExePath))
                ? $@"shell:AppsFolder\{item.Aumid}"
                : item.ExePath;
            Update(item, g.Windows, fg);
            desired.Add(item);
        }

        foreach (var stale in _firstSeen.Keys.Where(k => !groups.ContainsKey(k)).ToList()) _firstSeen.Remove(stale);

        Sync(desired);
    }

    private void Update(AppItem item, List<IntPtr>? windows, IntPtr fg)
    {
        item.Windows.Clear();
        if (windows is not null) item.Windows.AddRange(windows);
        item.WindowCount = item.Windows.Count;
        item.IsRunning = item.Windows.Count > 0;
        if (item.IsLaunching && (item.Windows.Count > item.LaunchedFrom.Windows || DateTime.UtcNow - item.LaunchedFrom.At > LaunchTimeout))
            item.IsLaunching = false;
        item.IsActive = item.Windows.Contains(fg) || item.Windows.Contains(Foreground) && IsOwn(fg);
        item.Tooltip = item.Windows.Count == 1
            ? $"{item.Name} — {Native.GetWindowTitle(item.Windows[0])}"
            : item.Windows.Count > 1 ? $"{item.Name} · {item.Windows.Count} windows" : item.Name;

        if (!item.IconLoaded)
        {
            item.IconLoaded = true;
            var hwnd = item.Windows.FirstOrDefault();
            var size = _iconPixels * 2; // fetched at 2x, drawn at 1x on whole pixels: a clean 2:1 downscale
            System.Windows.Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background,
                () => item.Icon = LoadIcon(item, hwnd, size));
        }
    }

    private const int LeaveMs = 240;
    private readonly Dictionary<AppItem, DateTime> _leaving = [];
    private DispatcherTimer? _sweep;

    private void Sync(List<AppItem> desired)
    {
        desired = KeepLeaving(desired);
        for (var i = 0; i < desired.Count; i++)
        {
            if (i < Items.Count && ReferenceEquals(Items[i], desired[i])) continue;
            var old = Items.IndexOf(desired[i]);
            if (old >= 0) Items.Move(old, i);
            else Items.Insert(i, desired[i]);
        }
        while (Items.Count > desired.Count) Items.RemoveAt(Items.Count - 1);
    }

    /// <summary>
    /// An app that just left the dock (closed, or unpinned while not running) stays a moment, in its place and
    /// flagged <see cref="AppItem.IsLeaving"/>, so the dock can shrink it away; a sweep then removes it. One that
    /// comes back in the meantime simply stays.
    /// </summary>
    private List<AppItem> KeepLeaving(List<AppItem> desired)
    {
        var now = DateTime.UtcNow;
        var wanted = new HashSet<AppItem>(desired);
        foreach (var back in _leaving.Keys.Where(wanted.Contains).ToList())
        {
            _leaving.Remove(back);
            back.IsLeaving = false;
        }
        if (Theme.Motion.Enabled)
        {
            foreach (var item in Items)
            {
                if (wanted.Contains(item) || _leaving.ContainsKey(item)) continue;
                _leaving[item] = now;
                item.IsLeaving = true;
                item.IsLaunching = false;
                item.IsActive = false; // the active-app line moves on right away
                item.IsRunning = false;
            }
        }
        foreach (var gone in _leaving.Where(kv => (now - kv.Value).TotalMilliseconds >= LeaveMs || !Theme.Motion.Enabled).Select(kv => kv.Key).ToList())
            _leaving.Remove(gone);
        if (_leaving.Count == 0) return desired;

        // in place: right after whichever item preceded it on the dock
        var result = new List<AppItem>(desired);
        for (var i = 0; i < Items.Count; i++)
        {
            if (!_leaving.ContainsKey(Items[i])) continue;
            var at = 0;
            for (var j = i - 1; j >= 0; j--)
            {
                var before = result.IndexOf(Items[j]);
                if (before < 0) continue;
                at = before + 1;
                break;
            }
            result.Insert(at, Items[i]);
        }

        if (_sweep is null)
        {
            _sweep = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(LeaveMs + 20) };
            _sweep.Tick += (_, _) =>
            {
                _sweep.Stop();
                Refresh();
            };
        }
        _sweep.Stop();
        _sweep.Start();
        return result;
    }

    private static System.Windows.Media.ImageSource? LoadIcon(AppItem item, IntPtr hwnd, int size)
    {
        var candidates = new List<string>();
        if (item.PinShortcut is not null) candidates.Add(item.PinShortcut);
        if (item.Aumid is { Length: > 0 } && (item.ExePath is null || IsPackaged(item.ExePath)))
            candidates.Add($@"shell:AppsFolder\{item.Aumid}");
        if (item.ExePath is { Length: > 0 } && File.Exists(item.ExePath)) candidates.Add(item.ExePath);
        if (item.Aumid is { Length: > 0 }) candidates.Add($@"shell:AppsFolder\{item.Aumid}");

        foreach (var c in candidates)
        {
            var icon = ShellInterop.GetShellIcon(c, size);
            if (icon is not null) return icon;
        }
        return hwnd != IntPtr.Zero ? ShellInterop.GetWindowIcon(hwnd) : null;
    }

    private void LoadPins()
    {
        _pinsDirty = false;
        var pins = new List<PinEntry>();
        var hidden = new HashSet<string>(_settings.Current.HiddenPins, StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(PinDir))
        {
            foreach (var lnk in Directory.GetFiles(PinDir, "*.lnk").OrderBy(File.GetCreationTimeUtc))
            {
                if (hidden.Contains(lnk)) continue;
                var (target, _) = ShellInterop.ResolveShortcut(lnk);
                var aumid = ShellInterop.GetFileAppId(lnk);
                // Windows 11's File Explorer pin points at a shell location, not explorer.exe, and may carry no
                // app ID: give it the same identity running Explorer windows get, so they share one icon.
                if (aumid is null && (IsExplorer(target) || Path.GetFileNameWithoutExtension(lnk).Equals("File Explorer", StringComparison.OrdinalIgnoreCase)))
                    aumid = ExplorerAppId;
                pins.Add(new PinEntry(Path.GetFileNameWithoutExtension(lnk), lnk, aumid,
                    string.IsNullOrWhiteSpace(target) ? null : target, lnk, null));
            }
        }

        foreach (var extra in _settings.Current.ExtraPins)
        {
            pins.Add(extra.IsAumid
                ? new PinEntry(extra.Name, $@"shell:AppsFolder\{extra.Target}", extra.Target, null, null, extra)
                : new PinEntry(extra.Name, extra.Target, null, extra.Target, null, extra));
        }

        _pins = pins;
    }

    private IEnumerable<IntPtr> EnumerateAppWindows()
    {
        var result = new List<IntPtr>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (IsAppWindow(hwnd)) result.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private bool IsAppWindow(IntPtr hwnd)
    {
        if (!Native.IsWindowVisible(hwnd)) return false;
        if (Native.GetWindow(hwnd, Native.GW_OWNER) != IntPtr.Zero) return false;
        var ex = Native.GetExStyle(hwnd);
        var appWindow = (ex & Native.WS_EX_APPWINDOW) != 0;
        if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && !appWindow) return false;
        if ((ex & Native.WS_EX_NOACTIVATE) != 0 && !appWindow) return false;
        if (Native.GetWindowTextLength(hwnd) == 0) return false;
        if (Native.IsCloaked(hwnd)) return false;
        if (IgnoredClasses.Contains(Native.GetClassName(hwnd))) return false;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        return pid != _ownPid;
    }

    private WindowIdentity? Identify(IntPtr hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (_identities.TryGetValue(hwnd, out var cached) && cached.Pid == pid) return cached;

        var exe = Native.GetProcessPath(pid);
        var aumid = ShellInterop.GetWindowAppId(hwnd);
        if (aumid is null && IsExplorer(exe)) aumid = ExplorerAppId; // Explorer windows don't publish one
        // packaged apps that don't tag their windows (Electron ones like Claude) still run under their package's
        // app ID; it's what the taskbar groups them by, and it survives updates where the exe path doesn't
        if (aumid is null && exe is not null && IsPackaged(exe)) aumid = Native.GetProcessAppId(pid);

        if (exe is not null && exe.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
        {
            var inner = FindHostedProcess(hwnd, pid);
            if (inner is not null) exe = inner;
        }

        string name;
        if (aumid is not null && (exe is null || IsPackaged(exe)))
        {
            name = Native.GetWindowTitle(hwnd);
        }
        else if (exe is not null)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exe);
                name = !string.IsNullOrWhiteSpace(info.FileDescription) ? info.FileDescription.Trim()
                     : !string.IsNullOrWhiteSpace(info.ProductName) ? info.ProductName.Trim()
                     : Path.GetFileNameWithoutExtension(exe);
            }
            catch (FileNotFoundException)
            {
                name = Path.GetFileNameWithoutExtension(exe);
            }
        }
        else
        {
            name = Native.GetWindowTitle(hwnd);
        }

        if (name.Equals("Windows Explorer", StringComparison.OrdinalIgnoreCase)) name = "File Explorer";

        var id = new WindowIdentity(pid, aumid, exe, name);
        _identities[hwnd] = id;
        return id;
    }

    private static string? FindHostedProcess(IntPtr frame, uint framePid)
    {
        string? found = null;
        Native.EnumChildWindows(frame, (child, _) =>
        {
            Native.GetWindowThreadProcessId(child, out var pid);
            if (pid != framePid)
            {
                found = Native.GetProcessPath(pid);
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private bool IsOwn(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return true;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == _ownPid;
    }

    private const string ExplorerAppId = "Microsoft.Windows.Explorer";

    private static bool IsExplorer(string? path) =>
        !string.IsNullOrEmpty(path) && Path.GetFileName(path).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);

    private static bool IsPackaged(string exe) =>
        exe.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
        || exe.Contains(@"\SystemApps\", StringComparison.OrdinalIgnoreCase)
        || exe.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);

    private static string? IdKey(string? aumid) => string.IsNullOrWhiteSpace(aumid) ? null : "a:" + aumid.ToLowerInvariant();
    private static string? PathKey(string? path) => string.IsNullOrWhiteSpace(path) ? null : "p:" + path.ToLowerInvariant();
}
