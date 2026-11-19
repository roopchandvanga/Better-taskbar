using System.Windows.Media;
using Facet.Core.Settings;
using Facet.Infrastructure;

namespace Facet.Apps;

/// <summary>One icon on the dock: a pinned app, a running app, or both.</summary>
public sealed class AppItem(string key) : ObservableObject
{
    private string _name = "";
    private string _tooltip = "";
    private ImageSource? _icon;
    private bool _isPinned, _isRunning, _isActive, _isDropTarget, _isLeaving, _isLaunching;
    private int _windowCount;

    public string Key { get; } = key;

    public string Name { get => _name; set => Set(ref _name, value); }
    public string Tooltip { get => _tooltip; set => Set(ref _tooltip, value); }
    public ImageSource? Icon { get => _icon; set => Set(ref _icon, value); }
    public bool IsPinned { get => _isPinned; set => Set(ref _isPinned, value); }
    public bool IsRunning { get => _isRunning; set => Set(ref _isRunning, value); }
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
    public int WindowCount { get => _windowCount; set => Set(ref _windowCount, value); }
    /// <summary>A file is being dragged over this icon (highlights it).</summary>
    public bool IsDropTarget { get => _isDropTarget; set => Set(ref _isDropTarget, value); }
    /// <summary>Gone from the dock: it stays a moment, in place, while the dock animates it out.</summary>
    public bool IsLeaving { get => _isLeaving; set => Set(ref _isLeaving, value); }
    /// <summary>Started from the dock and no new window yet (the icon bounces).</summary>
    public bool IsLaunching { get => _isLaunching; set => Set(ref _isLaunching, value); }

    /// <summary>When the launch started and how many windows the app had then.</summary>
    internal (DateTime At, int Windows) LaunchedFrom { get; set; }

    /// <summary>Top-level windows in z-order (front first).</summary>
    public List<IntPtr> Windows { get; } = [];

    /// <summary>What to start when launching: .lnk, exe, or "shell:AppsFolder\AUMID".</summary>
    public string? LaunchTarget { get; set; }
    public string? Aumid { get; set; }
    public string? ExePath { get; set; }
    /// <summary>Set when the pin comes from the Windows taskbar's pinned folder.</summary>
    public string? PinShortcut { get; set; }
    /// <summary>Set when the pin was added in Facet.</summary>
    public PinnedApp? ExtraPin { get; set; }
    public bool IconLoaded { get; set; }
}
