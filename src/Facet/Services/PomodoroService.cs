using System.Windows.Threading;
using Facet.Infrastructure;

namespace Facet.Services;

public enum PomodoroPhase { Idle, Focus, Break }

public sealed class PomodoroService(SettingsService settings) : ObservableObject
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private PomodoroPhase _phase;
    private bool _running;
    private DateTime _endsAt;
    private TimeSpan _remaining;
    private TimeSpan _total;

    public event Action<PomodoroPhase>? PhaseCompleted;

    public PomodoroPhase Phase { get => _phase; private set { if (Set(ref _phase, value)) Notify(); } }
    public bool Running { get => _running; private set { if (Set(ref _running, value)) Notify(); } }
    public TimeSpan Remaining { get => _remaining; private set { if (Set(ref _remaining, value)) Notify(); } }

    public bool IsFocusing => Phase == PomodoroPhase.Focus && Running;
    public string Display => Phase == PomodoroPhase.Idle
        ? $"{settings.Current.PomodoroFocusMinutes}:00"
        : $"{(int)Remaining.TotalMinutes}:{Remaining.Seconds:00}";
    /// <summary>0..100 of the current phase elapsed.</summary>
    public double Progress => Phase == PomodoroPhase.Idle || _total.TotalSeconds <= 0
        ? 0
        : Math.Clamp(100 * (1 - Remaining.TotalSeconds / _total.TotalSeconds), 0, 100);
    public string Label => Phase switch
    {
        PomodoroPhase.Focus => Running ? "Focus" : "Paused",
        PomodoroPhase.Break => Running ? "Break" : "Paused",
        _ => "Pomodoro",
    };

    public void Start()
    {
        _timer.Tick += (_, _) => Tick();
        settings.Changed += Notify;
    }

    /// <summary>Click behaviour: start a focus session, or pause/resume the current phase.</summary>
    public void Toggle()
    {
        if (Phase == PomodoroPhase.Idle) { Begin(PomodoroPhase.Focus); return; }
        if (Running)
        {
            Remaining = _endsAt - DateTime.Now;
            Running = false;
            _timer.Stop();
        }
        else
        {
            _endsAt = DateTime.Now + Remaining;
            Running = true;
            _timer.Start();
        }
    }

    public void Reset()
    {
        _timer.Stop();
        Running = false;
        Phase = PomodoroPhase.Idle;
        Remaining = TimeSpan.Zero;
    }

    public void Skip()
    {
        if (Phase == PomodoroPhase.Focus) Begin(PomodoroPhase.Break);
        else Reset();
    }

    private void Begin(PomodoroPhase phase)
    {
        var minutes = phase == PomodoroPhase.Focus ? settings.Current.PomodoroFocusMinutes : settings.Current.PomodoroBreakMinutes;
        _total = TimeSpan.FromMinutes(minutes);
        _endsAt = DateTime.Now + _total;
        Phase = phase;
        Remaining = _total;
        Running = true;
        _timer.Start();
    }

    private void Tick()
    {
        if (!Running) return;
        var left = _endsAt - DateTime.Now;
        if (left > TimeSpan.Zero)
        {
            Remaining = left;
            return;
        }

        var finished = Phase;
        if (finished == PomodoroPhase.Focus) Begin(PomodoroPhase.Break);
        else Reset();
        PhaseCompleted?.Invoke(finished);
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(Display));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(IsFocusing));
    }
}
