# Hub Display Switch — Agent Instructions

## Project overview

A resident WinUI 3 tray app that switches the display layout, refresh rates and default audio device
when the USB hub for a keyboard/mouse/controller connects or disconnects. Ships as an **MSIX**
(`HubDisplaySwitchMSIX/`), built from WSL through the shared Windows build bridge. Follows Little
Launcher / Repilot conventions (settings window, `SettingsManager`/`UserSettings`, NLog, MSIX script).

## Concepts

- **Setup** (`Models/Setup.cs`): name + icon, trigger hubs, displays to turn on (+ main display, mirror),
  playback device (+ "also for calls"). Exactly one setup is the **default** (devices plugged straight
  into the PC); others are triggered by hubs.
- **Arbitration** (`Services/SwitchEngine.cs`): the most recently connected hub's setup wins; when its hub
  goes, fall back to the previous still-connected hub's setup, then the default. At startup, setups
  higher in the list win. Manual switches (flyout / tray menu / "Switch now") stick until the desired
  setup next changes.
- **Switch sequence**: popup → apply layout (retry ~60 s while a TV wakes) → *Settle* (refresh rates +
  audio until stable; TV HDMI audio appears seconds after its display) → Done popup → *Background*
  re-checks (TVs often drop back to 60 Hz after re-syncing).

## Architecture

| Area | Files |
|---|---|
| Host (hidden, resident, owns tray + engine) | `MainWindow.xaml(.cs)` |
| Tray flyout (click tray icon) | `Windows/SwitcherWindow.xaml(.cs)` |
| On-screen popup | `Windows/OsdWindow.xaml(.cs)` |
| Settings window + pages | `SettingsWindow.xaml(.cs)`, `Pages/SetupPage`, `Pages/GeneralPage`, `Pages/AboutPage`, `Pages/DetectHubDialog.cs` |
| Displays (CCD `SetDisplayConfig`, `ChangeDisplaySettingsEx`) | `Services/DisplayConfig.cs` |
| USB presence + change events (WMI) | `Services/UsbDevices.cs` |
| Default audio device (Core Audio + undocumented `IPolicyConfig`) | `Services/AudioDevices.cs` |
| Settings | `ViewModels/UserSettings.cs`, `Classes/Settings/SettingsManager.cs` (kit `JsonSettingsStore`) |
| Tray, flyout window, single instance, startup, theme, window chrome, localization mechanism | `external/winui-app-kit` submodule (shared WinUI App Kit), `ProjectReference` in the csproj |
| UI P/Invoke | kit `WinUIAppKit.Interop.NativeMethods`, plus the popup's extras in `Classes/NativeMethods.cs` (both via `using static`; never redeclare a kit name) |
| UI text / languages | `Classes/AppLanguages.cs`, `Localization/Catalogs/*.json` (kit `UiText` / `Localize`) |
| Tray icon image | `Classes/TrayIconRenderer.cs` (PNG at `TrayIcon.IconPixelSize`) |
| "Our other apps" page | `external/promo` submodule (shared `apps-promo` catalog), imported in the csproj |

## Conventions / gotchas

- **WinUI App Kit** (`external/winui-app-kit`, read its README/AGENTS first) owns the shared window
  plumbing: `TrayIcon` (raw `Shell_NotifyIcon`, stable GUID, re-added after Explorer restarts,
  `DisplayChanged`/`RenderNeeded` events), `TrayMenu` (native Win32 menu, dark via undocumented uxtheme
  ordinals), `FlyoutController` (context-menu presenter, acrylic, **cloaked and parked off-screen, never
  hidden**, reveal after render, 300 ms reopen guard), `WindowChrome`/`WindowIcon`, `AppTheme`,
  `StartupRegistration`, `SingleInstance`, `JsonSettingsStore`, `UiText`/`Localize`. Hub keeps its own
  identity values in `MainWindow` (tray GUID, mutex, message names, startup task id: never change them,
  Windows keys the tray show/hide choice on the GUID). Behaviour Hub needs from the kit goes in as a
  general option or event in a separate kit commit, and WSL Monitor must still build against it.
