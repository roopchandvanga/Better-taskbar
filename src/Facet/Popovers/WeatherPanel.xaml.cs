using System.Windows;
using System.Windows.Controls;
using Facet.Infrastructure;
using Facet.SettingsUi;

namespace Facet.Popovers;

public partial class WeatherPanel : UserControl
{
    public WeatherPanel()
    {
        InitializeComponent();
        DataContext = AppServices.Weather;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = AppServices.Weather.RefreshAsync(force: true);

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        PopoverWindow.CloseCurrent();
        SettingsWindow.Open("weather");
    }
}
