using System.Windows;
using System.Windows.Media.Animation;
using Facet.Infrastructure;

namespace Facet.Theme;

/// <summary>
/// Shared timing and easing for Facet's animations, and the one switch for all of them: motion is off when
/// Windows' "Animation effects" is off (Settings › Accessibility › Visual effects) or Facet's own Animations
/// setting is. Movement goes through here, so with motion off every change is simply instant.
/// Keep animations event-driven and short: the dock is a transparent (layered) window, so every animated
/// frame is copied back from the GPU, and anything that loops forever costs CPU while idle.
/// </summary>
internal static class Motion
{
    public const double Fast = 140;
    public const double Normal = 220;
    public const double Slow = 360;

    public static bool Enabled =>
        SystemParameters.ClientAreaAnimation && (AppServices.Settings?.Current.Animations ?? true);

    public static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
    public static readonly IEasingFunction EaseIn = Frozen(new CubicEase { EasingMode = EasingMode.EaseIn });
    public static readonly IEasingFunction EaseInOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseInOut });

    /// <summary>A soft spring: starts from rest, overshoots by about 5 % and settles.</summary>
    public static readonly IEasingFunction Spring = Frozen(new SpringEase());

    /// <summary>
    /// Animate <paramref name="dp"/> of <paramref name="target"/> to <paramref name="to"/> (holding the end value);
    /// with motion off, set it at once. Use this for every change of a property it animates, so a held value
    /// never hides a later one.
    /// </summary>
    public static void To(IAnimatable target, DependencyProperty dp, double to, double ms, IEasingFunction? ease = null,
        Action? done = null, double? from = null)
    {
        if (!Enabled || ms <= 0)
        {
            target.BeginAnimation(dp, null);
            ((DependencyObject)target).SetValue(dp, to);
            done?.Invoke();
            return;
        }
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease ?? EaseOut };
        if (from is { } f) animation.From = f;
        if (done is not null) animation.Completed += (_, _) => done();
        target.BeginAnimation(dp, animation);
    }

    /// <summary>Set <paramref name="dp"/> at once, dropping any animation holding it.</summary>
    public static void Set(IAnimatable target, DependencyProperty dp, double value)
    {
        target.BeginAnimation(dp, null);
        ((DependencyObject)target).SetValue(dp, value);
    }

    // ───────────────────────── attached behaviours (usable from templates) ─────────────────────────

    /// <summary>
    /// On a bound TextBlock (binding with NotifyOnTargetUpdated=True): a new value fades in and settles from
    /// slightly below, instead of just replacing the old one. The first value, set while loading, doesn't.
    /// </summary>
    public static readonly DependencyProperty FadeOnUpdateProperty = DependencyProperty.RegisterAttached("FadeOnUpdate",
        typeof(bool), typeof(Motion), new PropertyMetadata(false, OnFadeOnUpdateChanged));

    public static bool GetFadeOnUpdate(DependencyObject d) => (bool)d.GetValue(FadeOnUpdateProperty);
    public static void SetFadeOnUpdate(DependencyObject d, bool value) => d.SetValue(FadeOnUpdateProperty, value);

    private static void OnFadeOnUpdateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true) return;
        element.AddHandler(System.Windows.Data.Binding.TargetUpdatedEvent, new EventHandler<System.Windows.Data.DataTransferEventArgs>((_, args) =>
        {
            if (!element.IsLoaded || args.Property != System.Windows.Controls.TextBlock.TextProperty) return;
            if (element.RenderTransform is not System.Windows.Media.TranslateTransform shift || shift.IsFrozen)
                element.RenderTransform = shift = new System.Windows.Media.TranslateTransform();
            To(element, UIElement.OpacityProperty, 1, Slow, EaseOut, from: 0.15);
            To(shift, System.Windows.Media.TranslateTransform.YProperty, 0, Slow, Spring, from: 4);
        }));
    }

    /// <summary>
    /// On a ControlTile check box: switching it on fills its round icon with the accent from the centre outward;
    /// switching it off drains it back. Needs a template ScaleTransform named "FillScale" on the accent disc.
    /// </summary>
    public static readonly DependencyProperty FillOnCheckProperty = DependencyProperty.RegisterAttached("FillOnCheck",
        typeof(bool), typeof(Motion), new PropertyMetadata(false, OnFillOnCheckChanged));

    public static bool GetFillOnCheck(DependencyObject d) => (bool)d.GetValue(FillOnCheckProperty);
    public static void SetFillOnCheck(DependencyObject d, bool value) => d.SetValue(FillOnCheckProperty, value);

    private static void OnFillOnCheckChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Controls.Primitives.ToggleButton tile || e.NewValue is not true) return;
        System.Windows.Media.ScaleTransform? Fill() =>
            tile.Template?.FindName("FillScale", tile) as System.Windows.Media.ScaleTransform;
        void Show(bool animate)
        {
            if (Fill() is not { } fill) return;
            var on = tile.IsChecked == true ? 1.0 : 0.0;
            if (!animate)
            {
                Set(fill, System.Windows.Media.ScaleTransform.ScaleXProperty, on);
                Set(fill, System.Windows.Media.ScaleTransform.ScaleYProperty, on);
                return;
            }
            var ease = on > 0 ? Spring : EaseIn;
            var ms = on > 0 ? Slow : Fast;
            To(fill, System.Windows.Media.ScaleTransform.ScaleXProperty, on, ms, ease);
            To(fill, System.Windows.Media.ScaleTransform.ScaleYProperty, on, ms, ease);
        }
        tile.Loaded += (_, _) => Show(animate: false);
        tile.Checked += (_, _) => Show(animate: tile.IsLoaded);
        tile.Unchecked += (_, _) => Show(animate: tile.IsLoaded);
    }

    /// <summary>Each time the element is loaded (e.g. a tooltip opening): fade in and grow from 96 %.</summary>
    public static readonly DependencyProperty AppearOnLoadProperty = DependencyProperty.RegisterAttached("AppearOnLoad",
        typeof(bool), typeof(Motion), new PropertyMetadata(false, OnAppearOnLoadChanged));

    public static bool GetAppearOnLoad(DependencyObject d) => (bool)d.GetValue(AppearOnLoadProperty);
    public static void SetAppearOnLoad(DependencyObject d, bool value) => d.SetValue(AppearOnLoadProperty, value);

    private static void OnAppearOnLoadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true) return;
        element.Loaded += (_, _) =>
        {
            if (!Enabled) return;
            if (element.RenderTransform is not System.Windows.Media.ScaleTransform grow || grow.IsFrozen)
            {
                element.RenderTransform = grow = new System.Windows.Media.ScaleTransform(1, 1);
                element.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            To(element, UIElement.OpacityProperty, 1, Fast, EaseOut, from: 0);
            To(grow, System.Windows.Media.ScaleTransform.ScaleXProperty, 1, Normal, EaseOut, from: 0.96);
            To(grow, System.Windows.Media.ScaleTransform.ScaleYProperty, 1, Normal, EaseOut, from: 0.96);
        };
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    /// <summary>
    /// Underdamped spring released from rest (zero starting speed): damping ratio 0.7, settling by the end of
    /// the animation; normalised so it ends exactly at 1.
    /// </summary>
    private sealed class SpringEase : EasingFunctionBase
    {
        private const double Zeta = 0.7, Omega = 7.2;
        private static readonly double Decay = Zeta * Omega;
        private static readonly double Damped = Omega * Math.Sqrt(1 - Zeta * Zeta);
        private static readonly double End = Raw(1);

        // EaseIn: Ease(t) is EaseInCore(t) itself (the base class defaults to EaseOut, which would mirror it)
        public SpringEase() => EasingMode = EasingMode.EaseIn;

        private static double Raw(double t) =>
            1 - Math.Exp(-Decay * t) * (Math.Cos(Damped * t) + Decay / Damped * Math.Sin(Damped * t));

        protected override double EaseInCore(double t) => Raw(t) / End;

        protected override Freezable CreateInstanceCore() => new SpringEase();
    }
}
