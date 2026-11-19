using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Facet.Core.Calendar;

/// <summary>A VEVENT master record as parsed from an iCalendar feed (times in local time).</summary>
public sealed class ICalEvent
{
    public string Uid { get; set; } = "";
    public string Summary { get; set; } = "";
    public string? Location { get; set; }
    public string? Description { get; set; }
    public string? Url { get; set; }
    /// <summary>A dedicated join link: CONFERENCE, X-GOOGLE-CONFERENCE or X-MICROSOFT-SKYPETEAMSMEETINGURL.</summary>
    public string? Conference { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool AllDay { get; set; }
    public string? RRule { get; set; }
    public List<DateTime> ExDates { get; } = [];
    internal bool HasEnd { get; set; }
    internal TimeSpan? Duration { get; set; }
}

/// <summary>
/// Minimal RFC 5545 reader: VEVENT with SUMMARY/LOCATION/DESCRIPTION/URL/UID/DTSTART/DTEND/DURATION/RRULE/EXDATE,
/// plus the meeting-link fields CONFERENCE / X-GOOGLE-CONFERENCE / X-MICROSOFT-SKYPETEAMSMEETINGURL.
/// RRULE supports FREQ (DAILY/WEEKLY/MONTHLY/YEARLY), INTERVAL, COUNT, UNTIL and BYDAY for WEEKLY.
/// </summary>
public static partial class ICalParser
{
    private const int MaxIterations = 100_000;

    public static List<ICalEvent> Parse(string ics)
    {
        var events = new List<ICalEvent>();
        ICalEvent? current = null;
        var nested = 0; // depth of sub-components inside a VEVENT (VALARM etc.)

        foreach (var line in Unfold(ics))
        {
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                current = new ICalEvent();
                nested = 0;
                continue;
            }

            if (current is null) continue;

            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                Finish(current);
                if (current.Start != default) events.Add(current);
                current = null;
                continue;
            }

            if (line.StartsWith("BEGIN:", StringComparison.OrdinalIgnoreCase)) { nested++; continue; }
            if (line.StartsWith("END:", StringComparison.OrdinalIgnoreCase)) { nested = Math.Max(0, nested - 1); continue; }
            if (nested > 0) continue;

            var colon = FindValueColon(line);
            if (colon < 0) continue;
            var head = line[..colon];
            var value = line[(colon + 1)..];
            var semi = head.IndexOf(';');
            var name = (semi < 0 ? head : head[..semi]).ToUpperInvariant();
            var parameters = semi < 0 ? "" : head[(semi + 1)..];

            try
            {
                switch (name)
                {
                    case "UID": current.Uid = value; break;
                    case "SUMMARY": current.Summary = Unescape(value); break;
                    case "LOCATION": current.Location = Unescape(value); break;
                    case "DESCRIPTION": current.Description = Unescape(value); break;
                    case "URL": current.Url = value.Trim(); break;
                    case "CONFERENCE" or "X-GOOGLE-CONFERENCE" or "X-MICROSOFT-SKYPETEAMSMEETINGURL":
                        current.Conference ??= Unescape(value).Trim();
                        break;
                    case "DTSTART":
                        (current.Start, current.AllDay) = ParseDate(value, parameters);
                        break;
                    case "DTEND":
                        current.End = ParseDate(value, parameters).Value;
                        current.HasEnd = true;
                        break;
                    case "DURATION": current.Duration = ParseDuration(value); break;
                    case "RRULE": current.RRule = value; break;
                    case "EXDATE":
                        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                            current.ExDates.Add(ParseDate(part, parameters).Value);
                        break;
                }
            }
            catch (FormatException)
            {
                // skip malformed property; keep the rest of the event
            }
        }

