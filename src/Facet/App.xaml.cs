using System.Windows;
using System.Windows.Threading;
using Facet.Infrastructure;
using Facet.Interop;
using Facet.Services;
using Facet.Shell;
using Facet.Theme;

namespace Facet;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private DockWindow? _dock;
    private bool _cleanedUp;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\Facet.Dock.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, a) =>
        {
            Log(a.ExceptionObject as Exception);
            Cleanup(); // never leave the user without a taskbar
        };
        TaskScheduler.UnobservedTaskException += (_, a) =>
        {
            Log(a.Exception);
            a.SetObserved();
        };
        SessionEnding += (_, _) => Cleanup();

        // started by the sign-in task, Facet can be up before Explorer's taskbar exists; hiding it, the app
        // bar and the pins all need the shell, so wait for it (up to a minute) before building the dock
        WhenShellReady(StartDock);
    }

    private static void WhenShellReady(Action start)
    {
        if (Native.FindWindow("Shell_TrayWnd", null) != IntPtr.Zero)
        {
            start();
            return;
        }
        var waited = TimeSpan.Zero;
        var appearedAt = TimeSpan.MinValue;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            waited += timer.Interval;
            if (appearedAt == TimeSpan.MinValue && Native.FindWindow("Shell_TrayWnd", null) != IntPtr.Zero) appearedAt = waited;
            var settled = appearedAt != TimeSpan.MinValue && waited - appearedAt >= TimeSpan.FromSeconds(1.5);
            if (!settled && waited < TimeSpan.FromMinutes(1)) return;
            timer.Stop();
            start();
        };
        timer.Start();
    }

    private void StartDock()
    {
        AppServices.Create();
        AppServices.Taskbar.RecoverIfDirty();

        var settings = AppServices.Settings.Current;
        ThemeManager.ApplySize(settings.Size);
        ThemeManager.ApplySurface(settings.Theme);
        ThemeManager.ApplyDock(settings.Theme);

        AppServices.Start();

        _dock = new DockWindow();
        _dock.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void Cleanup()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;
        try { _dock?.Teardown(); } catch (Exception ex) { Log(ex); }
        try { AppServices.Taskbar?.Restore(); } catch (Exception ex) { Log(ex); }
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        e.Handled = true; // a widget failing must not take the dock (and the hidden taskbar) down with it
    }

    internal static void Log(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            var path = Path.Combine(SettingsService.DataDir, "facet.log");
            Directory.CreateDirectory(SettingsService.DataDir);
            if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
            File.AppendAllText(path, $"[{DateTime.Now:O}] {ex}\n\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
