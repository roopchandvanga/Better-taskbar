using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace Facet.Services;

/// <summary>
/// "Launch at startup" as a Task Scheduler task that runs when this user signs in.
/// <para>
/// Not the HKCU Run key: Explorer works through Run entries one at a time after sign-in, waiting on each
/// (a dock showed up minutes late), and on this machine it silently skipped Facet's entry altogether
/// even though it was present and enabled. The Run key stays only as a fallback if Task Scheduler refuses.
/// </para>
/// </summary>
internal static class StartupRegistration
{
    private const string TaskName = "Facet Dock";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Facet";

    private static readonly object Gate = new();
    private static string? _applied; // what this process last made true ("on|<exe>" or "off")

    /// <summary>Make startup match the setting. Cheap when nothing changed; runs schtasks, so call off the UI thread.</summary>
    public static void Apply(bool enabled, string? exe)
    {
        lock (Gate)
        {
            var wanted = enabled && exe is not null ? "on|" + exe : "off";
            if (wanted == _applied) return;

            if (enabled && exe is not null)
            {
                var task = ReadTask();
                var current = task is { Command: var c, Active: true } && string.Equals(c, exe, StringComparison.OrdinalIgnoreCase);
                if (!current) Note($"startup task {(task is null ? "missing" : $"is {task}")}; {(task is null ? "registering" : "updating")} for {exe}");
                var ok = current || task switch
                {
                    // exists but points elsewhere (Facet moved) or was disabled: fix it in place. Never delete and
                    // re-create it here: this process may be that task's running instance, and a /create /f
                    // from inside it lost the task
                    { } => UpdateTask(exe) || RegisterTask(exe),
                    null => RegisterTask(exe),
                };
                if (ok) RemoveRunValue();
                else SetRunValue(exe); // Task Scheduler blocked by policy: the Run key beats nothing
            }
            else
            {
                if (TaskExists())
                {
                    Note($"launch at startup is off (exe {exe ?? "unknown"}); removing the startup task");
                    Schtasks($"/delete /tn \"{TaskName}\" /f", out _);
                }
                RemoveRunValue();
            }
            _applied = wanted;
        }
    }

    private sealed record TaskInfo(string? Command, bool Active);

    /// <summary>The registered task's command and whether it (and its sign-in trigger) is enabled; null if there's none.</summary>
    private static TaskInfo? ReadTask()
    {
        if (Schtasks($"/query /tn \"{TaskName}\" /xml ONE", out var xml) != 0) return null;
        try
        {
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            var root = XDocument.Parse(xml.Trim()).Root;
            if (root is null) return new TaskInfo(null, false);
            var command = root.Descendants(ns + "Command").FirstOrDefault()?.Value.Trim().Trim('"');
            var enabled = root.Element(ns + "Settings")?.Element(ns + "Enabled")?.Value.Trim() != "false";
            var trigger = root.Descendants(ns + "LogonTrigger").Any(t => t.Element(ns + "Enabled")?.Value.Trim() != "false");
            return new TaskInfo(command, enabled && trigger);
        }
        catch (XmlException)
        {
            return new TaskInfo(null, false); // exists but unreadable: treat as needing an update
        }
    }

    private static bool TaskExists() => Schtasks($"/query /tn \"{TaskName}\"", out _) == 0;

    /// <summary>Point the existing task at <paramref name="exe"/> and enable it, without removing it first.</summary>
    private static bool UpdateTask(string exe) =>
        Schtasks($"/change /tn \"{TaskName}\" /tr \"\\\"{exe}\\\"\" /enable", out _) == 0;

    private static bool RegisterTask(string exe)
    {
        var user = WindowsIdentity.GetCurrent().User?.Value;
        if (user is null) return false;

        // at sign-in for this user only, unelevated, normal priority (task default 7 is "below normal"),
        // no time limit, keeps running on battery
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Starts the Facet dock when you sign in. Turn off in Facet settings (Launch at startup).</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(exe)}</Command>
                </Exec>
              </Actions>
            </Task>
            """;

        var file = Path.Combine(SettingsService.DataDir, "startup-task.xml");
        try
        {
            File.WriteAllText(file, xml, Encoding.Unicode); // schtasks wants UTF-16 to match the declaration
            return Schtasks($"/create /tn \"{TaskName}\" /xml \"{file}\" /f", out _) == 0;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        finally
        {
            try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static int Schtasks(string arguments, out string output)
    {
        output = "";
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
            });
            if (p is null) return -1;
            p.StandardInput.Close(); // /change asks for a "run as" password; it's not needed (the task runs in your session)
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(15_000)) { p.Kill(); return -1; }
            output = stdout.Result;
            // a missing task on /query is expected; anything else failing is worth a line in facet.log
            if (p.ExitCode != 0 && !arguments.StartsWith("/query", StringComparison.Ordinal))
                App.Log(new InvalidOperationException($"Startup task: schtasks {arguments.Split(' ')[0]} failed ({p.ExitCode}): {stderr.Result.Trim()}"));
            return p.ExitCode;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            App.Log(ex);
            return -1;
        }
    }

    /// <summary>Startup changes are rare and worth a line in facet.log (the log only takes exceptions).</summary>
    private static void Note(string message) => App.Log(new InvalidOperationException("Startup: " + message));

    private static void SetRunValue(string exe) => WithRunKey(key =>
    {
        var value = $"\"{exe}\"";
        if (!Equals(key.GetValue(RunValue), value)) key.SetValue(RunValue, value);
    });

    private static void RemoveRunValue() => WithRunKey(key =>
    {
        if (key.GetValue(RunValue) is not null) key.DeleteValue(RunValue, throwOnMissingValue: false);
    });

    private static void WithRunKey(Action<RegistryKey> action)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is not null) action(key);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            // policy-restricted machine; ignore
        }
    }

}
