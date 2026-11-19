using System.Text.Json;

namespace Facet.Core.Weather;

/// <summary>One day of a forecast, in the location's local calendar. Temperatures in °C.</summary>
public sealed record ForecastDay(DateOnly Date, string Symbol, double HighC, double LowC);

/// <summary>Current conditions plus daily outlook. <see cref="Symbol"/> is a MET Norway symbol code, e.g. "partlycloudy_day".</summary>
public sealed record Forecast(double CurrentC, string Symbol, IReadOnlyList<ForecastDay> Days);

/// <summary>
/// Parser for MET Norway's Locationforecast 2.0 (api.met.no, free, no key): Facet's fallback when Open-Meteo
/// doesn't answer. The feed is a UTC time series (hourly, then 6-hourly), so days are built by grouping
/// entries on the local date: high / low from the instant temperatures, and the outlook nearest mid-morning
/// as the day's symbol.
/// </summary>
public static class MetNorway
{
    private const int RepresentativeHour = 10; // its 6-hour outlook covers the middle of the day

    public static Forecast Parse(string json, TimeZoneInfo zone, int days = 6)
    {
        using var doc = JsonDocument.Parse(json);
        var series = doc.RootElement.GetProperty("properties").GetProperty("timeseries");
        if (series.GetArrayLength() == 0) throw new JsonException("The forecast has no entries.");

        double? currentC = null;
        string? currentSymbol = null;
        var byDate = new SortedDictionary<DateOnly, Day>();

        foreach (var entry in series.EnumerateArray())
        {
            var local = TimeZoneInfo.ConvertTime(entry.GetProperty("time").GetDateTimeOffset(), zone);
            var data = entry.GetProperty("data");
            var temp = data.GetProperty("instant").GetProperty("details").GetProperty("air_temperature").GetDouble();

            if (currentC is null)
            {
                currentC = temp;
                currentSymbol = SymbolOf(data, "next_1_hours") ?? SymbolOf(data, "next_6_hours") ?? SymbolOf(data, "next_12_hours");
            }

            var date = DateOnly.FromDateTime(local.DateTime);
            if (!byDate.TryGetValue(date, out var day)) byDate[date] = day = new Day(temp);
            day.Add(temp, local.Hour,
                SymbolOf(data, "next_6_hours") ?? SymbolOf(data, "next_1_hours") ?? SymbolOf(data, "next_12_hours"));
        }

        var result = byDate.Take(Math.Max(0, days))
            .Select(kv => new ForecastDay(kv.Key, kv.Value.Symbol ?? currentSymbol ?? "cloudy", kv.Value.High, kv.Value.Low))
            .ToList();
        return new Forecast(currentC!.Value, currentSymbol ?? "cloudy", result);
    }

    private static string? SymbolOf(JsonElement data, string period) =>
        data.TryGetProperty(period, out var p) && p.TryGetProperty("summary", out var s)
        && s.TryGetProperty("symbol_code", out var code) && code.ValueKind == JsonValueKind.String
            ? code.GetString()
            : null;

    private sealed class Day(double first)
    {
        private int _symbolDistance = int.MaxValue;

        public double High { get; private set; } = first;
        public double Low { get; private set; } = first;
        public string? Symbol { get; private set; }

        public void Add(double temp, int hour, string? symbol)
        {
            High = Math.Max(High, temp);
            Low = Math.Min(Low, temp);
            var distance = Math.Abs(hour - RepresentativeHour);
            if (symbol is not null && distance < _symbolDistance)
            {
                _symbolDistance = distance;
                Symbol = symbol;
            }
        }
    }
}
