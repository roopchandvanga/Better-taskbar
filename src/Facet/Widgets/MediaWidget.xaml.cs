using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Facet.Infrastructure;
using Facet.Popovers;
using Facet.Services;

namespace Facet.Widgets;

public partial class MediaWidget : UserControl
{
    private readonly MediaService _media = AppServices.Media;

    public MediaWidget()
    {
        InitializeComponent();
        DataContext = _media;
        UpdateGlyph();
        _media.PropertyChanged += OnChanged;
        Unloaded += (_, _) => _media.PropertyChanged -= OnChanged;
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaService.IsPlaying)) UpdateGlyph();
    }

    private void UpdateGlyph() => PlayPause.Content = _media.IsPlaying ? "" : "";

    private void PlayPause_Click(object sender, RoutedEventArgs e) => _ = _media.PlayPauseAsync();

    private void Btn_Click(object sender, RoutedEventArgs e) => PopoverWindow.Toggle(Btn, () => new MediaPanel());
}
