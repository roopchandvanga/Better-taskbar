using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Facet.Apps;
using Facet.Infrastructure;
using Facet.Theme;

namespace Facet.Shell;

/// <summary>
/// Motion for the dock's left side (Start button + app icons):
/// <list type="bullet">
/// <item>one active-app line that glides (and briefly stretches) from app to app instead of jumping;</item>
/// <item>macOS-style magnification: icons near the cursor grow with a smooth falloff and follow it.</item>
/// </list>
/// Icons only scale (render transforms), so nothing re-lays out; the frame loop runs only while icons are
/// still moving and stops when they settle, so an idle dock costs nothing.
/// </summary>
internal sealed class AppStripAnimator
{
    private const double MaxScale = 1.38;       // fits inside the dock's own padding (icons grow mostly upward)
    private const double HoverScale = 1.14;     // single-icon hover when magnification or motion is off
    private const double RangeItems = 2.3;      // falloff radius, in icon slots
    private const double SettleMs = 55;         // how quickly scales chase their targets

    private readonly FrameworkElement _area;
    private readonly FrameworkElement _startButton;
    private readonly FrameworkElement _startLogo;
    private readonly ItemsControl _apps;
    private readonly FrameworkElement _layer;
    private readonly Border _bar;
    private readonly TranslateTransform _barMove = new();
    private readonly ScaleTransform _barScale = new(1, 1);

    private readonly Dictionary<ScaleTransform, double> _targets = [];
    private bool _hovering;
    private bool _ticking;
    private TimeSpan _lastFrame;

    private AppItem? _barFor;
    private Point _barAt;

    public AppStripAnimator(FrameworkElement area, FrameworkElement startButton, FrameworkElement startLogo, ItemsControl apps,
        FrameworkElement layer, Border bar)
    {
        _area = area;
        _startButton = startButton;
        _startLogo = startLogo;
        _apps = apps;
        _layer = layer;
        _bar = bar;

        _startLogo.RenderTransformOrigin = new Point(0.5, 0.8);
        _startLogo.RenderTransform = new ScaleTransform(1, 1);
        _bar.RenderTransformOrigin = new Point(0.5, 0.5);
        _bar.RenderTransform = new TransformGroup { Children = { _barScale, _barMove } };
        _bar.Opacity = 0;

        _area.MouseMove += (_, e) => Magnify(e.GetPosition(_area).X);
        _area.MouseLeave += (_, _) => Relax();

        // the active app changes on a property of an item; positions change with any layout pass
        if (_apps.ItemsSource is INotifyCollectionChanged items)
        {
            items.CollectionChanged += OnItemsChanged;
            foreach (var item in _apps.Items.OfType<AppItem>()) item.PropertyChanged += OnItemChanged;
        }
        _apps.LayoutUpdated += (_, _) => PlaceBar(animate: true);
    }

    // ───────────────────────── magnification ─────────────────────────

    private void Magnify(double x)
    {
        _hovering = true;
        var slot = _apps.Items.Count > 0 && Container(_apps.Items[0]) is { } first ? first.ActualWidth : 54;
        var range = Math.Max(1, slot * RangeItems);
        var full = Motion.Enabled && (AppServices.Settings?.Current.Magnify ?? true);

        foreach (var (element, scale) in Scalables())
        {
            if (!_area.IsAncestorOf(element)) continue; // a container that's being recycled
            var left = element.TransformToAncestor(_area).Transform(new Point()).X;
            var centre = left + element.ActualWidth / 2;
            double target;
            if (full)
            {
                var t = Math.Min(1, Math.Abs(x - centre) / range);
                target = 1 + (MaxScale - 1) * (Math.Cos(Math.PI * t) + 1) / 2; // smooth bell, flat at the edges
            }
            else
            {
                target = x >= left && x < left + element.ActualWidth ? HoverScale : 1;
            }
            _targets[scale] = target;
        }
        Run();
    }

    private void Relax()
    {
        _hovering = false;
        foreach (var scale in _targets.Keys.ToList()) _targets[scale] = 1;
        Run();
    }

    /// <summary>The Start logo and every app icon, with the transform that scales it.</summary>
    private IEnumerable<(FrameworkElement Element, ScaleTransform Scale)> Scalables()
    {
        if (_startLogo.RenderTransform is ScaleTransform start)
            yield return (_startButton, start);
        foreach (var item in _apps.Items)
        {
            if (Container(item) is not { } container || IconScale(container) is not { } scale) continue;
            yield return (container, scale);
        }
    }

