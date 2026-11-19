namespace Facet.Core.Calendar;

/// <summary>A concrete (already expanded) calendar occurrence in local time.</summary>
public sealed record CalendarEvent(
    string Id,
    string Title,
    DateTime Start,
    DateTime End,
    bool AllDay,
    string? Location = null,
    string Source = "local",
    string? Color = null);
