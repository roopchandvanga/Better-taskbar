# Facet Dock Implementation Plan

> Executed inline (executing-plans style) in the session that wrote it. Steps use `- [ ]`.

**Goal:** Native WPF smart dock for Windows per the spec.
**Architecture:** `Facet.Core` (net10.0, pure logic, unit-tested) + `Facet` (WPF app, interop,
UI) + `Facet.Core.Tests` (xUnit). Services are plain singletons owned by `App`; widgets bind to
them through a tiny `ObservableObject`.
**Tech Stack:** .NET 10, C#, WPF, WinRT projections (`net10.0-windows10.0.19041.0`), xUnit.
**Spec:** `docs/superpowers/specs/2026-11-17-facet-dock-design.md`

## Global Constraints
- No third-party runtime packages in `Facet`/`Facet.Core` (tests may use xUnit).
- Only network use: Open-Meteo, user-configured iCal URLs, user-configured custom-widget URLs.
- Taskbar is never modified unless `HideTaskbar` is on; always restored on exit/crash/next launch.
- All persisted data under `%AppData%\Facet\`.

## File map
```
Facet.sln
src/Facet.Core/            (net10.0 class library)
  Calendar/CalendarEvent.cs, ICalParser.cs
  Alerts/AlertEngine.cs
  Palette/FuzzyMatcher.cs, MathEvaluator.cs
  Glass/Refractor.cs        (blur, lens refraction, luminance on BGRA buffers)
  Widgets/JsonPathLite.cs
  Settings/FacetSettings.cs, JsonStore.cs
  Todos/TodoItem.cs
src/Facet/                 (WinExe, WPF)
  App.xaml(.cs), app.manifest
  Interop/Native.cs, ShellInterop.cs, AudioInterop.cs
  Infrastructure/ObservableObject.cs, Services.cs (service locator), RelayCommand.cs
  Theme/ThemeManager.cs, Styles.xaml
  Glass/WallpaperSource.cs, GlassRenderer.cs, Backdrop.cs (DWM acrylic for popovers)
  Shell/DockWindow.xaml(.cs), TaskbarController.cs, AppBar.cs, ForegroundTracker.cs, Hotkey.cs
  Apps/AppTracker.cs, AppItem.cs, IconLoader.cs, AppCatalog.cs
  Services/SettingsService.cs, SystemMonitor.cs, MediaService.cs, AudioService.cs,
           WeatherService.cs, CalendarService.cs, TodoService.cs, PomodoroService.cs,
           CustomWidgetService.cs, AlertService.cs, StartupService.cs
  Controls/Ring.cs, WeatherIcon.cs, Glyph.cs
  Widgets/*.xaml(.cs)  (Clock, UpNext, Media, Weather, System, Battery, Pomodoro, Todos,
                        Toggles, Custom)
  Popovers/PopoverWindow.cs, CalendarPanel.xaml, TodoPanel.xaml, MediaPanel.xaml,
           WeatherPanel.xaml, ToastWindow.xaml
  Palette/CommandPalette.xaml(.cs)
  SettingsUi/SettingsWindow.xaml(.cs)
tests/Facet.Core.Tests/    (xUnit)
```

## Tasks
- [ ] 1. Solution scaffold; `dotnet build` green.
- [ ] 2. Core: FuzzyMatcher, MathEvaluator, JsonPathLite — tests first.
- [ ] 3. Core: ICalParser (+RRULE daily/weekly), CalendarEvent — tests first.
- [ ] 4. Core: AlertEngine — tests first.
- [ ] 5. Core: Refractor (displacement monotonic, blur, luminance) — tests first.
- [ ] 6. Core: FacetSettings + JsonStore round-trip — tests first.
- [ ] 7. App shell: interop, App startup/shutdown, single instance, settings service, theme.
- [ ] 8. Dock window: layout, positioning, edge drag, fullscreen hide, glass renderer.
- [ ] 9. Apps: pins + running windows, icons, activation, context menus.
- [ ] 10. Widgets + services: clock/calendar, up next, media, weather, system, battery,
      pomodoro, todos, toggles, custom.
- [ ] 11. Alerts + toasts; command palette + hotkey; settings window; taskbar/appbar/startup.
- [ ] 12. Build, run, screenshot each theme, open popovers; fix issues.

Verification per task: `dotnet test tests/Facet.Core.Tests` (Core) / `dotnet build` (App),
final manual run with screenshots.