    private void Run()
    {
        if (!Motion.Enabled)
        {
            // no motion: jump straight to the targets
            foreach (var (scale, target) in _targets) Apply(scale, target);
            Prune();
            return;
        }
        if (_ticking) return;
        _ticking = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame) return; // Rendering can fire more than once per frame
        var dt = _lastFrame == TimeSpan.Zero ? 16 : (now - _lastFrame).TotalMilliseconds;
        _lastFrame = now;
        var k = 1 - Math.Exp(-Math.Min(dt, 50) / SettleMs);

        var moving = false;
        foreach (var (scale, target) in _targets)
        {
            var current = scale.ScaleX;
            var next = current + (target - current) * k;
            if (Math.Abs(target - next) < 0.002) next = target;
            else moving = true;
            Apply(scale, next);
        }

        if (moving || _hovering) return;
        CompositionTarget.Rendering -= OnFrame;
        _ticking = false;
        Prune();
    }

    private static void Apply(ScaleTransform scale, double value)
    {
        scale.ScaleX = value;
        scale.ScaleY = value;
    }

    /// <summary>Forget icons that are back at rest (and ones whose app left the dock).</summary>
    private void Prune()
    {
        foreach (var (scale, target) in _targets.ToList())
            if (target == 1 && scale.ScaleX == 1) _targets.Remove(scale);
    }

    // ───────────────────────── active-app line ─────────────────────────

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (var item in e.OldItems.OfType<AppItem>()) item.PropertyChanged -= OnItemChanged;
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<AppItem>())
            {
                item.PropertyChanged += OnItemChanged;
                // a newly added app (Move is just reordering): its container exists already, not yet drawn
                if (e.Action == NotifyCollectionChangedAction.Add) Arrive(item);
            }
        }
        if (e.Action == NotifyCollectionChangedAction.Reset)
            foreach (var item in _apps.Items.OfType<AppItem>()) { item.PropertyChanged -= OnItemChanged; item.PropertyChanged += OnItemChanged; }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not AppItem item) return;
        switch (e.PropertyName)
        {
            case nameof(AppItem.IsActive):
                _apps.Dispatcher.BeginInvoke(() => PlaceBar(animate: true), System.Windows.Threading.DispatcherPriority.Loaded);
                break;
            case nameof(AppItem.IsLeaving):
                if (Container(item) is { } container) Presence(container, item.IsLeaving ? 0 : 1);
                break;
            case nameof(AppItem.IsLaunching):
                Bounce(item);
                break;
        }
    }

    // ───────────────────────── apps arriving, leaving, launching ─────────────────────────

    private const double ArriveMs = 260;
    private const double LeaveMs = 220;
    private const double BounceHeight = 8;

    /// <summary>A new app opens a gap that widens to full size while its icon fades in.</summary>
    private void Arrive(AppItem item)
    {
        if (!Motion.Enabled) return;
        if (Container(item) is { } container)
        {
            Presence(container, 1, from: 0);
            return;
        }
        // not generated yet (shouldn't happen for this non-virtualised list): do it once it is
        _apps.Dispatcher.BeginInvoke(() =>
        {
            if (Container(item) is { } later) Presence(later, 1, from: 0);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// How much of an app's slot is there: 0 = gone, 1 = fully there. The slot narrows through negative
    /// margins (so the neighbours slide over) while the icon shrinks evenly and fades — never squashed.
    /// </summary>
    private static readonly DependencyProperty PresenceProperty = DependencyProperty.RegisterAttached("Presence",
        typeof(double), typeof(AppStripAnimator), new PropertyMetadata(1.0, OnPresenceChanged));

    private static void Presence(FrameworkElement container, double to, double? from = null)
    {
        if (from is { } f) Motion.Set(container, PresenceProperty, f);
        var leaving = to < 1;
        Motion.To(container, PresenceProperty, to, leaving ? LeaveMs : ArriveMs, leaving ? Motion.EaseIn : Motion.EaseOut);
    }

    private static void OnPresenceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement container) return;
        var p = Math.Clamp((double)e.NewValue, 0, 1);
        var item = container.TryFindResource("ItemSize") is double size ? size : 48;
        var spacing = container.TryFindResource("AppSpacing") is Thickness t ? t.Left + t.Right : 6;
        var collapse = -(1 - p) * (item + spacing) / 2;
        container.Margin = p >= 1 ? new Thickness() : new Thickness(collapse, 0, collapse, 0);
        container.Opacity = Math.Clamp(p * 1.25 - 0.1, 0, 1); // fades a little ahead of the size
        if (FindDescendant<Button>(container) is not { } button) return;
        if (button.RenderTransform is not ScaleTransform grow || grow.IsFrozen)
        {
            grow = new ScaleTransform(1, 1);
            button.RenderTransform = grow;
            button.RenderTransformOrigin = new Point(0.5, 0.8);
        }
        grow.ScaleX = grow.ScaleY = 0.4 + 0.6 * p;
    }

    /// <summary>
    /// macOS-style launch bounce until the app's window shows up. With motion off, the icon dims instead.
    /// The only looping animation in the dock, and it stops by itself (AppTracker gives up after 10 s).
    /// </summary>
    private void Bounce(AppItem item)
    {
        if (Container(item) is not { } container || TemplatePart<TranslateTransform>(container, "Lift") is not { } lift
            || TemplatePart<Image>(container, "Icon") is not { } icon) return;

        if (!item.IsLaunching)
        {
            Motion.To(lift, TranslateTransform.YProperty, 0, Motion.Fast, Motion.EaseOut);
            Motion.Set(icon, UIElement.OpacityProperty, 1);
            return;
        }
        if (!Motion.Enabled)
        {
            Motion.Set(icon, UIElement.OpacityProperty, 0.55);
            return;
        }
        var hop = new System.Windows.Media.Animation.DoubleAnimation(0, -BounceHeight, TimeSpan.FromMilliseconds(300))
        {
            AutoReverse = true,
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
        };
        lift.BeginAnimation(TranslateTransform.YProperty, hop);
    }

    /// <summary>Put the line under the active app: glide there from the last one, or fade in if there was none.</summary>
    private void PlaceBar(bool animate)
    {
        var active = _apps.Items.OfType<AppItem>().FirstOrDefault(i => i.IsActive);
        if (active is null || Container(active) is not { } container || Root(container) is not { } root
            || root.ActualWidth <= 0 || PresentationSource.FromVisual(_layer) is null || !_area.IsAncestorOf(root))
        {
            if (_barFor is not null)
            {
                _barFor = null;
                Motion.To(_bar, UIElement.OpacityProperty, 0, Motion.Fast);
            }
            return;
        }

        var gap = _bar.TryFindResource("DotGap") is Thickness dotGap ? dotGap.Bottom : 1;
        // the layer is a sibling of the icon row, not an ancestor: transform through their common parent
        var bottomCentre = root.TransformToVisual(_layer).Transform(new Point(root.ActualWidth / 2, root.ActualHeight));
        var at = new Point(Math.Round(bottomCentre.X - _bar.Width / 2), Math.Round(bottomCentre.Y - _bar.Height - gap));
        if (ReferenceEquals(active, _barFor) && at == _barAt) return;

        var appearing = _barFor is null || !animate;
        var moved = Math.Abs(at.X - _barAt.X) > 0.5;
        _barFor = active;
        _barAt = at;
        Motion.Set(_barMove, TranslateTransform.YProperty, at.Y);

        if (appearing)
        {
            Motion.Set(_barMove, TranslateTransform.XProperty, at.X);
            Motion.To(_bar, UIElement.OpacityProperty, 1, Motion.Fast);
            Motion.To(_barScale, ScaleTransform.ScaleXProperty, 1, Motion.Slow, Motion.Spring, from: 0.3);
            return;
        }
        if (!moved) return;

        // glide over, stretching a little on the way like a drop of liquid, then settle
        Motion.To(_bar, UIElement.OpacityProperty, 1, Motion.Fast);
        Motion.To(_barMove, TranslateTransform.XProperty, at.X, Motion.Slow, Motion.Spring);
        Motion.To(_barScale, ScaleTransform.ScaleXProperty, 1.7, 110, Motion.EaseOut,
            done: () => Motion.To(_barScale, ScaleTransform.ScaleXProperty, 1, 260, Motion.Spring));
    }

    // ───────────────────────── helpers ─────────────────────────

    private FrameworkElement? Container(object item) => _apps.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;

    /// <summary>The app button's template root (the icon slot the dot sits at the bottom of).</summary>
    private static FrameworkElement? Root(FrameworkElement container) =>
        FindDescendant<Button>(container) is { } button && VisualTreeHelper.GetChildrenCount(button) > 0
            ? VisualTreeHelper.GetChild(button, 0) as FrameworkElement
            : null;

    private static ScaleTransform? IconScale(FrameworkElement container) => TemplatePart<ScaleTransform>(container, "Scale");

    private static T? TemplatePart<T>(FrameworkElement container, string name) where T : class =>
        FindDescendant<Button>(container) is { } button && button.Template?.FindName(name, button) is T part ? part : null;

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

}
