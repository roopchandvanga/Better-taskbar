using System.Runtime.InteropServices;
using System.Windows.Threading;
using Facet.Infrastructure;
using Facet.Interop;

namespace Facet.Services;

/// <summary>CPU / RAM / battery from cheap kernel calls (no performance counters).</summary>
public sealed class SystemMonitor : ObservableObject
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Queue<double> _cpuWindow = new();
    private ulong _lastIdle, _lastTotal;

    private double _cpu, _ram, _ramUsedGb, _ramTotalGb, _cpuAverage;
    private bool _hasBattery, _charging;
    private int _batteryPercent;

    public double Cpu { get => _cpu; private set => Set(ref _cpu, value); }
    public double Ram { get => _ram; private set => Set(ref _ram, value); }
    public double RamUsedGb { get => _ramUsedGb; private set => Set(ref _ramUsedGb, value); }
    public double RamTotalGb { get => _ramTotalGb; private set => Set(ref _ramTotalGb, value); }
    /// <summary>Mean CPU over the last ~60 s (used by alerts).</summary>
    public double CpuAverage { get => _cpuAverage; private set => Set(ref _cpuAverage, value); }
    public bool HasBattery { get => _hasBattery; private set => Set(ref _hasBattery, value); }
    public bool Charging { get => _charging; private set => Set(ref _charging, value); }
    public int BatteryPercent { get => _batteryPercent; private set => Set(ref _batteryPercent, value); }

    public string Summary => $"CPU {Cpu:0}% · Memory {Ram:0}% ({RamUsedGb:0.0} of {RamTotalGb:0} GB)";
    public string BatterySummary => HasBattery ? $"Battery {BatteryPercent}%{(Charging ? " · charging" : "")}" : "No battery";

    public void Start()
    {
        Sample();
        _timer.Tick += (_, _) => Sample();
        _timer.Start();
    }

    private void Sample()
    {
        if (Native.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var total = kernel.Value + user.Value; // kernel time includes idle
            if (_lastTotal != 0 && total > _lastTotal)
            {
                var idleDelta = idle.Value - _lastIdle;
                var totalDelta = total - _lastTotal;
                Cpu = Math.Round(Math.Clamp(100.0 * (1.0 - (double)idleDelta / totalDelta), 0, 100)); // whole numbers: redraw only on visible change
                _cpuWindow.Enqueue(Cpu);
                while (_cpuWindow.Count > 30) _cpuWindow.Dequeue();
                CpuAverage = _cpuWindow.Count >= 15 ? _cpuWindow.Average() : 0;
            }
            _lastIdle = idle.Value;
            _lastTotal = total;
        }

        var mem = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
        if (Native.GlobalMemoryStatusEx(ref mem))
        {
            Ram = mem.dwMemoryLoad;
            RamTotalGb = mem.ullTotalPhys / 1073741824.0;
            RamUsedGb = Math.Round((mem.ullTotalPhys - mem.ullAvailPhys) / 1073741824.0, 1);
        }

        if (Native.GetSystemPowerStatus(out var power))
        {
            var noBattery = (power.BatteryFlag & 128) != 0 || power.BatteryFlag == 255 || power.BatteryLifePercent == 255;
            HasBattery = !noBattery;
            if (HasBattery)
            {
                BatteryPercent = Math.Min(100, (int)power.BatteryLifePercent);
                Charging = power.ACLineStatus == 1;
            }
        }

        // tooltips: notify only when the visible text actually changes
        if (Summary != _lastSummary) { _lastSummary = Summary; OnPropertyChanged(nameof(Summary)); }
        if (BatterySummary != _lastBatterySummary) { _lastBatterySummary = BatterySummary; OnPropertyChanged(nameof(BatterySummary)); }
    }

    private string? _lastSummary, _lastBatterySummary;
}
