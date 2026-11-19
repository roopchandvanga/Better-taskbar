using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Facet.Controls;
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
        Btn.ToolTip = PreviewCard(n) ?? (object)NotificationBadge.Tooltip(n.CanCount, n.Unseen, n.Total, n.DoNotDisturb);

        var arrived = _lastUnseen >= 0 && n.Unseen > _lastUnseen && IsLoaded;
        _lastUnseen = n.Unseen;
        if (arrived) Announce();
    }

    /// <summary>
    /// Hovering the bell: the newest few notifications (app icon, app, how long ago, title, a line of text),
    /// new ones marked with an accent dot. Null when Windows doesn't let Facet read them, or there are none.
    /// </summary>
    private static FrameworkElement? PreviewCard(Services.NotificationService n)
    {
        if (!n.CanCount || n.Latest.Count == 0) return null;
        var now = DateTimeOffset.Now;
        var card = new StackPanel { Width = 290 };

        var head = n.Unseen switch
        {
            0 => "No new notifications",
            1 => "1 new notification",
            _ => $"{n.Unseen} new notifications",
        };
        if (n.DoNotDisturb) head += " · do not disturb is on";
        card.Children.Add(new TextBlock { Text = head, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 4) });

        foreach (var p in n.Latest)
        {
            var row = new Grid { Margin = new Thickness(0, 5, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var logo = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 1, 9, 0),
                VerticalAlignment = VerticalAlignment.Top };
            if (n.LogoFor(p.AppKey) is { } image)
                logo.Child = new Image { Source = image, Stretch = Stretch.Uniform };
            else
            {
                logo.SetResourceReference(Border.BackgroundProperty, "SurfaceCardStrong");
                logo.Child = new GlyphIcon { Glyph = BellGlyph, FontSize = 11 };
            }
            row.Children.Add(logo);

            var text = new StackPanel();
            Grid.SetColumn(text, 1);
            var meta = new DockPanel();
            var ago = Subtle(NotificationBadge.Ago(p.Arrived, now));
            DockPanel.SetDock(ago, Dock.Right);
            meta.Children.Add(ago);
            if (p.Arrived > n.LastOpened)
            {
                var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center };
                dot.SetResourceReference(Border.BackgroundProperty, "Accent");
                meta.Children.Add(dot);
            }
            meta.Children.Add(Subtle(p.App));
            text.Children.Add(meta);
            text.Children.Add(new TextBlock { Text = p.Title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) });
            if (!string.IsNullOrWhiteSpace(p.Body))
            {
                var body = Subtle(p.Body);
                body.TextWrapping = TextWrapping.Wrap;
                body.MaxHeight = 32; // two lines
                text.Children.Add(body);
            }
            row.Children.Add(text);
            card.Children.Add(row);
        }

        var foot = Subtle("Click for the notification center (Win+N)");
        foot.Margin = new Thickness(0, 6, 0, 1);
        card.Children.Add(foot);
        return card;
    }

    private static TextBlock Subtle(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 11, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetResourceReference(TextBlock.ForegroundProperty, "SurfaceSubtle");
        return t;
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
