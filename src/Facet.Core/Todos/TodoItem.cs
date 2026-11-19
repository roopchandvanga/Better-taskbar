namespace Facet.Core.Todos;

public sealed class TodoItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
