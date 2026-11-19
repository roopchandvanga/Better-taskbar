using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Facet.Infrastructure;
using Facet.Services;

namespace Facet.Popovers;

public partial class TodoPanel : UserControl
{
    public TodoPanel()
    {
        InitializeComponent();
        DataContext = AppServices.Todos;
        UpdateEmpty();
        AppServices.Todos.Items.CollectionChanged += OnItemsChanged;
        Unloaded += (_, _) => AppServices.Todos.Items.CollectionChanged -= OnItemsChanged;
        Loaded += (_, _) => Input.Focus();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmpty();

    private void UpdateEmpty() => Empty.Visibility = AppServices.Todos.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        AppServices.Todos.Add(Input.Text);
        Input.Clear();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TodoViewModel vm) AppServices.Todos.Remove(vm);
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => AppServices.Todos.ClearCompleted();
}
