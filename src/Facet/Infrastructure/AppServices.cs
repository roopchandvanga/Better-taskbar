using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using Facet.Apps;
using Facet.Services;
using Facet.Shell;

namespace Facet.Infrastructure;

/// <summary>Process-wide singletons. Created once in App.OnStartup.</summary>
internal static class AppServices
{
    public static readonly HttpClient Http = CreateHttp();

    public static SettingsService Settings { get; private set; } = null!;
    public static SystemMonitor System { get; private set; } = null!;
    public static MediaService Media { get; private set; } = null!;
    public static AudioService Audio { get; private set; } = null!;
    public static ConnectivityService Connectivity { get; private set; } = null!;
    public static NotificationService Notifications { get; private set; } = null!;
    public static WeatherService Weather { get; private set; } = null!;
    public static CalendarService Calendar { get; private set; } = null!;
    public static TodoService Todos { get; private set; } = null!;
    public static PomodoroService Pomodoro { get; private set; } = null!;
    public static CustomWidgetService CustomWidgets { get; private set; } = null!;
    public static AlertService Alerts { get; private set; } = null!;
    public static AppTracker Apps { get; private set; } = null!;
    public static AppCatalog Catalog { get; private set; } = null!;
    public static TaskbarController Taskbar { get; private set; } = null!;

    public static void Create()
    {
        Settings = new SettingsService();
        Taskbar = new TaskbarController(SettingsService.DataDir);
        System = new SystemMonitor();
        Media = new MediaService();
        Audio = new AudioService();
        Connectivity = new ConnectivityService();
        Notifications = new NotificationService(Settings);
        Weather = new WeatherService(Settings);
        Calendar = new CalendarService(Settings);
        Todos = new TodoService();
        Pomodoro = new PomodoroService(Settings);
        CustomWidgets = new CustomWidgetService(Settings);
        Alerts = new AlertService(Settings, System, Media, Calendar, Pomodoro);
        Apps = new AppTracker(Settings);
        Catalog = new AppCatalog();
    }

    public static void Start()
    {
        System.Start();
        _ = Media.StartAsync();
        Audio.Start();
        Connectivity.Start();
        Notifications.Start();
        Weather.Start();
        Calendar.Start();
        Pomodoro.Start();
        CustomWidgets.Start();
        Alerts.Start();
        Apps.Start();
        Catalog.LoadInBackground();
    }

    /// <summary>Open a URL, file, folder or shell target without blocking.</summary>
    public static void Open(string target, string? arguments = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, Arguments = arguments ?? "" });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Debug.WriteLine($"Open failed for {target}: {ex.Message}");
        }
    }

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Facet/0.1 (+desktop dock)");
        return http;
    }
}
