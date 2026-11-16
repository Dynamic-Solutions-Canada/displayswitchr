# Hub Display Switch: Agent Instructions

Hub Display Switch is a resident WinUI 3 tray app for Windows users who move between a desk and a TV: it switches the display layout, refresh rates and default audio device when the USB hub for a keyboard, mouse or controller connects or disconnects. It ships as an MSIX (`HubDisplaySwitchMSIX/`) built from WSL through the shared Windows build bridge. The tray, flyout, settings-file, startup, theme, window-chrome and localization plumbing come from the shared WinUI App Kit submodule at `external/winui-app-kit`, and the "Our other apps" page from the apps-promo submodule at `external/promo`.

## Standards

This repo follows the shared standards at https://github.com/Dynamic-Solutions-Canada/app-standards. Read the index for this stack, https://github.com/Dynamic-Solutions-Canada/app-standards/blob/main/stacks/windows.md, then the topic file for the area you are changing. If `external/standards/` exists, it is the same content pinned for offline use. Where this file differs from the standards, this file wins and says why.

## Architecture

| Area | Files |
|---|---|
| Host (hidden, resident, owns tray + engine) | `MainWindow.xaml(.cs)` |
| Tray flyout (click tray icon) | `Windows/SwitcherWindow.xaml(.cs)` |
| On-screen popup | `Windows/OsdWindow.xaml(.cs)` |
| Settings window + pages | `SettingsWindow.xaml(.cs)`, `Pages/SetupPage`, `Pages/GeneralPage`, `Pages/AboutPage`, `Pages/DetectHubDialog.cs` |
| Setup model | `Models/Setup.cs` |
| Arbitration and switch sequence | `Services/SwitchEngine.cs` |
| Displays (CCD `SetDisplayConfig`, `ChangeDisplaySettingsEx`) | `Services/DisplayConfig.cs` |
| USB presence + change events (WMI) | `Services/UsbDevices.cs` |
| Default audio device (Core Audio + undocumented `IPolicyConfig`) | `Services/AudioDevices.cs` |
| Settings | `ViewModels/UserSettings.cs`, `Classes/Settings/SettingsManager.cs` (kit `JsonSettingsStore`) |
| Tray, flyout window, single instance, startup, theme, window chrome, localization mechanism | `external/winui-app-kit` submodule, `ProjectReference` in the csproj |
| UI P/Invoke | kit `WinUIAppKit.Interop.NativeMethods`, plus the popup's extras in `Classes/NativeMethods.cs` (both via `using static`; never redeclare a kit name) |
| UI text / languages | `Classes/AppLanguages.cs`, `Localization/Catalogs/*.json` (kit `UiText` / `Localize`) |
| Tray icon image | `Classes/TrayIconRenderer.cs` (PNG at `TrayIcon.IconPixelSize`) |
| "Our other apps" page | `external/promo` submodule (shared apps-promo catalog), imported in the csproj |
| MSIX packaging, app icon and MSIX images | `HubDisplaySwitchMSIX/build-msix.ps1`, `HubDisplaySwitchMSIX/generate-msix-images.ps1` (committed output) |

## Behaviour

- **Setup** (`Models/Setup.cs`): name and icon, trigger hubs, displays to turn on (plus main display and mirror), playback device (plus "also for calls"). Exactly one setup is the **default** (devices plugged straight into the PC); the others are triggered by hubs.
- **Arbitration** (`Services/SwitchEngine.cs`): the most recently connected hub's setup wins; when its hub goes, fall back to the previous still-connected hub's setup, then the default. At startup, setups higher in the list win. Manual switches (flyout, tray menu, "Switch now") stick until the desired setup next changes.
- **Switch sequence:** popup, then apply the layout (retried for about 60 s while a TV wakes), then *Settle* (refresh rates and audio until stable; TV HDMI audio appears seconds after its display), then the Done popup, then *Background* re-checks (TVs often drop back to 60 Hz after re-syncing).
- Windows doesn't allow display changes before sign-in; the sign-in screen uses the last layout.

## Conventions and gotchas

