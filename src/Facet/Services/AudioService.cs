using System.Windows.Threading;
using Facet.Infrastructure;
using Facet.Interop;

namespace Facet.Services;

public sealed class AudioService : ObservableObject
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    private double _volume;
    private bool _muted, _hasSpeakers, _hasMic, _micMuted;

    /// <summary>0..100</summary>
    public double Volume { get => _volume; private set { if (Set(ref _volume, value)) OnPropertyChanged(nameof(VolumeGlyph)); } }
    public bool Muted { get => _muted; private set { if (Set(ref _muted, value)) OnPropertyChanged(nameof(VolumeGlyph)); } }
    public bool HasSpeakers { get => _hasSpeakers; private set => Set(ref _hasSpeakers, value); }
    public bool HasMic { get => _hasMic; private set => Set(ref _hasMic, value); }
    public bool MicMuted { get => _micMuted; private set { if (Set(ref _micMuted, value)) OnPropertyChanged(nameof(MicGlyph)); } }

    // Segoe Fluent / MDL2 glyphs
    // full-width speaker / muted glyphs: the per-level glyphs (E993-E995) are drawn left-heavy and never
    // sit centred next to the mic; the exact level is in the tooltip and the quick panel
    public string VolumeGlyph => Muted || Volume < 1 ? "" : "";
    public string MicGlyph => MicMuted ? "" : "";

    public void Start()
    {
        Poll();
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
    }

    public void SetVolume(double percent)
    {
        AudioInterop.SetSpeakerVolume((float)(percent / 100.0));
        if (Muted && percent > 0) AudioInterop.SetSpeakerMute(false);
        Poll();
    }

    public void Step(double deltaPercent) => SetVolume(Math.Clamp(Volume + deltaPercent, 0, 100));

    public void ToggleMute()
    {
        AudioInterop.SetSpeakerMute(!Muted);
        Poll();
    }

    public void ToggleMic()
    {
        AudioInterop.SetMicMute(!MicMuted);
        Poll();
    }

    private void Poll()
    {
        var speakers = AudioInterop.GetSpeakers();
        HasSpeakers = speakers.Available;
        Volume = Math.Round(speakers.Volume * 100);
        Muted = speakers.Muted;

        var mic = AudioInterop.GetMicrophone();
        HasMic = mic.Available;
        MicMuted = mic.Muted;
    }
}
