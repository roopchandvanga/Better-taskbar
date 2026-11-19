using Facet.Core.Calendar;

namespace Facet.Core.Tests;

public class MeetingLinkTests
{
    [Theory]
    [InlineData("Join: https://teams.microsoft.com/l/meetup-join/19%3ameeting_abc%40thread.v2/0?context=%7b%22Tid%22%7d ok",
        "https://teams.microsoft.com/l/meetup-join/19%3ameeting_abc%40thread.v2/0?context=%7b%22Tid%22%7d", "Teams")]
    [InlineData("<https://us02web.zoom.us/j/81234567890?pwd=AbCd>", "https://us02web.zoom.us/j/81234567890?pwd=AbCd", "Zoom")]
    [InlineData("Join with Google Meet: https://meet.google.com/abc-defg-hij.", "https://meet.google.com/abc-defg-hij", "Meet")]
    [InlineData("https://acme.webex.com/meet/jdoe", "https://acme.webex.com/meet/jdoe", "Webex")]
    public void FindsJoinLinks(string text, string url, string service)
    {
        Assert.Equal(url, MeetingLink.Find(text));
        Assert.Equal(service, MeetingLink.Service(url));
    }

    [Theory]
    [InlineData("Conference room 4B")]
    [InlineData("Agenda: https://docs.google.com/document/d/123 and https://zoom.us/pricing")]
    [InlineData("https://teams.microsoft.com/v2/")]
    [InlineData(null)]
    public void IgnoresOtherText(string? text) => Assert.Null(MeetingLink.Find(text));

    [Fact]
    public void PrefersTheEarlierField() =>
        Assert.Equal("https://meet.google.com/aaa-bbbb-ccc",
            MeetingLink.Find(null, "https://meet.google.com/aaa-bbbb-ccc", "https://zoom.us/j/1"));

    [Fact]
    public void ParserFillsMeetingUrlFromDescriptionOrConferenceFields()
    {
        const string ics = """
            BEGIN:VCALENDAR
            BEGIN:VEVENT
            UID:a
            SUMMARY:Standup
            DTSTART:20261120T090000
            DTEND:20261120T091500
            DESCRIPTION:Join with Google Meet: https://meet.google.com/xyz-abcd-efg\nOr dial in
            END:VEVENT
            BEGIN:VEVENT
            UID:b
            SUMMARY:Review
            DTSTART:20261120T100000
            DTEND:20261120T110000
            LOCATION:Microsoft Teams Meeting
            X-MICROSOFT-SKYPETEAMSMEETINGURL:https://teams.microsoft.com/l/meetup-join/19%3ameeting_x%40thread.v2/0
            END:VEVENT
            BEGIN:VEVENT
            UID:c
            SUMMARY:Lunch
            DTSTART:20261120T120000
            DTEND:20261120T130000
            LOCATION:Cafe
            END:VEVENT
            END:VCALENDAR
            """;
        var events = ICalParser.ParseAndExpand(ics, new DateTime(2026, 11, 20), new DateTime(2026, 11, 21), "feed");
        Assert.Equal("https://meet.google.com/xyz-abcd-efg", events.Single(e => e.Title == "Standup").MeetingUrl);
        Assert.Equal("https://teams.microsoft.com/l/meetup-join/19%3ameeting_x%40thread.v2/0", events.Single(e => e.Title == "Review").MeetingUrl);
        Assert.Null(events.Single(e => e.Title == "Lunch").MeetingUrl);
    }
}
