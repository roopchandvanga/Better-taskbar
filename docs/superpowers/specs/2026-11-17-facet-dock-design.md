# Facet — native Windows smart dock (design)

Date: 2026-11-17 · Status: approved

## Goal
An original, native Windows 10/11 dock in the genre of lumabar.xyz: a glass pill dock that
replaces the taskbar and hosts live widgets — but free/open, fully native, real data on first
launch, a command palette, and user-defined widgets.

## Stack
C# 13 / .NET 10, WPF, `net10.0-windows10.0.19041.0` (WinRT projections for media controls).
No web runtime. Tests: xUnit on pure logic.

## Shell
- `DockWindow`: borderless, `AllowsTransparency`, topmost, `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`
  (no taskbar/Alt-Tab entry, doesn't steal focus). Primary monitor only (v1).
- Layout: centered pill (default) or full width; bottom (default) or top edge; size S/M/L
  (item height 40/48/56 px). Drag dock into top half → top edge; panels open away from edge.
- Hides when a fullscreen app is foreground (poll foreground window rect vs monitor, 1 s).
- Optional AppBar registration (`SHAppBarMessage ABM_NEW/ABM_SETPOS`) to reserve work area.

## Look
Themes: LiquidGlass, Frost, Midnight, Black, White.
- LiquidGlass: read wallpaper (`SystemParametersInfo SPI_GETDESKWALLPAPER`, fallback
  `%AppData%\Microsoft\Windows\Themes\TranscodedWallpaper`), map to screen with "fill" scaling,
  crop the dock's rect (+margin), apply a CPU refraction pass (lens displacement that grows toward
  the rounded-rect edge, slight chromatic split, light blur, saturation boost), then a rim
  highlight and top specular gradient in XAML. Recomputed only on wallpaper/position/size change
  (`WM_SETTINGCHANGE` + 30 s wallpaper path check). Zero cost while idle.
- Frost: same source, heavy blur + tint. Midnight/Black/White: solid tinted fills.
- Popovers and toasts share the dock theme.

## Dock contents (left → right)
1. Start button → sends Win key (opens Start).
2. Apps: taskbar pins (`%AppData%\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\*.lnk`)
   + extra pins from settings, merged with running top-level windows (EnumWindows, Alt-Tab rules).
   Running dot; click = activate / minimize if foreground / launch if not running; middle-click =
   new instance; right-click menu: Open new, Close window(s), Pin/Unpin.
3. Widgets (ordered, toggleable).
4. Clock/date tile → calendar popover.

## Widgets (v1, all local/no account)
- System: CPU % and RAM % rings (PerformanceCounter-free: `GetSystemTimes`, `GlobalMemoryStatusEx`).
- Battery ring (`GetSystemPowerStatus`), hidden on desktops without battery.
- Now Playing: `GlobalSystemMediaTransportControlsSessionManager` — title/artist/thumbnail,
  play/pause/next/prev.
- Weather: Open-Meteo forecast + geocoding (no key). City set in settings. 20 min refresh.
- Todos: popover list, add/check/delete, persisted.
- Pomodoro: 25/5 ring timer; start → optional quiet hours during focus.
- Quick toggles: volume/mute (Core Audio `IAudioEndpointVolume`), mic mute.
- Custom widget (N instances): label + shell command or JSON URL (+ optional JSON path) +
  interval; renders a value tile; click opens a URL/command if set.

## Calendar popover
Month grid with dots, today's agenda list, quick-add (time + title), local events persisted.
iCal subscriptions (URL list) fetched read-only every 15 min; minimal RFC 5545 parser
(VEVENT DTSTART/DTEND/SUMMARY/LOCATION, all-day, TZID ignored→local, UTC `Z` converted,
line unfolding, basic RRULE FREQ=DAILY/WEEKLY with COUNT/UNTIL expansion within ±60 days).

## Smart alerts (rules engine)
Pure `AlertEngine.Evaluate(Snapshot, now, state) → Alert[]` with de-dup keys:
- Event starts in ≤5 min → "Up next: {title}" + action "Pause music" (if media playing).
- Battery ≤ 20% and discharging.
- CPU ≥ 90% sustained 60 s.
- Pomodoro phase finished.
- Todo with due time reached (v1: none — YAGNI; todos have no due date).
Quiet hours (start/end) and Pomodoro focus suppress non-critical alerts.
Toast: glass card above dock's right end, auto-dismiss 8 s, buttons.

