using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Facet.Infrastructure;
using Facet.Services;

namespace Facet.Popovers;

public partial class MediaPanel : UserControl
{
    private readonly MediaService _media = AppServices.Media;

    public MediaPanel()
    {
        InitializeComponent();
        DataContext = _media;
        UpdatePlayGlyph();
        _media.PropertyChanged += OnMediaChanged;
        Unloaded += (_, _) => _media.PropertyChanged -= OnMediaChanged;
    }

    private void OnMediaChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaService.IsPlaying)) UpdatePlayGlyph();
    }

    private void UpdatePlayGlyph() => PlayButton.Content = _media.IsPlaying ? "" : "";

    private void Prev_Click(object sender, RoutedEventArgs e) => _ = _media.PreviousAsync();
    private void Play_Click(object sender, RoutedEventArgs e) => _ = _media.PlayPauseAsync();
    private void Next_Click(object sender, RoutedEventArgs e) => _ = _media.NextAsync();
}
