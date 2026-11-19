using System.Runtime.InteropServices;

namespace Facet.Apps;

public sealed record CatalogApp(string Name, string ParsingName);

/// <summary>Every app in the Start menu (Win32 and Store), via the shell's AppsFolder.</summary>
public sealed class AppCatalog
{
    private volatile IReadOnlyList<CatalogApp> _apps = [];

    public IReadOnlyList<CatalogApp> Apps => _apps;

    public void LoadInBackground()
    {
        var thread = new Thread(() =>
        {
            try { _apps = EnumerateAppsFolder(); }
            catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                _apps = EnumerateStartMenu();
            }
            if (_apps.Count == 0) _apps = EnumerateStartMenu();
        })
        { IsBackground = true, Name = "Facet app catalog" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static List<CatalogApp> EnumerateAppsFolder()
    {
        var type = Type.GetTypeFromProgID("Shell.Application") ?? throw new COMException("Shell.Application unavailable");
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic folder = shell.NameSpace("shell:AppsFolder");
        var list = new List<CatalogApp>();
        foreach (var item in folder.Items())
        {
            string name = item.Name;
            string path = item.Path;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;
            list.Add(new CatalogApp(name, $@"shell:AppsFolder\{path}"));
        }
        return list.DistinctBy(a => a.ParsingName, StringComparer.OrdinalIgnoreCase)
                   .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                   .ToList();
    }

    private static List<CatalogApp> EnumerateStartMenu()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        };
        var list = new List<CatalogApp>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                foreach (var lnk in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileNameWithoutExtension(lnk);
                    if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new CatalogApp(name, lnk));
                }
            }
            catch (UnauthorizedAccessException) { }
        }
        return list.DistinctBy(a => a.Name, StringComparer.OrdinalIgnoreCase).OrderBy(a => a.Name).ToList();
    }
}
