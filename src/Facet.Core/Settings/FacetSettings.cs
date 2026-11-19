using System.Globalization;
using System.Text.Json.Serialization;
using Facet.Core.Alerts;

namespace Facet.Core.Settings;

public enum DockTheme { LiquidGlass, Frost, Midnight, Black, White }

public enum DockEdge { Bottom, Top }

public enum DockSize { Small, Medium, Large }

public enum DockLayout { Pill, FullWidth }

public enum CustomWidgetKind { Command, Url }

public sealed class WidgetSlot
{
    public string Id { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class CustomWidgetConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Custom";
    public CustomWidgetKind Kind { get; set; } = CustomWidgetKind.Command;
    /// <summary>Shell command (run with cmd /c) or http(s) URL.</summary>
    public string Source { get; set; } = "";
    /// <summary>Optional path into JSON output, e.g. "data.price".</summary>
    public string? JsonPath { get; set; }
    public int IntervalSeconds { get; set; } = 60;
    public string? Prefix { get; set; }
    public string? Suffix { get; set; }
    /// <summary>URL, file or command opened when the tile is clicked.</summary>
    public string? ClickTarget { get; set; }
}

public sealed class PinnedApp
{
    public string Name { get; set; } = "";
    /// <summary>Executable/shortcut path, or an AppUserModelID when <see cref="IsAumid"/>.</summary>
    public string Target { get; set; } = "";
    public bool IsAumid { get; set; }
}

public sealed class FacetSettings
{
    /// <summary>
    /// Default order on the dock; "connect" (network + battery gauge, expand button) sits last, beside the clock.
    /// Volume / mic / do-not-disturb and battery used to be tiles of their own ("toggles", "battery"); they now
    /// live in the connect tile and its expanded panel, and Normalize drops those ids from older settings.
    /// </summary>
    public static readonly string[] BuiltInWidgetIds =
        ["upnext", "media", "weather", "todos", "connect", "system", "pomodoro"];

    /// <summary>Available but off until the user turns them on.</summary>
    public static readonly string[] OptInWidgetIds = ["system", "pomodoro"];

    /// <summary>
    /// Buttons the quick controls tile ("connect") can show, in their default order. "network" is the network
    /// icon inside the battery ring; "battery" is a ring of its own (the network ring then shows signal instead).
    /// </summary>
    public static readonly string[] ConnectItemIds =
        ["network", "battery", "bluetooth", "project", "volume", "mic", "dnd", "screenshot", "expand"];

    /// <summary>Offered in the quick controls tile, but off until the user adds them.</summary>
    public static readonly string[] OptInConnectItemIds = ["battery", "volume", "mic", "dnd", "screenshot"];

    // Appearance
    public DockTheme Theme { get; set; } = DockTheme.LiquidGlass;
    public DockEdge Edge { get; set; } = DockEdge.Bottom;
    public DockSize Size { get; set; } = DockSize.Medium;
    public DockLayout Layout { get; set; } = DockLayout.Pill;
    public bool Clock24 { get; set; }
    /// <summary>Follow Windows' light/dark mode: Glass when Windows is dark, Frosted when it's light.</summary>
    public bool MatchWindowsMode { get; set; }
    /// <summary>Use Windows' accent colour instead of Facet's blue.</summary>
    public bool WindowsAccent { get; set; } = true;
    /// <summary>The bell at the dock's far right that opens Windows' notification center.</summary>
    public bool ShowNotifications { get; set; } = true;
    /// <summary>Facet's motion (also off whenever Windows' "Animation effects" is off).</summary>
    public bool Animations { get; set; } = true;
    /// <summary>Icons near the cursor grow, macOS-style (off: only the hovered icon grows a little).</summary>
    public bool Magnify { get; set; } = true;
    /// <summary>Resting on a running app shows live previews of its windows.</summary>
    public bool WindowPreviews { get; set; } = true;

    // Behaviour
    /// <summary>Tuck the dock away and slide it in when the cursor touches the screen edge.</summary>
    public bool AutoHide { get; set; } = true;
    public bool LaunchAtStartup { get; set; }
    public bool HideTaskbar { get; set; }
    public bool ReserveSpace { get; set; }
    public bool HideInFullscreen { get; set; } = true;
    public bool PaletteHotkey { get; set; } = true;
    /// <summary>Preferred shortcut; Facet falls back to another free one if it's taken.</summary>
    public string PaletteShortcut { get; set; } = "Ctrl+Alt+Space";

