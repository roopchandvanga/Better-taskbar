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

    /// <summary>A soft spring: overshoots by about 8 % and settles.</summary>
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

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    /// <summary>Damped spring, normalised so it ends exactly at 1.</summary>
    private sealed class SpringEase : EasingFunctionBase
    {
        private const double Damping = 8, Frequency = 10;
        private static readonly double End = 1 - Math.Exp(-Damping) * Math.Cos(Frequency);

        // EaseIn: Ease(t) is EaseInCore(t) itself (the base class defaults to EaseOut, which would mirror it)
        public SpringEase() => EasingMode = EasingMode.EaseIn;

        protected override double EaseInCore(double t) =>
            (1 - Math.Exp(-Damping * t) * Math.Cos(Frequency * t)) / End;

        protected override Freezable CreateInstanceCore() => new SpringEase();
    }
}
