using Facet.Core.Calendar;

namespace Facet.Core.Alerts;

public enum AlertKind { Event, Battery, Cpu, Pomodoro, Info }

public enum AlertAction { None, PauseMedia, OpenCalendar }

public sealed record Alert(
    string Key,
    string Title,
    string Body,
    AlertKind Kind,
    AlertAction Action = AlertAction.None,
    bool Critical = false);

/// <summary>Everything the rules look at, captured at one instant.</summary>
public sealed record AlertSnapshot(
    DateTime Now,
    IReadOnlyList<CalendarEvent> Events,
    int? BatteryPercent,
    bool Charging,
    double CpuAverage,
    bool MediaPlaying,
    bool QuietHours);

public sealed class AlertRules
{
    public bool EventSoon { get; set; } = true;
    public int EventLeadMinutes { get; set; } = 5;
    public bool BatteryLow { get; set; } = true;
    public int BatteryThreshold { get; set; } = 20;
    public bool CpuHot { get; set; } = true;
    public int CpuThreshold { get; set; } = 90;
}

/// <summary>
/// Pure rules engine. Each alert has a key and fires once until its condition clears (re-arm).
/// Quiet hours hold non-critical alerts: they are not consumed, so they surface later if still relevant.
/// </summary>
public sealed class AlertEngine
{
    private const int CriticalBattery = 10;
    private readonly HashSet<string> _fired = [];

    public AlertRules Rules { get; set; } = new();

    public IReadOnlyList<Alert> Evaluate(AlertSnapshot s)
    {
        var candidates = new List<Alert>();

        if (Rules.EventSoon)
        {
            var lead = TimeSpan.FromMinutes(Math.Max(1, Rules.EventLeadMinutes));
            foreach (var e in s.Events)
            {
                if (e.AllDay) continue;
                var until = e.Start - s.Now;
                if (until <= TimeSpan.Zero || until > lead) continue;
                var minutes = Math.Max(1, (int)Math.Ceiling(until.TotalMinutes));
                var body = string.IsNullOrWhiteSpace(e.Location)
                    ? $"At {e.Start:t}"
                    : $"At {e.Start:t} · {e.Location}";
                candidates.Add(new Alert(
                    $"event:{e.Id}:{e.Start.Ticks}",
                    $"{e.Title} starts in {minutes} min",
                    body,
                    AlertKind.Event,
                    s.MediaPlaying ? AlertAction.PauseMedia : AlertAction.OpenCalendar));
            }
        }

        if (Rules.BatteryLow && s.BatteryPercent is int battery)
        {
            if (!s.Charging && battery <= Rules.BatteryThreshold)
            {
                candidates.Add(new Alert("battery-low", $"Battery at {battery}%",
                    battery <= CriticalBattery ? "Plug in now to avoid losing work." : "Consider plugging in soon.",
                    AlertKind.Battery, AlertAction.None, Critical: battery <= CriticalBattery));
            }
            else if (s.Charging || battery > Rules.BatteryThreshold + 5)
            {
                _fired.Remove("battery-low");
            }
        }

        if (Rules.CpuHot)
        {
            if (s.CpuAverage >= Rules.CpuThreshold)
            {
                candidates.Add(new Alert("cpu-hot", $"CPU busy at {s.CpuAverage:0}%",
                    "Something has been working hard for a minute. Open Task Manager to check.", AlertKind.Cpu));
            }
            else if (s.CpuAverage < Rules.CpuThreshold - 20)
            {
                _fired.Remove("cpu-hot");
            }
        }

        var result = new List<Alert>();
        foreach (var alert in candidates)
        {
            if (_fired.Contains(alert.Key)) continue;
            if (s.QuietHours && !alert.Critical) continue;
            _fired.Add(alert.Key);
            result.Add(alert);
        }

        if (_fired.Count > 512) _fired.RemoveWhere(k => k.StartsWith("event:", StringComparison.Ordinal));
        return result;
    }
}

public static class QuietHours
{
    /// <summary>True when <paramref name="now"/> falls in [start, end); handles ranges across midnight.</summary>
    public static bool IsActive(TimeSpan start, TimeSpan end, DateTime now)
    {
        if (start == end) return false;
        var t = now.TimeOfDay;
        return start < end ? t >= start && t < end : t >= start || t < end;
    }
}