    // Widgets
    public List<WidgetSlot> Widgets { get; set; } = DefaultSlots(BuiltInWidgetIds, OptInWidgetIds);
    public List<CustomWidgetConfig> CustomWidgets { get; set; } = [];
    /// <summary>What the quick controls tile shows, and in which order (ids from <see cref="ConnectItemIds"/>).</summary>
    public List<WidgetSlot> ConnectItems { get; set; } = DefaultSlots(ConnectItemIds, OptInConnectItemIds);

    // Weather
    public string? WeatherCity { get; set; }
    public double? WeatherLatitude { get; set; }
    public double? WeatherLongitude { get; set; }
    public bool Fahrenheit { get; set; }

    // Calendar
    public List<string> ICalUrls { get; set; } = [];

    // Alerts
    public AlertRules Alerts { get; set; } = new();
    public bool QuietHoursEnabled { get; set; }
    public string QuietStart { get; set; } = "22:00";
    public string QuietEnd { get; set; } = "07:00";

    // Pomodoro
    public int PomodoroFocusMinutes { get; set; } = 25;
    public int PomodoroBreakMinutes { get; set; } = 5;
    public bool QuietDuringFocus { get; set; } = true;

    // Apps
    public List<PinnedApp> ExtraPins { get; set; } = [];
    public List<string> HiddenPins { get; set; } = [];

    [JsonIgnore] public TimeSpan QuietStartTime => ParseTime(QuietStart, new TimeSpan(22, 0, 0));
    [JsonIgnore] public TimeSpan QuietEndTime => ParseTime(QuietEnd, new TimeSpan(7, 0, 0));

    /// <summary>Repair the widget list after upgrades or hand edits; clamp numeric settings.</summary>
    public void Normalize()
    {
        Widgets ??= [];
        CustomWidgets ??= [];
        ICalUrls ??= [];
        ExtraPins ??= [];
        HiddenPins ??= [];
        Alerts ??= new AlertRules();

        var customIds = CustomWidgets.Select(c => "custom:" + c.Id).ToList();
        Widgets = NormalizeSlots(Widgets, BuiltInWidgetIds, OptInWidgetIds, customIds);
        ConnectItems = NormalizeSlots(ConnectItems, ConnectItemIds, OptInConnectItemIds, []);

        PomodoroFocusMinutes = Math.Clamp(PomodoroFocusMinutes, 1, 180);
        PomodoroBreakMinutes = Math.Clamp(PomodoroBreakMinutes, 1, 60);
        foreach (var c in CustomWidgets) c.IntervalSeconds = Math.Clamp(c.IntervalSeconds, 5, 86_400);
        Alerts.EventLeadMinutes = Math.Clamp(Alerts.EventLeadMinutes, 1, 60);
        Alerts.BatteryThreshold = Math.Clamp(Alerts.BatteryThreshold, 5, 80);
        Alerts.CpuThreshold = Math.Clamp(Alerts.CpuThreshold, 50, 100);
    }

    private static List<WidgetSlot> DefaultSlots(string[] ids, string[] optIn) => ids.Select(id => NewSlot(id, optIn)).ToList();

    private static WidgetSlot NewSlot(string id, string[] optIn) => new() { Id = id, Enabled = !optIn.Contains(id) };

    /// <summary>
    /// Keep the user's order and on/off choices; drop unknown and duplicate ids. A built-in id that's missing
    /// (added in a newer version) lands where it sits in the default order: right after the closest built-in
    /// that precedes it, or at the front. <paramref name="extra"/> ids (custom widgets) go at the end.
    /// </summary>
    private static List<WidgetSlot> NormalizeSlots(List<WidgetSlot>? slots, string[] builtIns, string[] optIn, IReadOnlyList<string> extra)
    {
        var valid = new HashSet<string>(builtIns.Concat(extra));
        var seen = new HashSet<string>();
        var list = (slots ?? []).Where(w => w?.Id is not null && valid.Contains(w.Id) && seen.Add(w.Id)).ToList();

        for (var i = 0; i < builtIns.Length; i++)
        {
            var id = builtIns[i];
            if (!seen.Add(id)) continue;
            var at = 0;
            for (var j = i - 1; j >= 0; j--)
            {
                var prev = list.FindIndex(w => w.Id == builtIns[j]);
                if (prev >= 0) { at = prev + 1; break; }
            }
            list.Insert(at, NewSlot(id, optIn));
        }
        foreach (var id in extra)
            if (seen.Add(id)) list.Add(NewSlot(id, optIn));
        return list;
    }

    private static TimeSpan ParseTime(string? text, TimeSpan fallback) =>
        TimeSpan.TryParseExact(text?.Trim(), [@"h\:mm", @"hh\:mm"], CultureInfo.InvariantCulture, out var t)
        && t < TimeSpan.FromDays(1)
            ? t
            : fallback;
}
