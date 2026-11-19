using Facet.Core.Notifications;

namespace Facet.Core.Tests;

public class NotificationBadgeTests
{
    private static readonly DateTimeOffset Opened = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CountsOnlyNotificationsThatArrivedAfterTheCenterWasOpened()
    {
        DateTimeOffset[] arrived = [Opened.AddMinutes(-5), Opened, Opened.AddSeconds(1), Opened.AddHours(2)];
        Assert.Equal(2, NotificationBadge.Unseen(arrived, Opened));
    }

    [Fact]
    public void NeverOpenedMeansEverythingIsNew() =>
        Assert.Equal(3, NotificationBadge.Unseen([Opened, Opened, Opened.AddDays(-30)], DateTimeOffset.MinValue));

    [Fact]
    public void AgoIsCompact()
    {
        var now = new DateTimeOffset(2026, 11, 19, 15, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 19)));
        Assert.Equal("now", NotificationBadge.Ago(now.AddSeconds(-20), now));
        Assert.Equal("5 min", NotificationBadge.Ago(now.AddMinutes(-5), now));
        Assert.Equal("3 h", NotificationBadge.Ago(now.AddHours(-3), now));
        Assert.Equal("yesterday", NotificationBadge.Ago(now.AddHours(-20), now));
        Assert.Equal("3 d", NotificationBadge.Ago(now.AddDays(-3), now));
        Assert.Equal("Nov 2", NotificationBadge.Ago(now.AddDays(-17), now));
    }

    [Theory]
    [InlineData(-1, "")]
    [InlineData(0, "")]
    [InlineData(1, "1")]
    [InlineData(9, "9")]
    [InlineData(10, "9+")]
    [InlineData(250, "9+")]
    public void BadgeText(int count, string expected) => Assert.Equal(expected, NotificationBadge.Text(count));

    [Theory]
    [InlineData(true, 0, 0, false, "No notifications · click for the notification center (Win+N)")]
    [InlineData(true, 0, 4, false, "No new notifications · click for the notification center (Win+N)")]
    [InlineData(true, 1, 4, false, "1 new notification · click for the notification center (Win+N)")]
    [InlineData(true, 3, 4, true, "Do not disturb is on · 3 new notifications · click for the notification center (Win+N)")]
    [InlineData(false, 0, 0, false, "Notifications · click for the notification center (Win+N)")]
    [InlineData(false, 0, 0, true, "Do not disturb is on · click for the notification center (Win+N)")]
    public void Tooltip(bool canCount, int unseen, int total, bool dnd, string expected) =>
        Assert.Equal(expected, NotificationBadge.Tooltip(canCount, unseen, total, dnd));
}
