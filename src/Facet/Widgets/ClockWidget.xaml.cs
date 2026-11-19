using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Facet.Infrastructure;
using Facet.Popovers;

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
        Time.Text = AppServices.Settings.Current.Clock24 ? now.ToString("HH:mm", culture) : now.ToString("h:mm tt", culture);
        Date.Text = now.ToString("ddd, MMM d", culture);
        Btn.ToolTip = now.ToString("D", culture);
    }

    private void Btn_Click(object sender, RoutedEventArgs e) => PopoverWindow.Toggle(Btn, () => new CalendarPanel());
}