- The popup must never activate: `WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED`;
  fade via `SetLayeredWindowAttributes`; re-centred on the tray's `DisplayChanged`.
- **Localization** follows Persistent and WSL Monitor: nine locale identities (`Classes/AppLanguages.cs`;
  Canadian ones reuse en-US/fr-FR wording). UI text lives in `Localization/Catalogs/*.json`, keyed by the
  English text and embedded in the exe. Use `UiText.Get("English text")` /
  `UiText.Format("… {{name}} …", ("name", value))` in code and
  `l10n:Localize.Text|Content|Header|ToolTip|…="English text"` (`xmlns:l10n="using:WinUIAppKit.Localization"`)
  in XAML (refreshes live on language change). Never translate logs, settings values, device names or
  setup names; switch reasons are `SwitchReason` keys, translated only in the popup. Add every new key
  to all seven catalogs and run `npm run check:l10n`. The language follows Windows unless
  `UserSettings.Language` is set (General page); don't set `ApplicationLanguages.PrimaryLanguageOverride`
  (the kit resolves "Match Windows" from the package language list; `App` clears one left by 1.0.0.10-11).
  New locales also go in `Package.appxmanifest` `<Resources>` and the Store `listings.json`.
- `build-msix.ps1` strips makepri's `<packaging>` node so scaled images and languages stay in the single
  `resources.pri` (split `resources.*.pri` files only work in a bundle) and fails if a split file appears.
- Namespace `HubDisplaySwitch.Windows` shadows WinRT `Windows.*` — always write `global::Windows...`.
- Settings JSON uses `WhenWritingNull` (not `WhenWritingDefault`): several bools default to true and
  an omitted false would load back as true.
- Settings edits apply and save immediately; call `MainWindow.Engine.SettingsChanged()` after editing setups.
- `SettingsManager` migrates the first (unpackaged, PowerShell/WinForms-era) two-state settings file
  (`Connected`/`Disconnected`) into a default "Desk" setup plus a hub-triggered "TV" setup.
- `RestoreSettings` never throws (falls back to defaults and sets `WasReset`, which opens Settings) and
  writes the file only when it changed. Zero setups is a valid saved state, not "unreadable". The single
  instance is claimed in `App` before settings load, so a second launch never touches the file.
- Paths handed to other processes (Explorer) must use `SettingsManager.PhysicalSettingsDir` (MSIX VFS).
- Startup (kit `StartupRegistration`): packaged = manifest `StartupTask` `HubDisplaySwitchStartup`
  (registered once on first run, never overriding DisabledByUser/policy); unpackaged = HKCU Run with `--silent`.
- Windows doesn't allow display changes before sign-in; the sign-in screen uses the last layout.
- **PowerShell build scripts must be ASCII.**
- Package identity is a **placeholder** (`TechnicallyReal.HubDisplaySwitch`) until the app is reserved
  in Partner Center; then update `Package.appxmanifest` and `windows-build.json` together.

## Building

Source is authoritative in WSL. Read `.codex/docs/windows-development.md` and use the root npm
commands (`npm run verify:desktop`, `build:desktop`, `install:desktop`, `package:desktop`). This PC is
x64: pass `-- --platform x64`. Clone with `--recurse-submodules` (or run `git submodule update --init`)
so `external/promo` and `external/winui-app-kit` are present. The bridge needs PowerShell 7 (`pwsh.exe`) on the WSL PATH. Never
build directly against WSL source with Windows tools, never uninstall to bypass an update error.
App icon + MSIX images come from `HubDisplaySwitchMSIX/generate-msix-images.ps1` (committed output).

`legacy/` holds the original PowerShell watcher scripts for reference only.
