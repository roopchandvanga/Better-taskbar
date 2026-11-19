using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Facet.Infrastructure;
using Facet.Popovers;
using Facet.Theme;

namespace Facet.Widgets;

public partial class ClockWidget : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public ClockWidget()
    {
        InitializeComponent();
        Update();
        _timer.Tick += (_, _) => Update();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    public FrameworkElement Anchor => Btn;

    private void Update()
    {
        var now = DateTime.Now;
        var culture = CultureInfo.CurrentCulture;
        var time = AppServices.Settings.Current.Clock24 ? now.ToString("HH:mm", culture) : now.ToString("h:mm tt", culture);
        if (time != Time.Text) Roll(time);
        Date.Text = now.ToString("ddd, MMM d", culture);
        Btn.ToolTip = now.ToString("D", culture);
    }

    private const double RollDistance = 7;

    /// <summary>New minute: the old time slides up and fades while the new one slides up into place.</summary>
    private void Roll(string time)
    {
        var old = Time.Text;
        Time.Text = time;
        if (!IsLoaded || string.IsNullOrEmpty(old) || !Motion.Enabled) return;

        TimeOld.Text = old;
        Motion.To(TimeOld, OpacityProperty, 0, Motion.Normal, Motion.EaseIn, from: 1);
        Motion.To(TimeOldShift, TranslateTransform.YProperty, -RollDistance, Motion.Normal, Motion.EaseIn, from: 0);
        Motion.To(Time, OpacityProperty, 1, Motion.Slow, Motion.EaseOut, from: 0);
        Motion.To(TimeShift, TranslateTransform.YProperty, 0, Motion.Slow, Motion.Spring, from: RollDistance);
    }

    private void Btn_Click(object sender, RoutedEventArgs e) => PopoverWindow.Toggle(Btn, () => new CalendarPanel());
}
