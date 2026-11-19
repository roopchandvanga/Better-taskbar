using System.Text.Json;
using Facet.Core.Weather;

namespace Facet.Core.Tests;

public class MetNorwayTests
{
    // trimmed Locationforecast 2.0 "compact" response: hourly entries, then a 6-hourly one without next_1_hours
    private const string Sample = """
    {
      "properties": {
        "meta": { "units": { "air_temperature": "celsius" } },
        "timeseries": [
          { "time": "2026-11-17T22:00:00Z", "data": {
              "instant": { "details": { "air_temperature": 8.5 } },
              "next_1_hours": { "summary": { "symbol_code": "cloudy" } },
              "next_6_hours": { "summary": { "symbol_code": "partlycloudy_night" } } } },
          { "time": "2026-11-17T23:00:00Z", "data": {
              "instant": { "details": { "air_temperature": 7.0 } },
              "next_1_hours": { "summary": { "symbol_code": "rain" } } } },
          { "time": "2026-11-18T06:00:00Z", "data": {
              "instant": { "details": { "air_temperature": 3.1 } },
              "next_6_hours": { "summary": { "symbol_code": "lightrain" } } } },
          { "time": "2026-11-18T12:00:00Z", "data": {
              "instant": { "details": { "air_temperature": 11.4 } },
              "next_6_hours": { "summary": { "symbol_code": "fair_day" } } } },
          { "time": "2026-11-18T18:00:00Z", "data": {
              "instant": { "details": { "air_temperature": 9.0 } },
              "next_6_hours": { "summary": { "symbol_code": "clearsky_night" } } } }
        ]
      }
    }
    """;

    [Fact]
    public void CurrentConditionsComeFromTheFirstEntry()
    {
        var f = MetNorway.Parse(Sample, TimeZoneInfo.Utc);
        Assert.Equal(8.5, f.CurrentC);
        Assert.Equal("cloudy", f.Symbol); // the next hour beats the next 6 hours
    }

    [Fact]
    public void DaysAreGroupedByLocalDateWithHighAndLow()
    {
        var f = MetNorway.Parse(Sample, TimeZoneInfo.Utc);
        Assert.Equal(2, f.Days.Count);
        Assert.Equal(new DateOnly(2026, 11, 17), f.Days[0].Date);
        Assert.Equal((8.5, 7.0), (f.Days[0].HighC, f.Days[0].LowC));
        Assert.Equal((11.4, 3.1), (f.Days[1].HighC, f.Days[1].LowC));
        Assert.Equal("fair_day", f.Days[1].Symbol); // the midday outlook represents the day
    }

    [Fact]
    public void LocalTimeZoneShiftsDayBoundaries()
    {
        // UTC-8: 22:00Z and 23:00Z on the 17th are still the afternoon of the 17th; 06:00Z on the 18th is 22:00 on the 17th
        var pacific = TimeZoneInfo.CreateCustomTimeZone("test-8", TimeSpan.FromHours(-8), "test", "test");
        var f = MetNorway.Parse(Sample, pacific);
        Assert.Equal(new DateOnly(2026, 11, 17), f.Days[0].Date);
        Assert.Equal(3.1, f.Days[0].LowC);
    }

    [Fact]
    public void LimitsTheNumberOfDays()
    {
        Assert.Single(MetNorway.Parse(Sample, TimeZoneInfo.Utc, days: 1).Days);
    }

    [Fact]
    public void EmptyForecastIsAnError()
    {
        Assert.Throws<JsonException>(() => MetNorway.Parse("""{ "properties": { "timeseries": [] } }""", TimeZoneInfo.Utc));
    }
}
