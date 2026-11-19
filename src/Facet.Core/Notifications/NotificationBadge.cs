using System.Globalization;

namespace Facet.Core.Notifications;

/// <summary>
/// The count on the dock's notification bell: notifications in Windows' notification center that arrived after
/// the user last opened it from the dock, like the badge on the Windows 11 taskbar.
/// </summary>
public static class NotificationBadge
{
    /// <summary>How many of the notifications (by arrival time) are newer than <paramref name="lastOpened"/>.</summary>
    public static int Unseen(IEnumerable<DateTimeOffset> arrived, DateTimeOffset lastOpened) =>
        arrived.Count(t => t > lastOpened);

    /// <summary>Badge text: nothing for none, the number up to 9, then "9+".</summary>
    public static string Text(int count) =>
        count <= 0 ? "" : count > 9 ? "9+" : count.ToString(CultureInfo.InvariantCulture);

    /// <summary>How long ago a notification arrived, compact: "now", "5 min", "2 h", "yesterday", "3 d", "Nov 2".</summary>
    public static string Ago(DateTimeOffset arrived, DateTimeOffset now)
    {
        var span = now - arrived;
        if (span < TimeSpan.FromMinutes(1)) return "now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min";
        if (span < TimeSpan.FromDays(1) && arrived.LocalDateTime.Date == now.LocalDateTime.Date) return $"{(int)span.TotalHours} h";
        if (arrived.LocalDateTime.Date == now.LocalDateTime.Date.AddDays(-1)) return "yesterday";
        if (span < TimeSpan.FromDays(7)) return $"{Math.Max(1, (int)Math.Round(span.TotalDays))} d";
        return arrived.LocalDateTime.ToString("MMM d", CultureInfo.InvariantCulture);
    }

    /// <summary>The bell's tooltip.</summary>
    public static string Tooltip(bool canCount, int unseen, int total, bool doNotDisturb)
    {
        var parts = new List<string>();
        if (doNotDisturb) parts.Add("Do not disturb is on");
        if (canCount)
        {
            parts.Add(unseen switch
            {
                0 when total == 0 => "No notifications",
                0 => "No new notifications",
                1 => "1 new notification",
                _ => $"{unseen} new notifications",
            });
        }
        else if (!doNotDisturb)
        {
            parts.Add("Notifications");
        }
        parts.Add("click for the notification center (Win+N)");
        return string.Join(" · ", parts);
    }
}
