using System.Text.RegularExpressions;

namespace Facet.Core.Calendar;

/// <summary>Finds an online-meeting join link (Teams, Zoom, Google Meet, Webex, GoTo, Whereby, Chime) in event text.</summary>
public static partial class MeetingLink
{
    /// <summary>The first join link in the texts, checked in the order given (most specific field first).</summary>
    public static string? Find(params string?[] texts)
    {
        foreach (var text in texts)
        {
            if (string.IsNullOrEmpty(text)) continue;
            var match = Pattern().Match(text);
            if (match.Success) return match.Value.TrimEnd('.', ',', ';', ':', '!', '?');
        }
        return null;
    }

    /// <summary>Short name of the meeting service, for a "Join Teams" style label.</summary>
    public static string Service(string url)
    {
        var u = url.ToLowerInvariant();
        if (u.Contains("teams.microsoft.com") || u.Contains("teams.live.com")) return "Teams";
        if (u.Contains("zoom.us")) return "Zoom";
        if (u.Contains("meet.google.com")) return "Meet";
        if (u.Contains("webex.com")) return "Webex";
        if (u.Contains("goto")) return "GoTo";
        if (u.Contains("whereby.com")) return "Whereby";
        if (u.Contains("chime.aws")) return "Chime";
        return "meeting";
    }

    [GeneratedRegex(@"https://(?:[\w-]+\.)*(?:teams\.microsoft\.com/l/meetup-join/|teams\.live\.com/meet/|zoom\.us/(?:j|my|w|s)/|meet\.google\.com/[a-z]{3,4}-[a-z]{4}-[a-z]{3}\b|webex\.com/(?:meet|join|[\w.-]+/j\.php)|gotomeeting\.com/join/|meet\.goto\.com/|whereby\.com/[\w-]+|chime\.aws/\d)[^\s<>""'\]\)]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
