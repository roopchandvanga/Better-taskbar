using Facet.Core.Alerts;
using Facet.Core.Calendar;

namespace Facet.Core.Tests;

public class AlertEngineTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 13, 40, 0);

    private static AlertSnapshot Snap(DateTime? now = null, IReadOnlyList<CalendarEvent>? events = null,
        int? battery = null, bool charging = false, double cpu = 10, bool media = false, bool quiet = false) =>
        new(now ?? Now, events ?? [], battery, charging, cpu, media, quiet);

    private static CalendarEvent Ev(DateTime start, bool allDay = false) =>
        new("e1", "Calculus lecture", start, start.AddHours(1), allDay);

    [Fact]
    public void EventSoonFiresOnce()
    {
        var engine = new AlertEngine();
        var s = Snap(events: [Ev(Now.AddMinutes(4))]);
        var a = Assert.Single(engine.Evaluate(s));
        Assert.Equal(AlertKind.Event, a.Kind);
        Assert.Contains("Calculus lecture", a.Title);
        Assert.Empty(engine.Evaluate(s));
    }

    [Fact]
    public void EventOutsideLeadWindowIgnored()
    {
        var engine = new AlertEngine();
        Assert.Empty(engine.Evaluate(Snap(events: [Ev(Now.AddMinutes(10))])));
        Assert.Empty(engine.Evaluate(Snap(events: [Ev(Now.AddMinutes(-1))])));
    }

    [Fact]
    public void AllDayEventsIgnored()
    {
        var engine = new AlertEngine();
        Assert.Empty(engine.Evaluate(Snap(events: [Ev(Now.AddMinutes(3), allDay: true)])));
    }

    [Fact]
    public void EventOffersPauseWhenMediaPlaying()
    {
        Assert.Equal(AlertAction.PauseMedia,
            Assert.Single(new AlertEngine().Evaluate(Snap(events: [Ev(Now.AddMinutes(2))], media: true))).Action);
        Assert.Equal(AlertAction.OpenCalendar,
            Assert.Single(new AlertEngine().Evaluate(Snap(events: [Ev(Now.AddMinutes(2))], media: false))).Action);
    }

    [Fact]
    public void BatteryLowFiresWhenDischargingAndRearms()
    {
        var engine = new AlertEngine();
        Assert.Equal(AlertKind.Battery, Assert.Single(engine.Evaluate(Snap(battery: 15))).Kind);
        Assert.Empty(engine.Evaluate(Snap(battery: 14)));
        Assert.Empty(engine.Evaluate(Snap(battery: 30, charging: true)));
        Assert.Single(engine.Evaluate(Snap(battery: 15)));
    }

    [Fact]
    public void BatteryIgnoredWhenCharging() =>
        Assert.Empty(new AlertEngine().Evaluate(Snap(battery: 5, charging: true)));

    [Fact]
    public void CpuHotFiresAndRearms()
    {
        var engine = new AlertEngine();
        Assert.Equal(AlertKind.Cpu, Assert.Single(engine.Evaluate(Snap(cpu: 95))).Kind);
        Assert.Empty(engine.Evaluate(Snap(cpu: 95)));
        Assert.Empty(engine.Evaluate(Snap(cpu: 80)));   // not low enough to re-arm
        Assert.Empty(engine.Evaluate(Snap(cpu: 95)));
        Assert.Empty(engine.Evaluate(Snap(cpu: 50)));   // re-armed
        Assert.Single(engine.Evaluate(Snap(cpu: 95)));
    }

    [Fact]
    public void QuietHoursHoldNonCriticalUntilOver()
    {
        var engine = new AlertEngine();
        var events = new[] { Ev(Now.AddMinutes(4)) };
        Assert.Empty(engine.Evaluate(Snap(events: events, quiet: true)));
        Assert.Single(engine.Evaluate(Snap(now: Now.AddMinutes(1), events: events, quiet: false)));
    }

    [Fact]
    public void CriticalBatteryBreaksQuietHours()
    {
        var a = Assert.Single(new AlertEngine().Evaluate(Snap(battery: 8, quiet: true)));
        Assert.True(a.Critical);
    }

    [Fact]
    public void DisabledRulesDoNothing()
    {
        var engine = new AlertEngine { Rules = new AlertRules { BatteryLow = false, CpuHot = false, EventSoon = false } };
        Assert.Empty(engine.Evaluate(Snap(events: [Ev(Now.AddMinutes(3))], battery: 5, cpu: 99)));
    }
}

public class QuietHoursTests
{
    [Theory]
    [InlineData("22:00", "07:00", "23:00", true)]
    [InlineData("22:00", "07:00", "06:59", true)]
    [InlineData("22:00", "07:00", "12:00", false)]
    [InlineData("22:00", "07:00", "07:00", false)]
    [InlineData("09:00", "17:00", "12:00", true)]
    [InlineData("09:00", "17:00", "18:00", false)]
    [InlineData("09:00", "09:00", "09:00", false)]
    public void IsActive(string start, string end, string at, bool expected) =>
        Assert.Equal(expected, QuietHours.IsActive(TimeSpan.Parse(start), TimeSpan.Parse(end),
            DateTime.Today + TimeSpan.Parse(at)));
}
