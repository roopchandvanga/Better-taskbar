# Facet — a native smart dock for Windows

A see-through, auto-hiding dock that replaces (or floats above) the Windows taskbar and puts live widgets one glance away.
Built in C# / WPF on .NET 10. No web runtime, no account, no telemetry.

## Run it

```powershell
dotnet build Facet.slnx -c Release
.\src\Facet\bin\Release\net10.0-windows10.0.22621.0\Facet.exe
```

Or publish a single-file exe (needs the .NET 10 Desktop Runtime, which Windows Update ships):

```powershell
dotnet publish src/Facet/Facet.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
.\publish\Facet.exe
```

Tests (pure logic: iCal, alert rules, math, fuzzy search, JSON paths, settings):

```powershell
dotnet test tests/Facet.Core.Tests
```

## What's on the dock

| Left | Middle | Right |
|---|---|---|
| Start button, your real taskbar pins + running apps (running dot, an active-app line that glides from app to app, click to focus/minimize, cycle windows, middle-click for a new window, right-click for windows / pin / close). Icons near the pointer grow like the macOS dock; resting on a running app shows live previews of its windows (click to switch, × to close) | Up next (mini calendar + next event countdown, and a Join button when the next event is a Teams / Zoom / Meet / Webex call starting within 10 minutes), Now playing (any app using Windows media controls, tinted with the album art's colour), Weather, Todos, your custom widgets. Opt-in: CPU & RAM rings, Pomodoro | Quick controls tile: Wi-Fi icon inside a battery ring gauge (percentage on top; red when low, green while charging; click → network list), Bluetooth (click → devices), Project (Win+P), and an expand chevron that opens volume, mic, do-not-disturb, Wi-Fi, Bluetooth, project and battery controls. Add or remove buttons (battery ring, volume, mic, do not disturb, screenshot) and reorder them: right-click the tile, or Settings → Widgets. Clock → calendar popover. Notification bell at the far right → Windows' notification center (Win+N); its badge counts notifications that arrived since you last opened it from the dock, hovering it previews the latest three, and it's crossed out while Windows' do not disturb is on |

- **Look:** a borderless rounded rectangle (macOS-like ~18 px corners) with live acrylic blur from the Windows compositor — it shows whatever is actually behind it (apps, video, wallpaper) — and a hairline of light along its top edge. Icons are requested from Windows at the exact pixel size they're drawn at, so they stay sharp at any display scaling. The accent follows your Windows accent colour, and the theme can follow Windows' light/dark mode (Glass / Frosted).
- **Motion:** panels rise out of the dock and grow from what opened them, and sink back when they close; apps slide in and out of the dock, bounce while starting, and the active-app line glides between them; rings sweep to new values, the clock rolls over, the bell rings for a new notification, quick-panel tiles fill from the centre. Every animation is short and only runs when something changes. Turn them off in Settings → Appearance → Animations (they're also off whenever Windows' "Animation effects" is).
- **Windows key:** pressing Win (Start or Search opening) brings the dock up too; it tucks away again once Start closes.
- **Themes:** Glass (dark, most see-through), Frosted (light), Midnight, Black, White · sizes S/M/L · floating or full width · bottom or top (drag the dock to switch).
- **Several displays:** optionally (Settings → Dock & taskbar), hold the pointer at another display's bottom edge and the dock moves there.
- **Auto-hide (on by default):** the dock stays out of sight and slides up when the cursor touches the bottom edge (top edge when docked at the top); it stays up while you're on it or one of its panels is open. Pair it with *Hide Windows taskbar* for a macOS-style setup. With auto-hide off, the dock reserves its strip of the screen and maximized windows stop above it.
- **Calendar popover:** month grid with event dots, agenda, quick add, read-only iCal subscriptions (Google / Outlook / iCloud) every 15 min.
- **Smart alerts:** "Design review starts in 4 min — Pause music", low battery, CPU running hot, Pomodoro done. Quiet hours and focus sessions hold alerts until they matter.
- **Command palette** (`Ctrl+Alt+Space`, or the first free fallback such as `Win+Alt+Space`): apps, dock actions, `=` math, `>` shell commands, `todo …`, folders, URLs, `?` web search.
- **Custom widgets:** any shell command or URL (+ optional JSON path) becomes a tile with its own refresh interval.

## Where it goes beyond lumabar

| | lumabar | Facet |
|---|---|---|
| Price / source | $5.99 licence, closed | Free, source in this folder |
| Runtime | — | Native WPF, no browser engine (measured: ~61 MB private memory, ~0.2% total CPU at idle on an 8-core laptop) |
| Glass | Bends the wallpaper only | Live compositor blur of whatever is behind the dock, including app windows |
| Popovers | Custom | Real Windows 11 acrylic (DWM) |
| First launch | Sample data until accounts are connected | Real data immediately (system, media, weather via Windows location or city) |
| Keyboard | — | Command palette with math, commands, todos, folders |
| Long-tail widgets | Wait for the developer | Build your own from a command or URL |
| Taskbar safety | Restored on quit | Restored on quit, crash handlers *and* next launch if the process was killed |
| Screen space | Overlaps maximised windows | With auto-hide off, the dock keeps its own strip of the screen (an AppBar, like the taskbar), so maximised windows stop at it |
| Fullscreen | Hides | Auto-hide: still reachable in fullscreen apps by holding the cursor at the edge ~0.35 s (like macOS); exclusive-fullscreen games are left alone. Always-visible mode steps aside for fullscreen apps; maximized windows keep the dock |
| Auto-hide | — | macOS-style reveal at the screen edge |
| Portability | — | Export / import settings as JSON |

## Data

Everything lives in `%AppData%\Facet\` (`settings.json`, `events.json`, `todos.json`, `state.json`, `facet.log`).
Network calls: Open-Meteo for weather (MET Norway, api.met.no, as the fallback when Open-Meteo doesn't answer within 8 s), and only the iCal / custom-widget URLs you add.

**Launch at startup** registers a Task Scheduler task, "Facet Dock", that starts the dock as soon as you sign in (no admin rights needed; turning the setting off removes it). Facet keeps the task pointed at whichever copy of Facet.exe is running, and waits for the taskbar to exist before hiding it. It isn't an HKCU `Run` entry: Explorer works through those one at a time, minutes after sign-in.

**Launching Facet from another app's terminal:** some packaged apps (the Claude desktop app, for one) redirect AppData and HKCU for processes started from their shell, so a Facet launched that way reads and writes a private copy of its settings. Start it from Explorer, the Start menu, or `schtasks /run /tn "Facet Dock"` instead.

## Layout

```
src/Facet.Core     pure logic (net10.0) — unit tested
src/Facet          WPF app: Shell/ (dock, taskbar, appbar, hotkeys), Glass/, Apps/, Services/, Widgets/, Popovers/, Palette/, SettingsUi/
tests/             xUnit tests for Facet.Core
docs/superpowers/  design spec and implementation plan
```
