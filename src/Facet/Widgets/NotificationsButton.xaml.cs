using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Facet.Theme;
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

    private int _lastUnseen = -1; // unknown until the first update: no wiggle at start-up

    private void Update()
    {
        var n = AppServices.Notifications;
        Bell.Glyph = n.DoNotDisturb ? QuietBellGlyph : BellGlyph;
        var badge = NotificationBadge.Text(n.Unseen);
        BadgeText.Text = badge;
        Badge.Visibility = badge.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Btn.ToolTip = NotificationBadge.Tooltip(n.CanCount, n.Unseen, n.Total, n.DoNotDisturb);

        var arrived = _lastUnseen >= 0 && n.Unseen > _lastUnseen && IsLoaded;
        _lastUnseen = n.Unseen;
        if (arrived) Announce();
    }

    /// <summary>A new notification: the badge pops in and the bell rings (a few decaying swings from its top).</summary>
    private void Announce()
    {
        if (!Motion.Enabled) return;
        Motion.To(BadgeScale, ScaleTransform.ScaleXProperty, 1, Motion.Slow, Motion.Spring, from: 0.3);
        Motion.To(BadgeScale, ScaleTransform.ScaleYProperty, 1, Motion.Slow, Motion.Spring, from: 0.3);

        var ring = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(760) };
        double[] swings = [16, -13, 9, -6, 3, 0];
        for (var i = 0; i < swings.Length; i++)
            ring.KeyFrames.Add(new EasingDoubleKeyFrame(swings[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120 * (i + 1) + 40)),
                new SineEase { EasingMode = EasingMode.EaseInOut }));
        BellSwing.BeginAnimation(RotateTransform.AngleProperty, ring);
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
