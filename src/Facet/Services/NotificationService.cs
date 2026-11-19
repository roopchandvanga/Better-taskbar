using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Facet.Core.Notifications;
using Facet.Infrastructure;
using Facet.Interop;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Facet.Services;

/// <summary>
/// Windows' notification center for the dock's bell: how many notifications arrived since the user last opened
/// it from the dock, and whether Windows' do not disturb is on. Windows only raises change events for apps with a
/// package identity, so this polls; a query takes a few milliseconds. Nothing is read while the bell is hidden.
/// </summary>
public sealed class NotificationService(SettingsService settings) : ObservableObject
{
    /// <summary>One notification as the bell's preview shows it.</summary>
    public sealed record Preview(string App, string Title, string? Body, DateTimeOffset Arrived, string AppKey);

    private const int PreviewCount = 3;
    private readonly Dictionary<string, ImageSource?> _logos = [];
    private uint[] _latestIds = [];
    private IReadOnlyList<Preview> _latest = [];

    /// <summary>The newest few notifications (newest first), for the preview on the bell.</summary>
    public IReadOnlyList<Preview> Latest { get => _latest; private set => Set(ref _latest, value); }

    /// <summary>The app's logo once it has loaded (null until then, or if it has none).</summary>
    public ImageSource? LogoFor(string appKey) => _logos.GetValueOrDefault(appKey);

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private DateTimeOffset _lastOpened = DateTimeOffset.MinValue; // never opened: everything waiting counts
    private DateTimeOffset[] _arrived = [];
    private bool _busy;
    private bool _canCount;
    private int _total;
    private int _unseen;
    private bool _doNotDisturb;

    /// <summary>Windows lets Facet read the notification center (Settings › Privacy › Notifications).</summary>
    public bool CanCount { get => _canCount; private set => Set(ref _canCount, value); }

    /// <summary>Notifications in the notification center.</summary>
    public int Total { get => _total; private set => Set(ref _total, value); }

    /// <summary>Notifications that arrived since the center was last opened from the dock.</summary>
    public int Unseen { get => _unseen; private set => Set(ref _unseen, value); }

    /// <summary>When the notification center was last opened from the dock (MinValue: not yet).</summary>
    public DateTimeOffset LastOpened => _lastOpened;

    /// <summary>Windows' do not disturb (or a focus session) is holding notifications back.</summary>
    public bool DoNotDisturb { get => _doNotDisturb; private set => Set(ref _doNotDisturb, value); }

    public void Start()
    {
        _timer.Tick += (_, _) => _ = RefreshAsync();
        settings.Changed += () => _ = RefreshAsync();
        _ = RefreshAsync();
        _timer.Start();
    }

    /// <summary>Windows' notification center (Win+N on Windows 11; the action center on Windows 10).</summary>
    public void OpenCenter()
    {
        _lastOpened = DateTimeOffset.Now;
        Unseen = 0;
        // after the click (or the palette closing) has settled, so the flyout opens over the right window
        _ui.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (Environment.OSVersion.Version.Build >= 22000) Native.PressWindowsShortcut(0x4E /* N */);
            else AppServices.Open("ms-actioncenter:");
        });
    }

    public void OpenSettings() => AppServices.Open("ms-settings:notifications");

    /// <summary>Where Windows decides whether apps may read notifications.</summary>
    public void OpenAccessSettings() => AppServices.Open("ms-settings:privacy-notifications");

    private async Task RefreshAsync()
    {
        if (_busy || !settings.Current.ShowNotifications) return;
        _busy = true;
        try
        {
            DoNotDisturb = ReadDoNotDisturb();

            var listener = UserNotificationListener.Current;
            if (listener.GetAccessStatus() == UserNotificationListenerAccessStatus.Allowed)
            {
                var list = await listener.GetNotificationsAsync(NotificationKinds.Toast);
                _arrived = list.Select(n => n.CreationTime).ToArray();
                CanCount = true;
                UpdateLatest(list);
            }
            else
            {
                _arrived = [];
                CanCount = false;
                Latest = [];
                _latestIds = [];
            }
        }
        catch (Exception ex)
        {
            // the notification platform isn't there (Windows N editions, a stopped service): just the bell then
            Debug.WriteLine($"Notification query failed: {ex.Message}");
            _arrived = [];
            CanCount = false;
            Latest = [];
            _latestIds = [];
        }
        finally
        {
            _busy = false;
        }

        Total = _arrived.Length;
        Unseen = NotificationBadge.Unseen(_arrived, _lastOpened);
    }

    /// <summary>Read the newest few (only when they changed: text and app name come from each notification).</summary>
    private void UpdateLatest(IReadOnlyList<UserNotification> all)
    {
        var newest = all.OrderByDescending(n => n.CreationTime).Take(PreviewCount).ToList();
        var ids = newest.Select(n => n.Id).ToArray();
        if (ids.SequenceEqual(_latestIds)) return;
        _latestIds = ids;

        var previews = new List<Preview>();
        foreach (var n in newest)
        {
            try
            {
                var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric)
                              ?? n.Notification?.Visual?.Bindings.FirstOrDefault();
                var texts = binding?.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? [];
                string app = "App", key = "";
                try
                {
                    app = n.AppInfo.DisplayInfo.DisplayName;
                    key = n.AppInfo.AppUserModelId;
                }
                catch (Exception ex) { Debug.WriteLine($"Notification app info: {ex.Message}"); }
                previews.Add(new Preview(app, texts.FirstOrDefault() ?? app, texts.Count > 1 ? string.Join(" ", texts.Skip(1)) : null,
                    n.CreationTime, key));
                if (key.Length > 0 && !_logos.ContainsKey(key)) _ = LoadLogoAsync(n.AppInfo, key);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Notification preview failed: {ex.Message}");
            }
        }
        Latest = previews;
    }

    private async Task LoadLogoAsync(Windows.ApplicationModel.AppInfo info, string key)
    {
        _logos[key] = null; // once per app, even if it fails
        try
        {
            var stream = await info.DisplayInfo.GetLogo(new Windows.Foundation.Size(32, 32)).OpenReadAsync();
            using var source = stream.AsStreamForRead();
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = source;
            image.DecodePixelWidth = 40;
            image.EndInit();
            image.Freeze();
            _logos[key] = image;
            OnPropertyChanged(nameof(Latest));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Notification logo failed: {ex.Message}");
        }
    }

    private static bool ReadDoNotDisturb()
    {
        try
        {
            return ToastNotificationManager.GetDefault().NotificationMode != ToastNotificationMode.Unrestricted;
        }
        catch (Exception ex)
        {
            // older Windows: no notification mode to read
            Debug.WriteLine($"Notification mode unavailable: {ex.Message}");
            return false;
        }
    }
}