        return events;
    }

    public static List<CalendarEvent> ParseAndExpand(string ics, DateTime from, DateTime to, string source)
    {
        var result = new List<CalendarEvent>();
        foreach (var ev in Parse(ics))
        {
            var meeting = MeetingLink.Find(ev.Conference, ev.Location, ev.Url, ev.Description);
            var duration = ev.End - ev.Start;
            if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;

            foreach (var start in Occurrences(ev, to))
            {
                if (IsExcluded(ev, start)) continue;
                var end = start + duration;
                var overlaps = start < to && (end > from || (duration == TimeSpan.Zero && start >= from));
                if (!overlaps) continue;
                var id = $"{(ev.Uid.Length > 0 ? ev.Uid : ev.Summary.GetHashCode().ToString(CultureInfo.InvariantCulture))}@{start:yyyyMMddHHmm}";
                result.Add(new CalendarEvent(id, ev.Summary, start, end, ev.AllDay, ev.Location, source, MeetingUrl: meeting));
            }
        }

        result.Sort((a, b) => a.Start.CompareTo(b.Start));
        return result;
    }

    private static void Finish(ICalEvent e)
    {
        if (e.HasEnd) return;
        e.End = e.Duration is TimeSpan d ? e.Start + d
              : e.AllDay ? e.Start.AddDays(1)
              : e.Start.AddHours(1);
    }

    private static bool IsExcluded(ICalEvent ev, DateTime start) =>
        ev.ExDates.Any(x => ev.AllDay ? x.Date == start.Date : x == start);

    private static IEnumerable<DateTime> Occurrences(ICalEvent ev, DateTime to)
    {
        if (string.IsNullOrWhiteSpace(ev.RRule))
        {
            yield return ev.Start;
            yield break;
        }

        var rule = ev.RRule.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .GroupBy(p => p[0].ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First()[1]);

        var freq = rule.GetValueOrDefault("FREQ", "").ToUpperInvariant();
        var interval = int.TryParse(rule.GetValueOrDefault("INTERVAL"), out var iv) && iv > 0 ? iv : 1;
        int? count = int.TryParse(rule.GetValueOrDefault("COUNT"), out var c) ? c : null;
        DateTime? until = null;
        if (rule.TryGetValue("UNTIL", out var untilText))
        {
            try
            {
                var (u, dateOnly) = ParseDate(untilText, "");
                until = dateOnly ? u.AddDays(1).AddTicks(-1) : u;
            }
            catch (FormatException) { }
        }

        var produced = 0;
        var guard = 0;

        if (freq == "WEEKLY" && rule.TryGetValue("BYDAY", out var byDayText))
        {
            var offsets = byDayText.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(d => ParseWeekday(d.Trim()))
                .Where(d => d.HasValue)
                .Select(d => ((int)d!.Value + 6) % 7) // Monday = 0
                .Distinct()
                .Order()
                .ToArray();
            if (offsets.Length == 0) offsets = [((int)ev.Start.DayOfWeek + 6) % 7];

            var weekStart = ev.Start.Date.AddDays(-(((int)ev.Start.DayOfWeek + 6) % 7));
            var timeOfDay = ev.Start.TimeOfDay;
            for (var week = 0; guard++ < MaxIterations; week += interval)
            {
                var ws = weekStart.AddDays(7.0 * week);
                foreach (var off in offsets)
                {
                    var occ = ws.AddDays(off) + timeOfDay;
                    if (occ < ev.Start) continue;
                    if (until.HasValue && occ > until.Value) yield break;
                    if (count.HasValue && produced >= count.Value) yield break;
                    if (occ >= to) yield break;
                    produced++;
                    yield return occ;
                }
            }
            yield break;
        }

        for (var k = 0; guard++ < MaxIterations; k++)
        {
            DateTime occ;
            try
            {
                occ = freq switch
                {
                    "DAILY" => ev.Start.AddDays((double)k * interval),
                    "WEEKLY" => ev.Start.AddDays(7.0 * k * interval),
                    "MONTHLY" => ev.Start.AddMonths(k * interval),
                    "YEARLY" => ev.Start.AddYears(k * interval),
                    _ => k == 0 ? ev.Start : DateTime.MaxValue,
                };
            }
            catch (ArgumentOutOfRangeException)
            {
                yield break;
            }

            if (occ == DateTime.MaxValue) yield break;
            if (until.HasValue && occ > until.Value) yield break;
            if (count.HasValue && produced >= count.Value) yield break;
            if (occ >= to) yield break;
            produced++;
            yield return occ;
        }
    }

    private static DayOfWeek? ParseWeekday(string token)
    {
        // strip ordinal prefixes like "1MO" / "-1FR" (treated as plain weekday)
        var code = new string(token.Where(char.IsLetter).ToArray()).ToUpperInvariant();
        return code switch
        {
            "MO" => DayOfWeek.Monday,
            "TU" => DayOfWeek.Tuesday,
            "WE" => DayOfWeek.Wednesday,
            "TH" => DayOfWeek.Thursday,
            "FR" => DayOfWeek.Friday,
            "SA" => DayOfWeek.Saturday,
            "SU" => DayOfWeek.Sunday,
            _ => null,
        };
    }

    internal static (DateTime Value, bool DateOnly) ParseDate(string value, string parameters)
    {
        value = value.Trim();
        var isDate = parameters.Contains("VALUE=DATE", StringComparison.OrdinalIgnoreCase)
                     && !parameters.Contains("VALUE=DATE-TIME", StringComparison.OrdinalIgnoreCase);
        if (isDate || value.Length == 8)
        {
            var d = DateTime.ParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture);
            return (d, true);
        }

        var utc = value.EndsWith('Z');
        var core = utc ? value[..^1] : value;
        var dt = DateTime.ParseExact(core, ["yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm"], CultureInfo.InvariantCulture,
            DateTimeStyles.None);

        if (utc) return (DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime(), false);

        var tzid = GetParameter(parameters, "TZID");
        if (tzid is not null)
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(tzid);
                return (TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), tz, TimeZoneInfo.Local), false);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
            {
                // unknown zone → treat as floating local time
            }
        }

        return (dt, false);
    }

    private static string? GetParameter(string parameters, string name)
    {
        foreach (var p in parameters.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = p.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals(name, StringComparison.OrdinalIgnoreCase))
                return kv[1].Trim('"');
        }
        return null;
    }

    internal static TimeSpan ParseDuration(string value)
    {
        var m = DurationRegex().Match(value.Trim());
        if (!m.Success) throw new FormatException($"Bad duration '{value}'");
        int G(int i) => m.Groups[i].Success ? int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture) : 0;
        var span = new TimeSpan(G(2) * 7 + G(3), G(4), G(5), G(6));
        return m.Groups[1].Value == "-" ? -span : span;
    }

    private static int FindValueColon(string line)
    {
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') inQuotes = !inQuotes;
            else if (line[i] == ':' && !inQuotes) return i;
        }
        return -1;
    }

    private static string Unescape(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                var n = value[++i];
                sb.Append(n is 'n' or 'N' ? '\n' : n);
            }
            else
            {
                sb.Append(value[i]);
            }
        }
        return sb.ToString();
    }

    private static IEnumerable<string> Unfold(string ics)
    {
        string? pending = null;
        foreach (var raw in ics.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t') && pending is not null)
            {
                pending += line[1..];
                continue;
            }
            if (pending is not null) yield return pending;
            pending = line;
        }
        if (pending is not null) yield return pending;
    }

    [GeneratedRegex(@"^([+-])?P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$")]
    private static partial Regex DurationRegex();
}