- **WinUI App Kit** owns the shared window plumbing; read its README and `AGENTS.md` before touching the tray, menu, flyout, window chrome, startup, theme, settings store or localization. Hub keeps its own identity values in `MainWindow` (tray GUID, mutex, message names, startup task id). Never change them: Windows keys the tray show/hide choice on the GUID. Behaviour Hub needs from the kit goes in as a general option or event in a separate kit commit, never as an edit under `external/`, and WSL Monitor must still build against it.
- The popup must never activate: `WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED`; fade via `SetLayeredWindowAttributes`; re-centred on the tray's `DisplayChanged`.
- **Every user-visible string goes through the catalogs.** UI text lives in `Localization/Catalogs/*.json`, keyed by the English text and embedded in the exe; nine locale identities are in `Classes/AppLanguages.cs` (en-CA and fr-CA reuse en-US and fr-FR wording). Use `UiText.Get("English text")` / `UiText.Format("... {{name}} ...", ("name", value))` in code and `l10n:Localize.Text|Content|Header|ToolTip|...="English text"` (`xmlns:l10n="using:WinUIAppKit.Localization"`) in XAML, which relabels live on a language change. Add every new key to all seven catalogs and run `npm run check:l10n`.
- Never translate logs, settings values, device names or setup names. Switch reasons are `SwitchReason` keys, translated only in the popup.
- The language follows Windows unless `UserSettings.Language` is set (General page). Don't set `ApplicationLanguages.PrimaryLanguageOverride`: the kit resolves "Match Windows" from the package language list, and `App` clears one left by 1.0.0.10 to 1.0.0.11. New locales also go in `Package.appxmanifest` `<Resources>` and the Store `listings.json`.
- `build-msix.ps1` strips makepri's `<packaging>` node so scaled images and languages stay in the single `resources.pri` (split `resources.*.pri` files only work in a bundle) and fails if a split file appears.
- Namespace `HubDisplaySwitch.Windows` shadows WinRT `Windows.*`: always write `global::Windows...`.
- Settings JSON uses `WhenWritingNull`, not `WhenWritingDefault`: several bools default to true, and an omitted false would load back as true.
- Settings edits apply and save immediately; call `MainWindow.Engine.SettingsChanged()` after editing setups.
- `SettingsManager` migrates the first (unpackaged, PowerShell and WinForms era) two-state settings file (`Connected` / `Disconnected`) into a default "Desk" setup plus a hub-triggered "TV" setup.
- `RestoreSettings` never throws (it falls back to defaults and sets `WasReset`, which opens Settings) and writes the file only when it changed. Zero setups is a valid saved state, not "unreadable". The single instance is claimed in `App` before settings load, so a second launch never touches the file.
- Paths handed to other processes (Explorer) must use `SettingsManager.PhysicalSettingsDir` (MSIX VFS).
- Startup (kit `StartupRegistration`): packaged uses the manifest `StartupTask` `HubDisplaySwitchStartup` (registered once on first run, never overriding DisabledByUser or policy); unpackaged uses HKCU Run with `--silent`.
- **PowerShell build scripts must be ASCII.**
- Package identity is a **placeholder** (`TechnicallyReal.HubDisplaySwitch`) until the app is reserved in Partner Center; then update `Package.appxmanifest` and `windows-build.json` together.
- `legacy/` holds the original PowerShell watcher scripts for reference only; they are not built or shipped.

## Commands

Clone with `--recurse-submodules` (or run `git submodule update --init`) so `external/winui-app-kit` and `external/promo` are present. Source is authoritative in WSL; the Windows commands go through `scripts/windows-build`, documented in the [Windows build tools README](https://github.com/Dynamic-Solutions-Canada/windows-build-tools). The build platform defaults to what the build tooling picks for the Windows build host; pass `-- --platform x64` or `-- --platform ARM64` to override it. Never build directly against the WSL source with Windows tools.

| Command | Proves | Does not prove |
| --- | --- | --- |
| `npm run verify:desktop` | The app compiles on Windows (Debug), XAML included | That it packages or runs |
| `npm run build:desktop` | A signed dev package builds | That it installs or runs |
| `npm run install:desktop` | The dev package updates in place and launches | Behaviour; check it in the running app |
| `npm run package:desktop` | An unsigned Store package builds, with every declared language in one `resources.pri` | Signing; that it runs |
| `npm run check:l10n` | Every catalog has the same keys and placeholders | That the text is translated well |

Before finishing a change, run `npm run check:l10n` and `npm run verify:desktop`. Use the `desktop` skill for build and install requests.

## Guidance map

| Guide, doc, or skill | Read or use when | Governs |
| --- | --- | --- |
| [docs/development.md](docs/development.md) | Building, checking, or changing where the app writes data | Build setup, data locations |
| [.agents/guides/ui-conventions.md](.agents/guides/ui-conventions.md) | Any window, page, flyout, tile, popup or card | `HubDisplaySwitch/**/*.xaml(.cs)`, `HubDisplaySwitch/Windows/**`, `HubDisplaySwitch/Pages/**` |
| [WinUI App Kit `AGENTS.md`](external/winui-app-kit/AGENTS.md) | Using the kit's tray, menu, flyout, settings, startup, theme or localization APIs | `external/winui-app-kit/**` (read only) |
| [Windows build tools README](https://github.com/Dynamic-Solutions-Canada/windows-build-tools) | A Windows build, install or packaging problem | `scripts/windows-build`, `windows-build.json` |
| `desktop` skill | Building, verifying or installing locally | Local Windows builds |

## Keeping docs current

| What changed | Update |
| --- | --- |
| A file, service or window added, moved or removed | The Architecture table above |
| Arbitration, the switch sequence or another rule users can see | Behaviour above, `README.md` Features, the manual checks in `docs/development.md` |
| A UI pattern, shared style or control | `.agents/guides/ui-conventions.md` |
| An npm command | The Commands table above, `docs/development.md`, the `desktop` skill, `paseo.json` |
| Where the app writes data, or a new kind of data | The data table in `docs/development.md` |
| A language or catalog rule | Conventions above, `scripts/check-localization.py` |
| A guide or skill moved or removed | The guidance map above and every skill that names it |
