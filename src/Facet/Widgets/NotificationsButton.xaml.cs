using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Facet.Core.Notifications;
using Facet.Infrastructure;
using Facet.Shell;

namespace Facet.Widgets;

public partial class NotificationsButton : UserControl
{
    // Segoe Fluent Icons: bell, and bell struck through
    private const string BellGlyph = "";
    private const string QuietBellGlyph = "";

    public NotificationsButton()
    {
        InitializeComponent();
        Update();
        Loaded += (_, _) =>
        {
            AppServices.Notifications.PropertyChanged += OnChanged;
            Update();
        };
        Unloaded += (_, _) => AppServices.Notifications.PropertyChanged -= OnChanged;
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void Update()
    {
        var n = AppServices.Notifications;
        Bell.Glyph = n.DoNotDisturb ? QuietBellGlyph : BellGlyph;
        var badge = NotificationBadge.Text(n.Unseen);
        BadgeText.Text = badge;
        Badge.Visibility = badge.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Btn.ToolTip = NotificationBadge.Tooltip(n.CanCount, n.Unseen, n.Total, n.DoNotDisturb);
    }

    private void Btn_Click(object sender, RoutedEventArgs e) => AppServices.Notifications.OpenCenter();

    private void Btn_RightClick(object sender, MouseButtonEventArgs e)
    {
        var n = AppServices.Notifications;
        var menu = new ContextMenu { PlacementTarget = Btn };
        Add(menu, "Notification center   Win+N", n.OpenCenter);
        Add(menu, "Notifications & do not disturb settings…", n.OpenSettings);
        if (!n.CanCount)
            Add(menu, "Show a count on the bell…", n.OpenAccessSettings);
        menu.Items.Add(new Separator());
        Add(menu, "Hide this button", () =>
        {
            AppServices.Settings.Current.ShowNotifications = false;
            AppServices.Settings.Save();
        });

        MenuGuard.Watch(menu);
        menu.IsOpen = true;
        e.Handled = true; // not the dock's own menu
    }

    private static void Add(ContextMenu menu, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }
}
