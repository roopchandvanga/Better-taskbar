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
