using System.Windows.Controls;
using Facet.Infrastructure;

namespace Facet.Widgets;

/// <summary>
/// The buttons the quick controls tile can show (<see cref="Core.Settings.FacetSettings.ConnectItemIds"/>):
/// their names and what they do, for the settings page and the menus that turn them on and off.
/// </summary>
internal static class ConnectItems
{
    private static readonly Dictionary<string, (string Name, string About)> Info = new()
    {
        ["network"] = ("Wi-Fi & battery", "Network icon inside the battery ring · click for Wi-Fi networks"),
        ["battery"] = ("Battery", "A ring of its own · the Wi-Fi ring then shows signal strength instead"),
        ["bluetooth"] = ("Bluetooth", "Click for Bluetooth devices"),
        ["project"] = ("Project", "Duplicate or extend to another screen, same as Win+P"),
        ["volume"] = ("Volume", "Click to mute, scroll over it to change the volume"),
        ["mic"] = ("Microphone", "Click to mute or unmute the microphone"),
        ["dnd"] = ("Do not disturb", "Click to hold Facet alerts until you turn it off"),
        ["screenshot"] = ("Screenshot", "Snip part of the screen, same as Win+Shift+S"),
        ["expand"] = ("Expand", "Opens volume, microphone, do not disturb and the other quick controls"),
    };

    public static string Name(string id) => Info.TryGetValue(id, out var i) ? i.Name : id;

    public static string About(string id) => Info.TryGetValue(id, out var i) ? i.About : "";

    /// <summary>
    /// One check item per button. The menu stays open on click so several can be switched in one go;
    /// the tile rearranges as each one is saved.
    /// </summary>
    public static void AddChoices(ItemsControl menu)
    {
        foreach (var slot in AppServices.Settings.Current.ConnectItems)
        {
            var captured = slot;
            var item = new MenuItem { Header = Name(slot.Id), IsCheckable = true, IsChecked = slot.Enabled, StaysOpenOnClick = true };
            item.Click += (_, _) =>
            {
                captured.Enabled = item.IsChecked;
                AppServices.Settings.Save();
            };
            menu.Items.Add(item);
        }
    }
}
