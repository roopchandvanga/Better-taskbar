using System.Text.Json;

namespace Facet.Core.Widgets;

/// <summary>
/// Minimal JSON path: "$.a.b[0].c", "a.b.0.c", "[1]". Returns the value as display text, or null.
/// </summary>
public static class JsonPathLite
{
    public static string? Extract(string json, string? path)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var el = doc.RootElement;
            foreach (var segment in Tokenize(path ?? ""))
            {
                if (el.ValueKind == JsonValueKind.Array && int.TryParse(segment, out var i))
                {
                    if (i < 0 || i >= el.GetArrayLength()) return null;
                    el = el[i];
                }
                else if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(segment, out var child))
                {
                    el = child;
                }
                else
                {
                    return null;
                }
            }

            return el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => el.GetRawText(),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static IEnumerable<string> Tokenize(string path)
    {
        path = path.Trim();
        if (path.StartsWith('$')) path = path[1..];
        foreach (var raw in path.Split(['.', '[', ']'], StringSplitOptions.RemoveEmptyEntries))
        {
            var seg = raw.Trim().Trim('\'', '"');
            if (seg.Length > 0) yield return seg;
        }
    }
}