## Command palette
Global hotkey Ctrl+Alt+Space (`RegisterHotKey`). Fuzzy search across: Start-menu shortcuts
(both ProgramData and AppData Start Menu `.lnk`), dock commands (Settings, theme switch, toggle
taskbar, start pomodoro), `>` shell command, `todo <text>`, `=` math (DataTable.Compute-free
recursive descent evaluator), paths (`C:\…` → open folder), URLs.

## Taskbar handling
Setting `HideTaskbar` (off by default). On enable: save original auto-hide state to
`state.json` with `dirty=true`, set ABS_AUTOHIDE and hide `Shell_TrayWnd` (+secondary).
Restore on exit, `UnhandledException`, `SessionEnding`; on launch, if `dirty`, restore first.

## Settings & persistence
`%AppData%\Facet\settings.json` (theme, size, edge, layout, clock24, startup, hideTaskbar,
reserveSpace, widgets order/enabled, weather city/lat/lon, iCal URLs, quiet hours, custom
widgets, extra pins), `todos.json`, `events.json`, `state.json`. Export/import in settings.
Launch at startup via `HKCU\…\Run`. Settings window: native WPF, themed.

## Testing
xUnit: ICalParser, AlertEngine, MathEvaluator, FuzzyMatcher, SettingsStore round-trip,
RefractionMap (displacement monotonic toward edge, zero at center), JsonPath extraction.
Manual: build, run, screenshot dock in each theme, open popovers.

## Revision — 2026-11-17 (user feedback after first run)
- Wallpaper refraction removed: it ignored app windows behind the dock. The dock is now a non-layered
  window with live acrylic (SetWindowCompositionAttribute accent policy, which stays translucent while
  the never-activated dock is inactive), DWM-rounded corners (Windows 11's fixed ~8 px radius) and no
  DWM border. Adaptive-contrast setting removed; text colour follows the theme.
- Auto-hide (default on): 60 ms cursor poll; reveal when the cursor touches the dock's screen edge,
  conceal 650 ms after it leaves unless a popover / palette / menu is open; slide animation on Window.Top.
- Default widgets: CPU/RAM and Pomodoro opt-in; battery moved last (beside the clock) as a
  percentage + battery glyph.
- Fullscreen: hide only when SHQueryUserNotificationState reports busy/D3D/presentation and the
  foreground window covers its monitor without being maximized (or is the window the shell flagged).
- File Explorer: Explorer windows and the Windows 11 File Explorer pin both map to
  `Microsoft.Windows.Explorer`, so they share one dock icon.

## Revision 2 — 2026-11-17
- Corners (what shipped, after two dead ends): the accent-policy blur ignores both per-pixel alpha
  (layered windows) and window regions, so it can't be shaped. The dock is now two windows:
  `DockGlassWindow` (content-less, click-through) hosts a Windows.UI.Composition DesktopWindowTarget
  with a HostBackdropBrush + tint, clipped by an anti-aliased CompositionRoundedRectangleGeometry
  (~18 DIP radius); the transparent, layered `DockWindow` with the WPF content is *owned* by it so it
  always stays directly on top. (A compositor target on the WPF window itself hides WPF's content.)
  The glass window is moved/shown/hidden in lockstep (LocationChanged / SizeChanged / IsVisibleChanged).
- Crisp icons: every dock scale lookup uses GetDpiForWindow (VisualTreeHelper.GetDpi reports 1.0
  until the first layout pass, which made earlier builds size icons for 100 % and resample them).
  Icons use a standard size (48 px at 125 %), fetched at 2x and halved on whole pixels.
- Spacing: AppSpacing resource (3 DIP, pixel-snapped) on each side of every app button.
- Windows key: auto-hide also reveals (and holds) the dock while StartMenuExperienceHost / SearchHost
  is the foreground window.
- Fullscreen + auto-hide: reveal is never blocked; in fullscreen apps it needs a 350 ms dwell at the
  edge; exclusive D3D fullscreen is left alone.

## Out of scope (v1)
Multi-monitor, online account widgets (GitHub, Stripe, AI limits), installer, auto-update.
