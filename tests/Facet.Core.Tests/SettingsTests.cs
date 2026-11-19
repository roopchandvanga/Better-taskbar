using Facet.Core.Settings;

namespace Facet.Core.Tests;

public class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "facet-tests-" + Guid.NewGuid().ToString("N"));

    public SettingsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void DefaultsContainEveryBuiltInWidget()
    {
        var s = new FacetSettings();
        Assert.Equal(FacetSettings.BuiltInWidgetIds, s.Widgets.Select(w => w.Id));
        Assert.False(s.HideTaskbar);
        Assert.True(s.AutoHide);
        Assert.True(s.ShowNotifications);
        Assert.Equal(DockTheme.LiquidGlass, s.Theme);
    }

    [Fact]
    public void OlderSettingsFilesGetTheNotificationsButton()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{ "Theme": "Black", "Clock24": true, "ReserveSpace": false }""");
        var loaded = JsonStore.Load<FacetSettings>(path);
        Assert.Equal(DockTheme.Black, loaded.Theme);
        Assert.True(loaded.ShowNotifications);
        Assert.True(loaded.Animations);
        Assert.True(loaded.ReserveScreenSpace); // the old, off-by-default switch doesn't carry over
    }

    [Fact]
    public void DefaultDockIsQuiet()
    {
        // CPU/RAM and the Pomodoro timer are opt-in; the network + battery tile sits last, next to the clock
        var s = new FacetSettings();
        Assert.False(s.Widgets.Single(w => w.Id == "system").Enabled);
        Assert.False(s.Widgets.Single(w => w.Id == "pomodoro").Enabled);
        Assert.Equal("connect", s.Widgets.Last(w => w.Enabled).Id);
    }

    [Fact]
    public void NormalizeDropsRetiredTiles()
    {
        // volume/mic/DND and battery moved into the connect tile; old settings mustn't bring them back
        var s = new FacetSettings();
        s.Widgets.Insert(4, new WidgetSlot { Id = "toggles" });
        s.Widgets.Insert(5, new WidgetSlot { Id = "battery" });
        s.Normalize();
        Assert.DoesNotContain(s.Widgets, w => w.Id is "toggles" or "battery");
        Assert.Equal(FacetSettings.BuiltInWidgetIds, s.Widgets.Select(w => w.Id));
    }

    [Fact]
    public void NormalizeAddsMissingWidgetsWithTheirDefaultState()
    {
        var s = new FacetSettings();
        s.Widgets.RemoveAll(w => w.Id is "pomodoro" or "todos");
        s.Normalize();
        Assert.False(s.Widgets.Single(w => w.Id == "pomodoro").Enabled);
        Assert.True(s.Widgets.Single(w => w.Id == "todos").Enabled);
    }

    [Fact]
    public void NormalizeSlotsNewBuiltInsIntoTheirDefaultPlace()
    {
        // a list saved before "connect" existed gets it right after "todos", not after the opt-in tiles at the end
        var s = new FacetSettings();
        s.Widgets.RemoveAll(w => w.Id == "connect");
        s.Widgets.Add(new WidgetSlot { Id = "custom:abc" });
        s.CustomWidgets.Add(new CustomWidgetConfig { Id = "abc", Name = "x" });
        s.Normalize();

        var ids = s.Widgets.Select(w => w.Id).ToList();
        Assert.Equal(ids.IndexOf("todos") + 1, ids.IndexOf("connect"));
        Assert.Equal(ids.IndexOf("connect") + 1, ids.IndexOf("system"));
        Assert.True(s.Widgets.Single(w => w.Id == "connect").Enabled);
        Assert.Equal("custom:abc", ids[^1]);
    }

    [Fact]
    public void NormalizeAddsMissingFirstBuiltInAtTheFront()
    {
        var s = new FacetSettings();
        s.Widgets.RemoveAll(w => w.Id == "upnext");
        s.Normalize();
        Assert.Equal("upnext", s.Widgets[0].Id);
    }

    [Fact]
    public void ControlsTileDefaultsToNetworkBluetoothProjectAndExpand()
    {
        var s = new FacetSettings();
        Assert.Equal(FacetSettings.ConnectItemIds, s.ConnectItems.Select(i => i.Id));
        Assert.Equal(["network", "bluetooth", "project", "expand"], s.ConnectItems.Where(i => i.Enabled).Select(i => i.Id));
    }

    [Fact]
    public void NormalizeRepairsControlsTileItems()
    {
        // the user's order and choices stay; unknown and duplicate ids go; a missing item comes back, off,
        // after the item it follows by default
        var s = new FacetSettings
        {
            ConnectItems =
            [
                new WidgetSlot { Id = "expand" },
                new WidgetSlot { Id = "dnd" },
                new WidgetSlot { Id = "network" },
                new WidgetSlot { Id = "bogus" },
                new WidgetSlot { Id = "network", Enabled = false },
                new WidgetSlot { Id = "bluetooth", Enabled = false },
                new WidgetSlot { Id = "project" },
            ],
        };
        s.Normalize();

        var ids = s.ConnectItems.Select(i => i.Id).ToList();
        Assert.Equal(["expand", "dnd", "network", "bluetooth", "project"],
            ids.Where(id => id is "expand" or "dnd" or "network" or "bluetooth" or "project"));
        Assert.Equal(ids.IndexOf("dnd") + 1, ids.IndexOf("screenshot")); // follows dnd, as by default
        Assert.DoesNotContain("bogus", ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.True(s.ConnectItems.Single(i => i.Id == "network").Enabled);
        Assert.False(s.ConnectItems.Single(i => i.Id == "bluetooth").Enabled);
        Assert.Equal(ids.IndexOf("project") + 1, ids.IndexOf("volume"));
        Assert.False(s.ConnectItems.Single(i => i.Id == "volume").Enabled);
        Assert.Equal(FacetSettings.ConnectItemIds.Order(), ids.Order());
    }

    [Fact]
    public void NullControlsTileItemsGetTheDefaults()
    {
        var s = new FacetSettings { ConnectItems = null! };
        s.Normalize();
        Assert.Equal(FacetSettings.ConnectItemIds, s.ConnectItems.Select(i => i.Id));
    }

    [Fact]
    public void RoundTrips()
    {
        var path = Path.Combine(_dir, "settings.json");
        var s = new FacetSettings { Theme = DockTheme.Midnight, Edge = DockEdge.Top, Clock24 = true, ShowNotifications = false, WeatherCity = "Lisbon" };
        s.ICalUrls.Add("https://example.com/cal.ics");
        s.CustomWidgets.Add(new CustomWidgetConfig { Id = "abc", Name = "BTC", Kind = CustomWidgetKind.Url, Source = "https://x", JsonPath = "price" });
        s.ConnectItems.Single(i => i.Id == "dnd").Enabled = true;
        s.ConnectItems.Reverse();
        s.Normalize();
        JsonStore.Save(path, s);

        var loaded = JsonStore.Load<FacetSettings>(path);
        Assert.Equal(DockTheme.Midnight, loaded.Theme);
        Assert.Equal(DockEdge.Top, loaded.Edge);
        Assert.True(loaded.Clock24);
        Assert.False(loaded.ShowNotifications);
        Assert.Equal("Lisbon", loaded.WeatherCity);
        Assert.Equal("https://example.com/cal.ics", Assert.Single(loaded.ICalUrls));
        Assert.Equal("price", Assert.Single(loaded.CustomWidgets).JsonPath);
        Assert.Contains(loaded.Widgets, w => w.Id == "custom:abc");
        Assert.Equal(Enumerable.Reverse(FacetSettings.ConnectItemIds), loaded.ConnectItems.Select(i => i.Id));
        Assert.True(loaded.ConnectItems.Single(i => i.Id == "dnd").Enabled);
        Assert.Contains("\"Midnight\"", File.ReadAllText(path)); // enums as strings
    }

    [Fact]
    public void MissingFileGivesDefaults() =>
        Assert.Equal(DockTheme.LiquidGlass, JsonStore.Load<FacetSettings>(Path.Combine(_dir, "nope.json")).Theme);

    [Fact]
    public void CorruptFileGivesDefaultsAndKeepsBackup()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ this is not json");
        var s = JsonStore.Load<FacetSettings>(path);
        Assert.Equal(DockTheme.LiquidGlass, s.Theme);
        Assert.True(File.Exists(path + ".corrupt"));
    }

    [Fact]
    public void NormalizeRepairsWidgetList()
    {
        var s = new FacetSettings();
        s.Widgets.RemoveAll(w => w.Id == "media");
        s.Widgets.Add(new WidgetSlot { Id = "bogus" });
        s.Widgets.Add(new WidgetSlot { Id = "custom:gone" });
        s.CustomWidgets.Add(new CustomWidgetConfig { Id = "keep", Name = "x" });
        s.Widgets.Add(new WidgetSlot { Id = "system" }); // duplicate
        s.Normalize();

        Assert.Contains(s.Widgets, w => w.Id == "media");
        Assert.DoesNotContain(s.Widgets, w => w.Id == "bogus");
        Assert.DoesNotContain(s.Widgets, w => w.Id == "custom:gone");
        Assert.Contains(s.Widgets, w => w.Id == "custom:keep");
        Assert.Single(s.Widgets, w => w.Id == "system");
    }

    [Fact]
    public void QuietHoursParse()
    {
        var s = new FacetSettings { QuietStart = "23:30", QuietEnd = "bad" };
        Assert.Equal(new TimeSpan(23, 30, 0), s.QuietStartTime);
        Assert.Equal(new TimeSpan(7, 0, 0), s.QuietEndTime); // falls back to default
    }
}
