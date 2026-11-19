using System.Text.Json;
using System.Text.Json.Serialization;

namespace Facet.Core.Settings;

/// <summary>Atomic JSON persistence. Corrupt files are kept as "*.corrupt" and defaults returned.</summary>
public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T Load<T>(string path) where T : new()
    {
        if (!File.Exists(path)) return new T();
        try
        {
            var json = ReadWithRetry(path);
            return JsonSerializer.Deserialize<T>(json, Options) ?? new T();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            try { File.Copy(path, path + ".corrupt", overwrite: true); } catch (IOException) { }
            return new T();
        }
        catch (IOException)
        {
            return new T();
        }
    }

    /// <summary>A file briefly locked (antivirus scan, sync client, a save in flight) shouldn't mean "use defaults".</summary>
    private static string ReadWithRetry(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return File.ReadAllText(path); }
            catch (IOException) when (attempt < 4) { Thread.Sleep(150 * attempt); }
        }
    }

    public static void Save<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }
}
