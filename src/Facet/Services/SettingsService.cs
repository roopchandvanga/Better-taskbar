using Facet.Core.Settings;

namespace Facet.Services;

public sealed class SettingsService
{
    public static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Facet");

    public static string SettingsPath => Path.Combine(DataDir, "settings.json");

    public FacetSettings Current { get; private set; }

    /// <summary>Raised after every save so the UI can re-apply appearance and widgets.</summary>
    public event Action? Changed;

    public SettingsService()
    {
        Directory.CreateDirectory(DataDir);
        var saved = File.Exists(SettingsPath);
        Current = JsonStore.Load<FacetSettings>(SettingsPath);
        Current.Normalize();
        // self-repair: if Facet was moved, point the sign-in task at the copy that's actually running. Only from
        // settings the user actually saved: running on defaults (no file, e.g. a first run or a data folder that
        // isn't the usual one) must never switch off a startup task they set up
        if (saved) ApplyStartup(Current.LaunchAtStartup);
    }

    public void Save()
    {
        Current.Normalize();
        try { JsonStore.Save(SettingsPath, Current); }
        catch (IOException) { /* transient lock; next save wins */ }
        ApplyStartup(Current.LaunchAtStartup);
        Changed?.Invoke();
    }

    public void Export(string path) => JsonStore.Save(path, Current);

    public bool Import(string path)
    {
        if (!File.Exists(path)) return false;
        var loaded = JsonStore.Load<FacetSettings>(path);
        loaded.Normalize();
        Current = loaded;
        Save();
        return true;
    }

    /// <summary>Sign-in task pointing at the copy that's actually running (see <see cref="StartupRegistration"/>).</summary>
    private static void ApplyStartup(bool enabled)
    {
        var exe = Environment.ProcessPath;
        // a debug build (bin\…) or a test harness isn't the installed dock: leave startup pointing where it is
        if (exe is null || !Path.GetFileName(exe).Equals("Facet.exe", StringComparison.OrdinalIgnoreCase)
            || exe.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase)) return;
        _ = Task.Run(() => StartupRegistration.Apply(enabled, exe));
    }
}
