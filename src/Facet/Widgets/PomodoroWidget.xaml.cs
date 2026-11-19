using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Facet.Infrastructure;
using Facet.Services;
using Facet.SettingsUi;
using Facet.Theme;

namespace Facet.Widgets;

public partial class PomodoroWidget : UserControl
{
    private readonly PomodoroService _pomodoro = AppServices.Pomodoro;

    public PomodoroWidget()
    {
        InitializeComponent();
        DataContext = _pomodoro;
        Update();
        _pomodoro.PropertyChanged += OnChanged;
        Unloaded += (_, _) => _pomodoro.PropertyChanged -= OnChanged;
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PomodoroService.Phase) or nameof(PomodoroService.Running)) Update();
    }

    private void Update()
    {
        Glyph.Text = _pomodoro.Running ? "" : "";
        ProgressRing.Fill = ThemeManager.Brush(_pomodoro.Phase == PomodoroPhase.Break ? "#30D158" : "#FF6B5E");
    }

    private void Btn_Click(object sender, RoutedEventArgs e) => _pomodoro.Toggle();

    private void Btn_RightClick(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = Btn };
        Add(menu, _pomodoro.Running ? "Pause" : "Start", _pomodoro.Toggle);
        Add(menu, _pomodoro.Phase == PomodoroPhase.Focus ? "Skip to break" : "Skip", _pomodoro.Skip, _pomodoro.Phase != PomodoroPhase.Idle);
        Add(menu, "Reset", _pomodoro.Reset, _pomodoro.Phase != PomodoroPhase.Idle);
        menu.Items.Add(new Separator());
        Add(menu, "Focus settings…", () => SettingsWindow.Open("alerts"));
        Facet.Shell.MenuGuard.Watch(menu);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static void Add(ContextMenu menu, string header, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }
}
