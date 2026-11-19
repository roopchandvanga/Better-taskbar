using System.Runtime.InteropServices;
using Facet.Interop;

namespace Facet.Shell;

/// <summary>Parses "Ctrl+Alt+Space"-style shortcuts and registers the first one that is free.</summary>
internal static class Hotkeys
{
    public static readonly string[] Choices = ["Ctrl+Alt+Space", "Win+Alt+Space", "Ctrl+Shift+Space", "Alt+Shift+Space", "Ctrl+Alt+K"];

    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char ch);

    /// <summary>The shortcut currently registered, or null if none could be.</summary>
    public static string? Active { get; private set; }

    /// <summary>Human-readable status for settings and the palette.</summary>
    public static string Status { get; private set; } = "";

    public static bool TryParse(string text, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= Native.MOD_CONTROL; break;
                case "alt": modifiers |= Native.MOD_ALT; break;
                case "shift": modifiers |= MOD_SHIFT; break;
                case "win" or "windows": modifiers |= MOD_WIN; break;
                case "space": vk = Native.VK_SPACE; break;
                default:
                    if (raw.Length == 1 && char.IsLetterOrDigit(raw[0])) vk = (uint)(VkKeyScan(char.ToLowerInvariant(raw[0])) & 0xFF);
                    else if (raw.StartsWith('F') && int.TryParse(raw[1..], out var f) && f is >= 1 and <= 24) vk = (uint)(0x70 + f - 1);
                    else return false;
                    break;
            }
        }
        return vk != 0 && modifiers != 0;
    }

    /// <summary>Register <paramref name="preferred"/>, falling back to the other choices if it's taken.</summary>
    public static void Register(IntPtr hwnd, int id, string preferred)
    {
        Unregister(hwnd, id);
        foreach (var candidate in new[] { preferred }.Concat(Choices).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!TryParse(candidate, out var mods, out var vk)) continue;
            if (!Native.RegisterHotKey(hwnd, id, mods | Native.MOD_NOREPEAT, vk)) continue;
            Active = candidate;
            Status = string.Equals(candidate, preferred, StringComparison.OrdinalIgnoreCase)
                ? $"Active: {candidate}"
                : $"Active: {candidate} ({preferred} is used by another app)";
            return;
        }
        Active = null;
        Status = "No shortcut available — every choice is used by another app.";
    }

    public static void Unregister(IntPtr hwnd, int id)
    {
        if (Active is null) return;
        Native.UnregisterHotKey(hwnd, id);
        Active = null;
        Status = "Shortcut off";
    }
}
