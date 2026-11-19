using System.Xml;
using System.Xml.Linq;

namespace Facet.Core.Apps;

/// <summary>
/// Store / MSIX apps live in a folder named after their full package name, version included
/// (…\WindowsApps\Claude_5.11492.5.0_x64__pzs8sxrjxfjjc\app\claude.exe), so a pin that stores that exe
/// path breaks on the next update. Their app ID (AUMID, "Claude_pzs8sxrjxfjjc!Claude") doesn't change.
/// These helpers get from an install path to the version-free pieces of that ID.
/// </summary>
public static class PackagedApp
{
    private const string Marker = @"\WindowsApps\";

    /// <summary>
    /// Split an exe path inside WindowsApps into its package family name ("Name_PublisherId") and the exe's
    /// path relative to the package folder. False for anything that isn't inside a package folder.
    /// </summary>
    public static bool TryParsePath(string path, out string familyName, out string relativeExe)
    {
        familyName = relativeExe = "";
        var at = path.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return false;

        var rest = path[(at + Marker.Length)..];
        var slash = rest.IndexOf('\\');
        if (slash <= 0 || slash == rest.Length - 1) return false;

        // full name: Name_Version_Architecture_ResourceId_PublisherId (the resource id is usually empty)
        var parts = rest[..slash].Split('_');
        if (parts.Length != 5 || parts[0].Length == 0 || parts[4].Length == 0 || !Version.TryParse(parts[1], out _)) return false;

        familyName = $"{parts[0]}_{parts[4]}";
        relativeExe = rest[(slash + 1)..];
        return true;
    }

    /// <summary>
    /// The Application Id in an AppxManifest whose Executable is <paramref name="relativeExe"/>; the only
    /// application if there's just one; otherwise null.
    /// </summary>
    public static string? AppIdFromManifest(string manifestXml, string relativeExe)
    {
        try
        {
            var apps = XDocument.Parse(manifestXml).Descendants()
                .Where(e => e.Name.LocalName == "Application" && e.Attribute("Id") is not null)
                .ToList();
            var wanted = Normalize(relativeExe);
            var match = apps.FirstOrDefault(a => Normalize((string?)a.Attribute("Executable")) == wanted);
            return (string?)(match ?? (apps.Count == 1 ? apps[0] : null))?.Attribute("Id");
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static string Normalize(string? path) => (path ?? "").Replace('/', '\\').TrimStart('\\').ToLowerInvariant();
}
