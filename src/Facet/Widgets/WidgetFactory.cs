using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Facet.Controls;
using Facet.Infrastructure;
using Facet.Popovers;

namespace Facet.Widgets;

internal static class WidgetFactory
{
    public static readonly IReadOnlyDictionary<string, string> DisplayNames = new Dictionary<string, string>
    {
        ["upnext"] = "Up next (calendar)",
        ["media"] = "Now playing",
        ["weather"] = "Weather",
        ["system"] = "CPU & memory",
        ["pomodoro"] = "Pomodoro timer",
        ["todos"] = "Todos",
        ["connect"] = "Quick controls (Wi-Fi & battery, Bluetooth…)",
    };

    public static string DisplayName(string id)
    {
        if (DisplayNames.TryGetValue(id, out var name)) return name;
        if (id.StartsWith("custom:", StringComparison.Ordinal))
        {
            var cfg = AppServices.Settings.Current.CustomWidgets.FirstOrDefault(c => "custom:" + c.Id == id);
            return cfg is null ? id : $"Custom · {cfg.Name}";
        }
        return id;
    }

    public static FrameworkElement? Create(string id)
    {
        switch (id)
        {
            case "upnext": return new UpNextWidget();
            case "media": return new MediaWidget();
            case "pomodoro": return new PomodoroWidget();
            case "connect": return new ConnectWidget();
            case "weather":
                return Tile(AppServices.Weather, "WeatherTile", b => PopoverWindow.Toggle(b, () => new WeatherPanel()), "Description");
            case "system":
                return Tile(AppServices.System, "SystemTile", _ => AppServices.Open("taskmgr.exe"), "Summary", padding: new Thickness(5, 0, 5, 0));
            case "todos":
                return Tile(AppServices.Todos, "TodosTile", b => PopoverWindow.Toggle(b, () => new TodoPanel()), "Summary");
        }

        if (id.StartsWith("custom:", StringComparison.Ordinal) && AppServices.CustomWidgets.Get(id[7..]) is { } vm)
            return Tile(vm, "CustomTile", _ => vm.Click(), "Tooltip");

        return null;
    }

    private static Button Tile(object dataContext, string templateKey, Action<Button> click, string? tooltipPath = null,
        string? visibilityPath = null, Thickness? padding = null, bool plain = false)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.FindResource(plain ? "PlainTileButton" : "TileButton"),
            DataContext = dataContext,
            Content = dataContext,
            ContentTemplate = (DataTemplate)Application.Current.FindResource(templateKey),
        };
        if (padding is { } p) button.Padding = p;
        button.Click += (_, _) => click(button);
        if (tooltipPath is not null) button.SetBinding(FrameworkElement.ToolTipProperty, new Binding(tooltipPath));
        if (visibilityPath is not null)
            button.SetBinding(UIElement.VisibilityProperty, new Binding(visibilityPath) { Converter = new BoolToVisibilityConverter() });
        return button;
    }
}
