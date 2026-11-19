using System.Collections.ObjectModel;
using Facet.Core.Settings;
using Facet.Core.Todos;
using Facet.Infrastructure;

namespace Facet.Services;

public sealed class TodoViewModel(TodoItem item, Action changed) : ObservableObject
{
    public TodoItem Item { get; } = item;
    public string Text => Item.Text;

    public bool Done
    {
        get => Item.Done;
        set
        {
            if (Item.Done == value) return;
            Item.Done = value;
            OnPropertyChanged();
            changed();
        }
    }
}

public sealed class TodoService : ObservableObject
{
    private readonly string _path = Path.Combine(SettingsService.DataDir, "todos.json");

    public ObservableCollection<TodoViewModel> Items { get; } = [];

    public int OpenCount => Items.Count(t => !t.Done);
    public string Summary => OpenCount == 0 ? "All done" : $"{OpenCount} open";

    public TodoService()
    {
        foreach (var t in JsonStore.Load<List<TodoItem>>(_path)) Items.Add(new TodoViewModel(t, Persist));
    }

    public void Add(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        Items.Insert(0, new TodoViewModel(new TodoItem { Text = text }, Persist));
        Persist();
    }

    public void Remove(TodoViewModel vm)
    {
        Items.Remove(vm);
        Persist();
    }

    public void ClearCompleted()
    {
        foreach (var done in Items.Where(t => t.Done).ToList()) Items.Remove(done);
        Persist();
    }

    private void Persist()
    {
        try { JsonStore.Save(_path, Items.Select(i => i.Item).ToList()); } catch (IOException) { }
        OnPropertyChanged(nameof(OpenCount));
        OnPropertyChanged(nameof(Summary));
    }
}
