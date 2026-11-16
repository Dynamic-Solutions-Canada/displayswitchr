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
| USB presence + change events (WMI) | `Services/UsbDevices.cs`; the device record and hub matching in `Services/UsbDevice.cs` |
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
- **Every user-visible string goes through the catalogs** (`Localization/Catalogs/*.json`, keyed by the English text): `UiText` in code, `l10n:Localize.*` in XAML, every key in all seven catalogs, then `npm run check:l10n`. Never translate logs, settings values, device names or setup names. Details in [.agents/guides/localization.md](.agents/guides/localization.md).
- `build-msix.ps1` strips makepri's `<packaging>` node so scaled images and languages stay in the single `resources.pri` (split `resources.*.pri` files only work in a bundle) and fails if a split file appears.
- Namespace `HubDisplaySwitch.Windows` shadows WinRT `Windows.*`: always write `global::Windows...`.
- **Settings** save on every edit; call `MainWindow.Engine.SettingsChanged()` after editing setups. Loading never throws, serialisation must keep `WhenWritingNull`, and paths handed to Explorer use `SettingsManager.PhysicalSettingsDir`. Read [.agents/guides/settings.md](.agents/guides/settings.md) before changing `UserSettings` or `SettingsManager`.
- Startup (kit `StartupRegistration`): packaged uses the manifest `StartupTask` `HubDisplaySwitchStartup` (registered once on first run, never overriding DisabledByUser or policy); unpackaged uses HKCU Run with `--silent`.
- **PowerShell build scripts must be ASCII.**
- Package identity is a **placeholder** (`TechnicallyReal.HubDisplaySwitch`) until the app is reserved in Partner Center; then update `Package.appxmanifest` and `windows-build.json` together.

## Commands

Clone with `--recurse-submodules` (or run `git submodule update --init`) so `external/winui-app-kit` and `external/promo` are present. Source is authoritative in WSL; the Windows commands go through `scripts/windows-build`, documented in the [Windows build tools README](https://github.com/Dynamic-Solutions-Canada/windows-build-tools). The build platform defaults to what the build tooling picks for the Windows build host; pass `-- --platform x64` or `-- --platform ARM64` to override it. Never build directly against the WSL source with Windows tools.

| Command | Proves | Does not prove |
| --- | --- | --- |
| `npm run verify:desktop` | The app compiles on Windows (Debug), XAML included | That it packages or runs |
| `npm run package:desktop:dev` | A signed dev package builds | That it installs or runs |
| `npm run install:desktop` | The dev package updates in place and launches | Behaviour; check it in the running app |
| `npm run package:desktop` | An unsigned Store package builds, with every declared language in one `resources.pri` | Signing; that it runs |
| `npm run check:l10n` | Every catalog has the same keys and placeholders | That the text is translated well |
| `npm run check:listing` | The Store listing source and the current version's release notes have every locale and field within the store limits, and the README tagline matches the listing | That the text reads well, or the live listing |
| `npm test` | Every portable harness under `tests/` passes on Linux (host .NET SDK or Docker): today the switching policy | Anything that needs Windows, real displays, audio or USB devices; each harness README says what it leaves out |
| `npm run validate` | The whole Linux gate passes: `format:check`, `check:l10n`, `check:listing`, then `npm test` | A Windows compile; also run `verify:desktop` |
| `npm run format` | Rewrites every C# project (not `external/`) to the root `.editorconfig`, which is winui-app-kit's copied unchanged | Behaviour; review the diff |

Before finishing a change, run `npm run validate` and `npm run verify:desktop`. Use the `desktop` skill for build and install requests.

## Release

Releases follow the `release` skill: approved notes in `release-notes/`, a `Bump version to vX.Y.Z` commit, an annotated `vX.Y.Z` tag, and the "Build and Package" workflow, which creates the GitHub Release. `<Version>` in `Directory.Build.props` is the only version source.

## Guidance map

| Guide, doc, or skill | Read or use when | Governs |
| --- | --- | --- |
| [docs/development.md](docs/development.md) | Building, checking, or changing where the app writes data | Build setup, data locations |
| [.agents/guides/ui-conventions.md](.agents/guides/ui-conventions.md) | Any window, page, flyout, tile, popup or card | `HubDisplaySwitch/**/*.xaml(.cs)`, `HubDisplaySwitch/Windows/**`, `HubDisplaySwitch/Pages/**` |
| [.agents/guides/localization.md](.agents/guides/localization.md) | Adding or changing text, a language, or the language setting | `HubDisplaySwitch/Localization/**`, `Classes/AppLanguages.cs`, `release-notes/**` |
| [.agents/guides/settings.md](.agents/guides/settings.md) | Changing a setting, the settings file or how it loads | `HubDisplaySwitch/ViewModels/UserSettings.cs`, `HubDisplaySwitch/Classes/Settings/**` |
| [WinUI App Kit `AGENTS.md`](external/winui-app-kit/AGENTS.md) | Using the kit's tray, menu, flyout, settings, startup, theme or localization APIs | `external/winui-app-kit/**` (read only) |
| [Windows build tools README](https://github.com/Dynamic-Solutions-Canada/windows-build-tools) | A Windows build, install or packaging problem | `scripts/windows-build`, `windows-build.json` |
| `desktop` skill | Building, verifying or installing locally | Local Windows builds |
| `release` skill | Releasing a version | `release-notes/**`, `Directory.Build.props` `<Version>`, `.github/workflows/build-msix.yml` |
| [store-assets/microsoft/listings.md](store-assets/microsoft/listings.md) | Changing the Store listing or a claim it makes | `store-assets/microsoft/**` |

## Keeping docs current

| What changed | Update |
| --- | --- |
| A file, service or window added, moved or removed | The Architecture table above |
| Arbitration, the switch sequence or another rule users can see | Behaviour above, `README.md` Features, the manual checks in `docs/development.md`, the scenarios and README in `tests/HubDisplaySwitch.Policy.Tests/` |
| A test harness added or changed | Its README; `npm test` finds it by itself |
| A UI pattern, shared style or control | `.agents/guides/ui-conventions.md` |
| An npm command | The Commands table above, `docs/development.md`, the `desktop` skill, `paseo.json`, and `.github/workflows/ci.yml` if `validate` or the Windows compile changes |
| App source, packaging or build configuration moved to a new top-level path | The Windows path filter in `.github/workflows/ci.yml` |
| Where the app writes data, or a new kind of data | The data table in `docs/development.md` |
| How settings load, save or migrate | `.agents/guides/settings.md` |
| winui-app-kit pin bumped | Copy the kit's `.editorconfig` and `build/shared.gitignore` (the last part of `.gitignore`) at that tag unchanged |
| Behaviour a Store listing claim describes (privacy, languages, sign-in, audio) | The listing in `store-assets/microsoft/listings.json` and its "Claims to keep true" in `listings.md` |
| The README tagline or the en-US short description | Both together; `npm run check:listing` compares them |
| A language or catalog rule | `.agents/guides/localization.md`, `scripts/check-localization.py` |
| A guide or skill moved or removed | The guidance map above and every skill that names it |
