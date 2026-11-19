using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Facet.Infrastructure;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Facet.Services;

/// <summary>"Now Playing" through the Windows media session manager (Spotify, browsers, Media Player…).</summary>
public sealed class MediaService : ObservableObject
{
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(3) };
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string? _thumbKey;

    private bool _hasSession, _isPlaying;
    private string _title = "", _artist = "", _appName = "";
    private ImageSource? _thumbnail;

    public bool HasSession { get => _hasSession; private set => Set(ref _hasSession, value); }
    public bool IsPlaying { get => _isPlaying; private set => Set(ref _isPlaying, value); }
    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Artist { get => _artist; private set => Set(ref _artist, value); }
    public string AppName { get => _appName; private set => Set(ref _appName, value); }
    public ImageSource? Thumbnail { get => _thumbnail; private set => Set(ref _thumbnail, value); }

    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.SessionsChanged += (_, _) => OnUi(SelectSession);
            _manager.CurrentSessionChanged += (_, _) => OnUi(SelectSession);
            SelectSession();
            _poll.Tick += (_, _) => SelectSession();
            _poll.Start();
        }
        catch (Exception)
        {
            HasSession = false; // media service unavailable (e.g. N editions)
        }
    }

    public async Task PlayPauseAsync()
    {
        if (_session is null) return;
        try { await _session.TryTogglePlayPauseAsync(); } catch (Exception) { }
    }

    public async Task PauseAsync()
    {
        if (_session is null) return;
        try { await _session.TryPauseAsync(); } catch (Exception) { }
    }

    public async Task NextAsync()
    {
        if (_session is null) return;
        try { await _session.TrySkipNextAsync(); } catch (Exception) { }
    }

    public async Task PreviousAsync()
    {
        if (_session is null) return;
        try { await _session.TrySkipPreviousAsync(); } catch (Exception) { }
    }

    private static void OnUi(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);

    private void SelectSession()
    {
        if (_manager is null) return;
        GlobalSystemMediaTransportControlsSession? best = null;
        try
        {
            // prefer a session that is actually playing; fall back to the system's "current" one
            foreach (var s in _manager.GetSessions())
            {
                if (s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    best = s;
                    break;
                }
            }
            best ??= _manager.GetCurrentSession();
        }
        catch (Exception)
        {
            best = null;
        }

        if (!ReferenceEquals(best, _session))
        {
            if (_session is not null)
            {
                _session.MediaPropertiesChanged -= OnMediaChanged;
                _session.PlaybackInfoChanged -= OnPlaybackChanged;
            }
            _session = best;
            if (_session is not null)
            {
                _session.MediaPropertiesChanged += OnMediaChanged;
                _session.PlaybackInfoChanged += OnPlaybackChanged;
            }
        }

        _ = RefreshAsync();
    }

    private void OnMediaChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) =>
        OnUi(() => _ = RefreshAsync());

    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) =>
        OnUi(() => _ = RefreshAsync());

    private async Task RefreshAsync()
    {
        var session = _session;
        if (session is null)
        {
            HasSession = false;
            IsPlaying = false;
            return;
        }

        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (!ReferenceEquals(session, _session)) return;

            Title = props?.Title ?? "";
            Artist = string.IsNullOrWhiteSpace(props?.Artist) ? props?.AlbumTitle ?? "" : props!.Artist;
            AppName = FriendlyAppName(session.SourceAppUserModelId);
            IsPlaying = session.GetPlaybackInfo()?.PlaybackStatus ==
                        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            HasSession = !string.IsNullOrWhiteSpace(Title);

            var key = $"{session.SourceAppUserModelId}|{Title}|{Artist}";
            if (key != _thumbKey)
            {
                _thumbKey = key;
                Thumbnail = await LoadThumbnailAsync(props?.Thumbnail);
            }
        }
        catch (Exception)
        {
            // session vanished mid-call
        }
    }

    private static async Task<ImageSource?> LoadThumbnailAsync(IRandomAccessStreamReference? reference)
    {
        if (reference is null) return null;
        try
        {
            using var ras = await reference.OpenReadAsync();
            await using var stream = ras.AsStreamForRead();
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 200;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static string FriendlyAppName(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return "";
        var name = aumid;
        var bang = name.IndexOf('!');
        if (bang >= 0) name = name[(bang + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        var underscore = name.IndexOf('_');
        if (underscore > 0) name = name[..underscore];
        var lastDot = name.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < name.Length - 1) name = name[(lastDot + 1)..];
        return name switch
        {
            "MSEdge" => "Microsoft Edge",
            "ZuneMusic" => "Media Player",
            "ZuneVideo" => "Movies & TV",
            _ => name,
        };
    }
}
