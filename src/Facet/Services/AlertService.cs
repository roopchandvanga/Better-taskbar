using System.Windows.Threading;
using Facet.Core.Alerts;
using Facet.Infrastructure;

namespace Facet.Services;

/// <summary>Runs the rules engine on a timer and raises alerts for the toast UI.</summary>
public sealed class AlertService(
    SettingsService settings,
    SystemMonitor system,
    MediaService media,
    CalendarService calendar,
    PomodoroService pomodoro) : ObservableObject
{
    private readonly AlertEngine _engine = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool _doNotDisturb;

    public event Action<Alert>? AlertRaised;

    /// <summary>Manual "do not disturb" from the quick panel (runtime only).</summary>
    public bool DoNotDisturb
    {
        get => _doNotDisturb;
        set { if (Set(ref _doNotDisturb, value)) OnPropertyChanged(nameof(QuietNow)); }
    }

    public bool QuietNow =>
        DoNotDisturb
        || (settings.Current.QuietHoursEnabled
            && QuietHours.IsActive(settings.Current.QuietStartTime, settings.Current.QuietEndTime, DateTime.Now))
        || (settings.Current.QuietDuringFocus && pomodoro.IsFocusing);

    public void Start()
    {
        pomodoro.PhaseCompleted += phase =>
        {
            var s = settings.Current;
            Post(phase == PomodoroPhase.Focus
                ? new Alert($"pomo:{DateTime.Now.Ticks}", "Focus session complete",
                    $"Nice work. Take a {s.PomodoroBreakMinutes}-minute break.", AlertKind.Pomodoro)
                : new Alert($"pomo:{DateTime.Now.Ticks}", "Break's over", "Ready for another round?", AlertKind.Pomodoro));
        };

        _timer.Tick += (_, _) => Evaluate();
        _timer.Start();
        calendar.Changed += Evaluate;
        pomodoro.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PomodoroService.IsFocusing)) OnPropertyChanged(nameof(QuietNow));
        };
        settings.Changed += () => OnPropertyChanged(nameof(QuietNow));
    }

    public void Post(Alert alert) => AlertRaised?.Invoke(alert);

    private void Evaluate()
    {
        OnPropertyChanged(nameof(QuietNow));
        _engine.Rules = settings.Current.Alerts;
        var now = DateTime.Now;
        var snapshot = new AlertSnapshot(
            now,
            calendar.Upcoming(TimeSpan.FromHours(1)),
            system.HasBattery ? system.BatteryPercent : null,
            system.Charging,
            system.CpuAverage,
            media.IsPlaying,
            QuietNow);

        foreach (var alert in _engine.Evaluate(snapshot)) Post(alert);
    }
}
