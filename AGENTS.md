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
| Settings | `ViewModels/UserSettings.cs`, `Classes/Settings/SettingsManager.cs` |
| UI P/Invoke | `Classes/NativeMethods.cs` (use `using static`) |
| UI text / languages | `Localization/UiText.cs`, `Localization/SupportedLanguages.cs`, `Localization/Catalogs/*.json`, `Classes/Localize.cs` |
| "Our other apps" page | `external/promo` submodule (shared `apps-promo` catalog), imported in the csproj |

## Conventions / gotchas

- Window patterns copy Little Launcher: tray icon is raw `Shell_NotifyIcon` on the host HWND with a
  stable GUID (re-added on `TaskbarCreated`); right-click menu is `H.NotifyIcon.Core.PopupMenu`; the
  flyout uses `OverlappedPresenter.CreateForContextMenu()` + `DesktopAcrylicBackdrop`, is **parked
  off-screen, never hidden**, and has a 300 ms re-open guard after dismissal.
- The popup must never activate: `WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED`;
  fade via `SetLayeredWindowAttributes`; re-centred on `WM_DISPLAYCHANGE` (forwarded by the host).
- **Localization** follows Persistent: nine locale identities (`en-US`, `en-CA`, `es-ES`, `fr-FR`,
  `fr-CA`, `de-DE`, `zh-CN`, `pt-BR`, `ja-JP`; Canadian ones reuse en-US/fr-FR wording). UI text lives
  in `Localization/Catalogs/*.json`, keyed by the English text and embedded in the exe. Use
  `UiText.Get("English text")` / `UiText.Format("… {{name}} …", ("name", value))` in code and
  `l10n:Localize.Text|Content|Header|ToolTip|…="English text"` in XAML (refreshes live on language
  change). Never translate logs, settings values, device names or setup names; switch reasons are
  `SwitchReason` keys, translated only in the popup. Add every new key to all seven catalogs and run
  `npm run check:l10n`. The language follows Windows unless `UserSettings.Language` is set (General page).
  New locales also go in `Package.appxmanifest` `<Resources>` and the Store `listings.json`.
- Namespace `HubDisplaySwitch.Windows` shadows WinRT `Windows.*` — always write `global::Windows...`.
- Settings JSON uses `WhenWritingNull` (not `WhenWritingDefault`): several bools default to true and
  an omitted false would load back as true.
- Settings edits apply and save immediately; call `MainWindow.Engine.SettingsChanged()` after editing setups.
- `SettingsManager` migrates the first (unpackaged, PowerShell/WinForms-era) two-state settings file
  (`Connected`/`Disconnected`) into a default "Desk" setup plus a hub-triggered "TV" setup.
- Paths handed to other processes (Explorer) must use `SettingsManager.PhysicalSettingsDir` (MSIX VFS).
- Startup: packaged = manifest `StartupTask` `HubDisplaySwitchStartup` (registered once on first run,
  never overriding DisabledByUser/policy); unpackaged = HKCU Run with `--silent`.
- Windows doesn't allow display changes before sign-in; the sign-in screen uses the last layout.
- **PowerShell build scripts must be ASCII.**
- Package identity is a **placeholder** (`TechnicallyReal.HubDisplaySwitch`) until the app is reserved
  in Partner Center; then update `Package.appxmanifest` and `windows-build.json` together.

## Building

Source is authoritative in WSL. Read `.codex/docs/windows-development.md` and use the root npm
commands (`npm run verify:desktop`, `build:desktop`, `install:desktop`, `package:desktop`). This PC is
x64: pass `-- --platform x64`. Clone with `--recurse-submodules` (or run `git submodule update --init`)
so `external/promo` is present. The bridge needs PowerShell 7 (`pwsh.exe`) on the WSL PATH. Never
build directly against WSL source with Windows tools, never uninstall to bypass an update error.
App icon + MSIX images come from `HubDisplaySwitchMSIX/generate-msix-images.ps1` (committed output).

`legacy/` holds the original PowerShell watcher scripts for reference only.
