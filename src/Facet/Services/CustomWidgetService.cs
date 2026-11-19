using System.Diagnostics;
using System.Net.Http;
using System.Windows.Threading;
using Facet.Core.Settings;
using Facet.Core.Widgets;
using Facet.Infrastructure;

namespace Facet.Services;

/// <summary>A user-defined tile: shell command or URL, optional JSON path, refreshed on an interval.</summary>
public sealed class CustomWidgetViewModel : ObservableObject
{
    private readonly DispatcherTimer _timer = new();
    private string _value = "…";
    private string _tooltip = "";
    private bool _busy;

    public CustomWidgetViewModel(CustomWidgetConfig config)
    {
        Config = config;
        _timer.Interval = TimeSpan.FromSeconds(Math.Max(5, config.IntervalSeconds));
        _timer.Tick += (_, _) => _ = RefreshAsync();
    }

    public CustomWidgetConfig Config { get; }
    public string Name => Config.Name;
    public string Value { get => _value; private set => Set(ref _value, value); }
    public string Tooltip { get => _tooltip; private set => Set(ref _tooltip, value); }

    public void Start()
    {
        _ = RefreshAsync();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public void Click()
    {
        if (!string.IsNullOrWhiteSpace(Config.ClickTarget)) AppServices.Open(Config.ClickTarget);
        else _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_busy || string.IsNullOrWhiteSpace(Config.Source)) { if (string.IsNullOrWhiteSpace(Config.Source)) Value = "not set"; return; }
        _busy = true;
        try
        {
            var raw = Config.Kind == CustomWidgetKind.Url
                ? await AppServices.Http.GetStringAsync(Config.Source)
                : await RunCommandAsync(Config.Source);

            string? text = string.IsNullOrWhiteSpace(Config.JsonPath)
                ? raw.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0)
                : JsonPathLite.Extract(raw, Config.JsonPath);

            text ??= "—";
            if (text.Length > 28) text = text[..27] + "…";
            Value = $"{Config.Prefix}{text}{Config.Suffix}";
            Tooltip = $"{Config.Name}: {Value}\nUpdated {DateTime.Now:T}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception or UriFormatException or TimeoutException)
        {
            Value = "error";
            Tooltip = $"{Config.Name}: {ex.Message}";
        }
        finally
        {
            _busy = false;
        }
    }

    private static async Task<string> RunCommandAsync(string command)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/d /c {command}")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start command");
        var output = p.StandardOutput.ReadToEndAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException("Command timed out after 15 s");
        }
        return await output;
    }
}

public sealed class CustomWidgetService(SettingsService settings)
{
    private readonly Dictionary<string, CustomWidgetViewModel> _widgets = [];

    public void Start()
    {
        Sync();
        settings.Changed += Sync;
    }

    public CustomWidgetViewModel? Get(string id) => _widgets.GetValueOrDefault(id);

    private void Sync()
    {
        var configs = settings.Current.CustomWidgets;
        foreach (var id in _widgets.Keys.ToList())
        {
            var cfg = configs.FirstOrDefault(c => c.Id == id);
            // configs are edited in place; rebuild when anything material changed
            if (cfg is null || !ReferenceEquals(cfg, _widgets[id].Config) || Signature(cfg) != _signatures.GetValueOrDefault(id))
            {
                _widgets[id].Stop();
                _widgets.Remove(id);
            }
        }

        foreach (var cfg in configs)
        {
            if (_widgets.ContainsKey(cfg.Id)) continue;
            var vm = new CustomWidgetViewModel(cfg);
            _widgets[cfg.Id] = vm;
            _signatures[cfg.Id] = Signature(cfg);
            vm.Start();
        }
    }

    private readonly Dictionary<string, string> _signatures = [];

    private static string Signature(CustomWidgetConfig c) =>
        $"{c.Name}|{c.Kind}|{c.Source}|{c.JsonPath}|{c.IntervalSeconds}|{c.Prefix}|{c.Suffix}|{c.ClickTarget}";
}
