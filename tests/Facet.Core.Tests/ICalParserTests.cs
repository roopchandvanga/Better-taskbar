using Facet.Core.Calendar;

namespace Facet.Core.Tests;

public class ICalParserTests
{
    private static string Cal(params string[] lines) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + string.Join("\r\n", lines) + "\r\nEND:VCALENDAR\r\n";

    private static readonly DateTime From = new(2026, 10, 1);
    private static readonly DateTime To = new(2026, 12, 31);

    [Fact]
    public void ParsesUtcEvent()
    {
        var ics = Cal("BEGIN:VEVENT", "UID:1", "SUMMARY:Standup", "DTSTART:20261117T090000Z",
                      "DTEND:20261117T091500Z", "LOCATION:Zoom", "END:VEVENT");
        var e = Assert.Single(ICalParser.ParseAndExpand(ics, From, To, "test"));
        Assert.Equal("Standup", e.Title);
        Assert.Equal("Zoom", e.Location);
        Assert.False(e.AllDay);
        Assert.Equal(new DateTime(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc).ToLocalTime(), e.Start);
        Assert.Equal(TimeSpan.FromMinutes(15), e.End - e.Start);
        Assert.Equal("test", e.Source);
    }

    [Fact]
    public void ParsesAllDayEvent()
    {
        var ics = Cal("BEGIN:VEVENT", "UID:2", "SUMMARY:Holiday", "DTSTART;VALUE=DATE:20261120",
                      "DTEND;VALUE=DATE:20261121", "END:VEVENT");
        var e = Assert.Single(ICalParser.ParseAndExpand(ics, From, To, "t"));
        Assert.True(e.AllDay);
        Assert.Equal(new DateTime(2026, 11, 20), e.Start);
        Assert.Equal(new DateTime(2026, 11, 21), e.End);
    }

    [Fact]
    public void AllDayWithoutEndLastsOneDay()
    {
        var ics = Cal("BEGIN:VEVENT", "UID:2", "SUMMARY:Bday", "DTSTART;VALUE=DATE:20261120", "END:VEVENT");
        var e = Assert.Single(ICalParser.ParseAndExpand(ics, From, To, "t"));
        Assert.Equal(new DateTime(2026, 11, 21), e.End);
    }

    [Fact]
    public void UnfoldsContinuationLines()
    {
        var ics = Cal("BEGIN:VEVENT", "UID:3", "SUMMARY:Design ", " review", "DTSTART:20261117T100000", "END:VEVENT");
        Assert.Equal("Design review", Assert.Single(ICalParser.ParseAndExpand(ics, From, To, "t")).Title);
    }

    [Fact]
    public void UnescapesText()
    {
        var ics = Cal("BEGIN:VEVENT", "UID:4", @"SUMMARY:A\, B\; C\nD", "DTSTART:20261117T100000", "END:VEVENT");
        Assert.Equal("A, B; C\nD", Assert.Single(ICalParser.ParseAndExpand(ics, From, To, "t")).Title);
    }

    [Fact]
    public void FloatingTimeIsLocal()
    {
        var ics = Cal("BEGIN:VEVENT", "UID:5", "SUMMARY:x", "DTSTART:20261117T100000", "END:VEVENT");
        var e = Assert.Single(ICalParser.ParseAndExpand(ics, From, To, "t"));
        Assert.Equal(new DateTime(2026, 11, 17, 10, 0, 0), e.Start);
        Assert.Equal(TimeSpan.FromHours(1), e.End - e.Start); // default 1h when no DTEND
    }

    [Fact]
    public void TzidIsConverted()
    {
        var ics = Cal("BEGIN:VEVENT", "UID:6", "SUMMARY:x", "DTSTART;TZID=UTC:20261117T100000", "END:VEVENT");
        var e = Assert.Single(ICalParser.ParseAndExpand(ics, From, To, "t"));
        Assert.Equal(new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc).ToLocalTime(), e.Start);
    }

    [Fact]
    public void DurationIsHonoured()
    {
        var ics = Cal("BEGIN:VEVENT", "UID:7", "SUMMARY:x", "DTSTART:20261117T100000", "DURATION:PT1H30M", "END:VEVENT");
        var e = Assert.Single(ICalParser.ParseAndExpand(ics, From, To, "t"));
        Assert.Equal(TimeSpan.FromMinutes(90), e.End - e.Start);
    }

    [Fact]
    public void WeeklyCount()
    {
        var ics = Cal("BEGIN:VEVENT", "UID:8", "SUMMARY:w", "DTSTART:20261102T090000",
                      "RRULE:FREQ=WEEKLY;COUNT=3", "END:VEVENT");
        var starts = ICalParser.ParseAndExpand(ics, From, To, "t").Select(e => e.Start.Day).ToArray();
        Assert.Equal(new[] { 2, 9, 16 }, starts);
    }

    [Fact]
    public void DailyUntil()
    {
        var ics = Cal("BEGIN:VEVENT", "UID:9", "SUMMARY:d", "DTSTART:20261102T090000",
                      "RRULE:FREQ=DAILY;UNTIL=20261105T235959", "END:VEVENT");
        Assert.Equal(4, ICalParser.ParseAndExpand(ics, From, To, "t").Count);
    }

    [Fact]
    public void WeeklyByDayWithInterval()
    {
        // 2026-11-02 is a Monday
        var ics = Cal("BEGIN:VEVENT", "UID:10", "SUMMARY:b", "DTSTART:20261102T090000",
                      "RRULE:FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE;COUNT=4", "END:VEVENT");
        var days = ICalParser.ParseAndExpand(ics, From, To, "t").Select(e => e.Start.Day).ToArray();
        Assert.Equal(new[] { 2, 4, 16, 18 }, days);
    }

    [Fact]
    public void ExDateRemovesOccurrence()
    {
        var ics = Cal("BEGIN:VEVENT", "UID:11", "SUMMARY:e", "DTSTART:20261102T090000",
                      "RRULE:FREQ=DAILY;COUNT=3", "EXDATE:20261103T090000", "END:VEVENT");
        var days = ICalParser.ParseAndExpand(ics, From, To, "t").Select(e => e.Start.Day).ToArray();
        Assert.Equal(new[] { 2, 4 }, days);
    }

    [Fact]
    public void YearlyRecursIntoWindow()
    {
        var ics = Cal("BEGIN:VEVENT", "UID:12", "SUMMARY:bday", "DTSTART;VALUE=DATE:20101125",
                      "RRULE:FREQ=YEARLY", "END:VEVENT");
        var e = Assert.Single(ICalParser.ParseAndExpand(ics, From, To, "t"));
        Assert.Equal(new DateTime(2026, 11, 25), e.Start);
    }

    [Fact]
    public void EventsOutsideWindowExcluded()
    {
        var ics = Cal("BEGIN:VEVENT", "UID:13", "SUMMARY:old", "DTSTART:20200101T090000", "END:VEVENT");
        Assert.Empty(ICalParser.ParseAndExpand(ics, From, To, "t"));
    }

    [Fact]
    public void GarbageInputYieldsNothing() => Assert.Empty(ICalParser.ParseAndExpand("<html>nope</html>", From, To, "t"));
}
