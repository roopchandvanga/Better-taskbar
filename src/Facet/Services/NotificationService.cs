using System.Diagnostics;
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
            }
            else
            {
                _arrived = [];
                CanCount = false;
            }
        }
        catch (Exception ex)
        {
            // the notification platform isn't there (Windows N editions, a stopped service): just the bell then
            Debug.WriteLine($"Notification query failed: {ex.Message}");
            _arrived = [];
            CanCount = false;
        }
        finally
        {
            _busy = false;
        }

        Total = _arrived.Length;
        Unseen = NotificationBadge.Unseen(_arrived, _lastOpened);
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
