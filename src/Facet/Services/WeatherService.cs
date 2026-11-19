using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using Facet.Core.Weather;
using Facet.Infrastructure;

namespace Facet.Services;

public enum WeatherKind { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Snow, Thunder }

public sealed record DayForecast(string Day, WeatherKind Kind, bool IsDay, string High, string Low);

/// <summary>
/// Open-Meteo, falling back to MET Norway when it doesn't answer (neither needs a key or account).
/// Location: settings city → Windows location → prompt. Failed refreshes retry within minutes.
/// </summary>
public sealed class WeatherService : ObservableObject
{
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan[] RetryAfter =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)];
    /// <summary>Open-Meteo gets this long before the fallback is tried (an unanswered TLS handshake otherwise hangs ~20 s).</summary>
    private static readonly TimeSpan PrimaryTimeout = TimeSpan.FromSeconds(8);

    private readonly SettingsService _settings;
    private readonly DispatcherTimer _timer = new() { Interval = RefreshEvery };
    private int _failures;

    private bool _available, _isDay = true;
    private string _temperature = "--°", _description = "", _city = "", _status = "Loading weather…", _range = "";
    private WeatherKind _kind;

    public WeatherService(SettingsService settings)
    {
        _settings = settings;
        _settings.Changed += () => _ = RefreshAsync();
    }

    public bool Available { get => _available; private set => Set(ref _available, value); }
    public string Temperature { get => _temperature; private set => Set(ref _temperature, value); }
    public string Description { get => _description; private set => Set(ref _description, value); }
    public string City { get => _city; private set => Set(ref _city, value); }
    public string Range { get => _range; private set => Set(ref _range, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public WeatherKind Kind { get => _kind; private set => Set(ref _kind, value); }
    public bool IsDay { get => _isDay; private set => Set(ref _isDay, value); }
    public ObservableCollection<DayForecast> Daily { get; } = [];

    public void Start()
    {
        _ = RefreshAsync();
        _timer.Tick += (_, _) => _ = RefreshAsync();
        _timer.Start();
    }

    private string? _lastKey;

    public async Task RefreshAsync(bool force = false)
    {
        var s = _settings.Current;
        var key = $"{s.WeatherLatitude}|{s.WeatherLongitude}|{s.WeatherCity}|{s.Fahrenheit}";
        if (!force && key == _lastKey && Available && DateTime.Now - _lastFetch < TimeSpan.FromMinutes(15)) return;
        _lastKey = key;

        try
        {
            double lat, lon;
            string city;
            if (s.WeatherLatitude is double la && s.WeatherLongitude is double lo)
            {
                (lat, lon, city) = (la, lo, s.WeatherCity ?? "");
            }
            else if (!string.IsNullOrWhiteSpace(s.WeatherCity))
            {
                var geo = await GeocodeAsync(s.WeatherCity);
                if (geo is null) { Fail($"Couldn't find \"{s.WeatherCity}\""); return; }
                (lat, lon, city) = geo.Value;
                s.WeatherLatitude = lat;
                s.WeatherLongitude = lon;
                s.WeatherCity = city;
                _settings.Save();
                return; // Save() triggers a refresh with coordinates
            }
            else
            {
                var here = await TryWindowsLocationAsync();
                if (here is null) { Fail("Set your city in Settings"); return; }
                (lat, lon) = here.Value;
                city = "Your location";
            }

            await FetchAsync(lat, lon, city, s.Fahrenheit);
            _failures = 0;
            _timer.Interval = RefreshEvery;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException
                                       or InvalidOperationException or FormatException)
        {
            Fail(Available ? Status : "Weather service isn't responding · retrying");
            // retry soon (1, 2, 5, 10 min, then every 20) instead of leaving the tile empty for 20 minutes
            _timer.Interval = _failures < RetryAfter.Length ? RetryAfter[_failures] : RefreshEvery;
            _failures++;
        }
    }

    private DateTime _lastFetch;

    private async Task FetchAsync(double lat, double lon, string city, bool fahrenheit)
    {
        try
        {
            await FetchOpenMeteoAsync(lat, lon, city, fahrenheit);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            // Open-Meteo's forecast server can be unreachable from some networks (it accepts the connection but
            // never completes TLS) while everything else works; MET Norway covers the same ground
            await FetchMetNorwayAsync(lat, lon, city, fahrenheit);
        }
    }

    private async Task FetchMetNorwayAsync(double lat, double lon, string city, bool fahrenheit)
    {
        var inv = CultureInfo.InvariantCulture;
        var url = "https://api.met.no/weatherapi/locationforecast/2.0/compact" +
                  $"?lat={lat.ToString("0.00", inv)}&lon={lon.ToString("0.00", inv)}";
        var forecast = MetNorway.Parse(await AppServices.Http.GetStringAsync(url), TimeZoneInfo.Local);

        string Temp(double celsius) => $"{Math.Round(fahrenheit ? celsius * 9 / 5 + 32 : celsius):0}°";
        var (kind, description, isDay) = FromMetSymbol(forecast.Symbol);
        IsDay = isDay;
        Kind = kind;
        Description = description;
        Temperature = Temp(forecast.CurrentC);
        City = city;

        var today = DateOnly.FromDateTime(DateTime.Now);
        Daily.Clear();
        foreach (var day in forecast.Days)
        {
            var label = day.Date == today ? "Today" : day.Date.ToString("ddd", CultureInfo.CurrentCulture);
            Daily.Add(new DayForecast(label, FromMetSymbol(day.Symbol).Kind, true, Temp(day.HighC), Temp(day.LowC)));
        }
        Range = Daily.Count > 0 ? $"H {Daily[0].High}  L {Daily[0].Low}" : "";

        Available = true;
        Status = "";
        _lastFetch = DateTime.Now;
    }

    private async Task FetchOpenMeteoAsync(double lat, double lon, string city, bool fahrenheit)
    {
        var inv = CultureInfo.InvariantCulture;
        var url = "https://api.open-meteo.com/v1/forecast" +
                  $"?latitude={lat.ToString("0.00", inv)}&longitude={lon.ToString("0.00", inv)}" +
                  "&current=temperature_2m,weather_code,is_day" +
                  "&daily=weather_code,temperature_2m_max,temperature_2m_min" +
                  "&timezone=auto&forecast_days=6" +
                  (fahrenheit ? "&temperature_unit=fahrenheit" : "");

        using var timeout = new CancellationTokenSource(PrimaryTimeout);
        using var doc = JsonDocument.Parse(await AppServices.Http.GetStringAsync(url, timeout.Token));
        var current = doc.RootElement.GetProperty("current");
        var code = current.GetProperty("weather_code").GetInt32();
        IsDay = current.GetProperty("is_day").GetInt32() == 1;
        Kind = KindFor(code);
        Description = DescribeCode(code);
        Temperature = $"{Math.Round(current.GetProperty("temperature_2m").GetDouble()):0}°";
        City = city;

        var daily = doc.RootElement.GetProperty("daily");
        var times = daily.GetProperty("time");
        var codes = daily.GetProperty("weather_code");
        var highs = daily.GetProperty("temperature_2m_max");
        var lows = daily.GetProperty("temperature_2m_min");
        Daily.Clear();
        for (var i = 0; i < times.GetArrayLength(); i++)
        {
            var date = DateTime.Parse(times[i].GetString()!, inv);
            var label = i == 0 ? "Today" : date.ToString("ddd", CultureInfo.CurrentCulture);
            Daily.Add(new DayForecast(label, KindFor(codes[i].GetInt32()), true,
                $"{Math.Round(highs[i].GetDouble()):0}°", $"{Math.Round(lows[i].GetDouble()):0}°"));
        }
        Range = Daily.Count > 0 ? $"H {Daily[0].High}  L {Daily[0].Low}" : "";

        Available = true;
        Status = "";
        _lastFetch = DateTime.Now;
    }

    private void Fail(string message)
    {
        Status = message;
        if (Available) return;
        Temperature = message.StartsWith("Set", StringComparison.Ordinal) ? "Set city" : "--°";
        Description = message;
    }

    /// <summary>Resolve a city name to coordinates via Open-Meteo's geocoder.</summary>
    public static async Task<(double Lat, double Lon, string Name)?> GeocodeAsync(string city)
    {
        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city.Trim())}&count=1&format=json";
        using var doc = JsonDocument.Parse(await AppServices.Http.GetStringAsync(url));
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0) return null;
        var r = results[0];
        var name = r.GetProperty("name").GetString() ?? city;
        if (r.TryGetProperty("country_code", out var cc)) name += $", {cc.GetString()}";
        return (r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(), name);
    }

    private static async Task<(double, double)?> TryWindowsLocationAsync()
    {
        try
        {
            var access = await Windows.Devices.Geolocation.Geolocator.RequestAccessAsync();
            if (access != Windows.Devices.Geolocation.GeolocationAccessStatus.Allowed) return null;
            var locator = new Windows.Devices.Geolocation.Geolocator
            {
                DesiredAccuracy = Windows.Devices.Geolocation.PositionAccuracy.Default,
            };
            var task = locator.GetGeopositionAsync(TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(6)).AsTask();
            var pos = await task.WaitAsync(TimeSpan.FromSeconds(8));
            return (pos.Coordinate.Point.Position.Latitude, pos.Coordinate.Point.Position.Longitude);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>MET Norway symbol code ("lightrainshowers_day", "cloudy", …) → icon kind, label, day/night.</summary>
    internal static (WeatherKind Kind, string Description, bool IsDay) FromMetSymbol(string symbol)
    {
        var parts = symbol.Split('_');
        var name = parts[0];
        var isDay = parts.Length < 2 || parts[1] != "night";
        var (kind, description) = name switch
        {
            "clearsky" => (WeatherKind.Clear, "Clear"),
            "fair" => (WeatherKind.Clear, "Mainly clear"),
            "partlycloudy" => (WeatherKind.PartlyCloudy, "Partly cloudy"),
            "cloudy" => (WeatherKind.Cloudy, "Overcast"),
            "fog" => (WeatherKind.Fog, "Fog"),
            _ when name.Contains("thunder") => (WeatherKind.Thunder, "Thunderstorm"),
            _ when name.Contains("sleet") => (WeatherKind.Snow, "Sleet"),
            _ when name.Contains("snow") => (WeatherKind.Snow,
                name.Contains("showers") ? "Snow showers" : name.StartsWith("light") ? "Light snow" : name.StartsWith("heavy") ? "Heavy snow" : "Snow"),
            "lightrain" => (WeatherKind.Drizzle, "Light rain"),
            _ when name.Contains("rain") => (WeatherKind.Rain,
                name.Contains("showers") ? "Rain showers" : name.StartsWith("heavy") ? "Heavy rain" : "Rain"),
            _ => (WeatherKind.Cloudy, "—"),
        };
        return (kind, description, isDay);
    }

    internal static WeatherKind KindFor(int code) => code switch
    {
        0 or 1 => WeatherKind.Clear,
        2 => WeatherKind.PartlyCloudy,
        3 => WeatherKind.Cloudy,
        45 or 48 => WeatherKind.Fog,
        >= 51 and <= 57 => WeatherKind.Drizzle,
        (>= 61 and <= 67) or (>= 80 and <= 82) => WeatherKind.Rain,
        (>= 71 and <= 77) or 85 or 86 => WeatherKind.Snow,
        >= 95 => WeatherKind.Thunder,
        _ => WeatherKind.Cloudy,
    };

    internal static string DescribeCode(int code) => code switch
    {
        0 => "Clear",
        1 => "Mainly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing drizzle",
        61 => "Light rain",
        63 => "Rain",
        65 => "Heavy rain",
        66 or 67 => "Freezing rain",
        71 => "Light snow",
        73 => "Snow",
        75 => "Heavy snow",
        77 => "Snow grains",
        80 or 81 => "Rain showers",
        82 => "Heavy showers",
        85 or 86 => "Snow showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunderstorm with hail",
        _ => "—",
    };
}
