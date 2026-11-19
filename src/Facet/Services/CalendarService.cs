using System.Net.Http;
using System.Windows.Threading;
using Facet.Core.Calendar;
using Facet.Core.Settings;
using Facet.Infrastructure;

namespace Facet.Services;

/// <summary>Local events (events.json) + read-only iCal subscriptions refreshed every 15 minutes.</summary>
public sealed class CalendarService : ObservableObject
{
    public const string LocalColor = "#0A84FF";
    private static readonly string[] FeedColors = ["#BF5AF2", "#30D158", "#FF9F0A", "#FF375F", "#64D2FF", "#FFD60A"];

    private readonly SettingsService _settings;
    private readonly string _path = Path.Combine(SettingsService.DataDir, "events.json");
    private readonly DispatcherTimer _feedTimer = new() { Interval = TimeSpan.FromMinutes(15) };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(30) };
    private List<CalendarEvent> _local;
    private List<CalendarEvent> _feeds = [];
    private string _feedKey = "";

    private CalendarEvent? _next;
    private string _nextWhen = "";
    private string _feedStatus = "";

    public CalendarService(SettingsService settings)
    {
        _settings = settings;
        _local = JsonStore.Load<List<CalendarEvent>>(_path);
        _settings.Changed += () =>
        {
            if (FeedKey() != _feedKey) _ = RefreshFeedsAsync();
        };
    }

    /// <summary>Raised whenever the event set changes.</summary>
    public event Action? Changed;

    public CalendarEvent? NextEvent { get => _next; private set => Set(ref _next, value); }
    public string NextWhen { get => _nextWhen; private set => Set(ref _nextWhen, value); }
    public string FeedStatus { get => _feedStatus; private set => Set(ref _feedStatus, value); }

    public void Start()
    {
        UpdateNext();
        _ = RefreshFeedsAsync();
        _feedTimer.Tick += (_, _) => _ = RefreshFeedsAsync();
        _feedTimer.Start();
        _tick.Tick += (_, _) => UpdateNext();
        _tick.Start();
    }

    public IReadOnlyList<CalendarEvent> GetEvents(DateTime from, DateTime to) =>
        _local.Concat(_feeds)
            .Where(e => e.Start < to && (e.End > from || e.Start >= from))
            .OrderBy(e => !e.AllDay)
            .ThenBy(e => e.Start)
            .ToList();

    public IReadOnlyList<CalendarEvent> Upcoming(TimeSpan window)
    {
        var now = DateTime.Now;
        return GetEvents(now.AddMinutes(-1), now + window);
    }

    public void Add(string title, DateTime start, TimeSpan duration, bool allDay = false)
    {
        var e = new CalendarEvent(Guid.NewGuid().ToString("N"), title.Trim(), start, start + duration, allDay,
            Source: "local", Color: LocalColor);
        _local.Add(e);
        PersistAndNotify();
    }

    public bool IsLocal(CalendarEvent e) => e.Source == "local";

    public void Remove(CalendarEvent e)
    {
        if (_local.RemoveAll(x => x.Id == e.Id) > 0) PersistAndNotify();
    }

    private void PersistAndNotify()
    {
        _local.RemoveAll(e => e.End < DateTime.Today.AddDays(-120)); // keep the file small
        try { JsonStore.Save(_path, _local); } catch (IOException) { }
        UpdateNext();
        Changed?.Invoke();
    }

    private string FeedKey() => string.Join("|", _settings.Current.ICalUrls);

    public async Task RefreshFeedsAsync()
    {
        _feedKey = FeedKey();
        var urls = _settings.Current.ICalUrls.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
        if (urls.Count == 0)
        {
            _feeds = [];
            FeedStatus = "";
            UpdateNext();
            Changed?.Invoke();
            return;
        }

        var from = DateTime.Today.AddDays(-40);
        var to = DateTime.Today.AddDays(100);
        var all = new List<CalendarEvent>();
        var failures = 0;
        for (var i = 0; i < urls.Count; i++)
        {
            var url = urls[i].Trim();
            if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url[9..];
            try
            {
                var ics = await AppServices.Http.GetStringAsync(url);
                var color = FeedColors[i % FeedColors.Length];
                all.AddRange(ICalParser.ParseAndExpand(ics, from, to, $"feed{i}").Select(e => e with { Color = color }));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException
                                           or InvalidOperationException)
            {
                failures++;
            }
        }

        _feeds = all;
        FeedStatus = failures == 0 ? $"{urls.Count} calendar(s) synced {DateTime.Now:t}" : $"{failures} calendar(s) failed to sync";
        UpdateNext();
        Changed?.Invoke();
    }

    private void UpdateNext()
    {
        var now = DateTime.Now;
        var next = GetEvents(now, now.AddHours(12))
            .Where(e => !e.AllDay && e.End > now)
            .OrderBy(e => e.Start)
            .FirstOrDefault();
        NextEvent = next;
        NextWhen = next is null ? "" : Describe(next, now);
    }

    internal static string Describe(CalendarEvent e, DateTime now)
    {
        if (e.Start <= now) return $"Now · until {e.End:t}";
        var until = e.Start - now;
        var rel = until.TotalMinutes < 60
            ? $"in {Math.Max(1, (int)Math.Ceiling(until.TotalMinutes))}m"
            : $"in {(int)until.TotalHours}h {until.Minutes}m";
        return $"{e.Start:t} · {rel}";
    }
}
