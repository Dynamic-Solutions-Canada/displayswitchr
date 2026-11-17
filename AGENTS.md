# Hub Display Switch: Agent Instructions

Hub Display Switch is a resident WinUI 3 tray app for Windows users who move between a desk and a TV: it switches the display layout, refresh rates and default audio device when the USB hub for a keyboard, mouse or controller connects or disconnects. It ships as an MSIX (`HubDisplaySwitchMSIX/`) built from WSL through the shared Windows build bridge. The tray, flyout, settings-file, startup, theme, window-chrome and localization plumbing come from the shared WinUI App Kit submodule at `external/winui-app-kit`, and the "Our other apps" page from the apps-promo submodule at `external/promo`.

## Standards

This repo follows the shared standards at https://github.com/Dynamic-Solutions-Canada/app-standards. Read the index for this stack, https://github.com/Dynamic-Solutions-Canada/app-standards/blob/main/stacks/windows.md, then the topic file for the area you are changing. If `external/standards/` exists, it is the same content pinned for offline use. Where this file differs from the standards, this file wins and says why.

## Architecture

| Area | Files |
|---|---|
| Startup order (crash logging, single instance, settings, host) | `App.xaml.cs` (kit `AppBootstrap`) |
| Host (hidden, resident, owns tray + engine, exit steps) | `MainWindow.xaml(.cs)` (kit `HostWindow`) |
| Tray flyout (click tray icon) | `Windows/SwitcherWindow.xaml(.cs)` |
| On-screen popup | `Windows/OsdWindow.xaml(.cs)` |
| Settings window + pages | `SettingsWindow.xaml(.cs)`, `Pages/SetupPage`, `Pages/GeneralPage` (kit Startup, Language and Theme rows), `Pages/DetectHubDialog.cs` |
| Setup model | `Models/Setup.cs` |
| Arbitration and switch sequence | `Services/SwitchEngine.cs` |
| Displays (CCD `SetDisplayConfig`, `ChangeDisplaySettingsEx`) | `Services/DisplayConfig.cs` |
| USB presence + change events (WMI) | `Services/UsbDevices.cs`; the device record and hub matching in `Services/UsbDevice.cs` |
| Default audio device (Core Audio + undocumented `IPolicyConfig`) | `Services/AudioDevices.cs` |
| Settings, schema steps, reset | `ViewModels/UserSettings.cs`, `Classes/Settings/SettingsManager.cs` (kit versioned `JsonSettingsStore`), `Classes/Settings/SettingsMigrations.cs` |
| Tray, flyout window, host, confirm dialog, single instance, startup, theme, window chrome, logging, localization mechanism | `external/winui-app-kit` submodule, `ProjectReference` in the csproj |
| Shared build settings, package versions, NLog config | `Directory.Build.props`, `Directory.Build.targets` and `Directory.Packages.props` import the kit's `build/` files; the app adds only `<Version>` and `System.Management` |
| UI P/Invoke | kit `WinUIAppKit.Interop.NativeMethods`, plus the popup's extras in `Classes/NativeMethods.cs` (both via `using static`; never redeclare a kit name) |
| UI text / languages | `Classes/AppLanguages.cs`, `Localization/Catalogs/*.json` (kit `UiText` / `Localize`) |
| About page (kit `AboutPage`), repository, privacy policy and update check | `Classes/AboutContent.cs` |
| Third-party notices (generated, shipped in the package) | `scripts/generate-notices.py` writes `HubDisplaySwitch/THIRD-PARTY-NOTICES.txt` |
| Tray icon artwork | `Classes/TrayIconRenderer.cs` (kit `TrayIconRasterizer`) |
| "Our other apps" page | `external/promo` submodule (shared apps-promo catalog), imported in the csproj |
| MSIX packaging, app icon and MSIX images | winui-app-kit's `build/build-msix.ps1`, driven by `windows-build.json`; `HubDisplaySwitchMSIX/Package.appxmanifest`, `HubDisplaySwitchMSIX/generate-msix-images.ps1` (committed output) |

## Behaviour

- **Setup** (`Models/Setup.cs`): name and icon, trigger hubs, displays to turn on (plus main display and mirror), playback device (plus "also for calls"). Exactly one setup is the **default** (devices plugged straight into the PC); the others are triggered by hubs.
- **Arbitration** (`Services/SwitchEngine.cs`): the most recently connected hub's setup wins; when its hub goes, fall back to the previous still-connected hub's setup, then the default. At startup, setups higher in the list win. Manual switches (flyout, tray menu, "Switch now") stick until the desired setup next changes.
- **Switch sequence:** popup, then apply the layout (retried for about 60 s while a TV wakes), then *Settle* (refresh rates and audio until stable; TV HDMI audio appears seconds after its display), then the Done popup, then *Background* re-checks (TVs often drop back to 60 Hz after re-syncing).
- Windows doesn't allow display changes before sign-in; the sign-in screen uses the last layout.

## Conventions and gotchas

- **WinUI App Kit** owns the shared plumbing and build configuration; its README "Gotchas" section applies here (identity values, single instance before settings, the `global::Windows` namespace shadowing, flyouts, paths for other processes, settings serialisation, startup, interface text) and is not repeated. Hub's identity values (tray GUID, mutex, message names, startup task id) live in `MainWindow` and never change. Behaviour Hub needs from the kit goes in as a general option or event in the kit's own repo, never as an edit under `external/`.
- The popup must never activate: `WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED`; fade via `SetLayeredWindowAttributes`; re-centred on the tray's `DisplayChanged`.
- **Every user-visible string goes through the catalogs** (`Localization/Catalogs/*.json`, keyed by the English text): `UiText` in code, `l10n:Localize.*` in XAML, every key in all seven catalogs, then `npm run check:l10n`. Never translate logs, settings values, device names or setup names. Details in [.agents/guides/localization.md](.agents/guides/localization.md).
- **Packaging** is winui-app-kit's `build/build-msix.ps1`, driven by `windows-build.json` (`msix_dir`, the identities). It keeps every scaled image and language in one `resources.pri` and fails if makepri splits it. The manifest's display name and description are English text, not `ms-resource` strings.
- **Settings** save on every edit; call `MainWindow.Engine.SettingsChanged()` after editing setups. A change to the file's shape is a new step in `SettingsMigrations`. Read [.agents/guides/settings.md](.agents/guides/settings.md) before changing `UserSettings` or `SettingsManager`.
- **PowerShell build scripts must be ASCII.**
- **Package identities:** the manifest carries the Store identity (`DynamicSolutionsCA.HubDisplaySwitch`, Store ID 9P1S2QJFXZFW). Signed dev builds are stamped with the separate dev identity `TechnicallyReal.HubDisplaySwitch` (`dev_identity` in `windows-build.json`) by the kit's packaging script (DIST-3); `--store-identity` builds a signed bundle with the Store identity for testing over a Store install. The Store identity is in the manifest and in `windows-build.json`; change both together.

## Commands

Clone with `--recurse-submodules` (or run `git submodule update --init`) so `external/winui-app-kit` and `external/promo` are present. The Windows commands go through winui-app-kit's `scripts/windows-build` ([Windows build tools README](https://github.com/Dynamic-Solutions-Canada/windows-build-tools)), never Windows tools run against the WSL source. The platform defaults to the build host's; `-- --platform x64` or `ARM64` overrides it.

| Command | Proves | Does not prove |
| --- | --- | --- |
| `npm run verify:desktop` | The app compiles on Windows (Debug), XAML included | That it packages or runs |
| `npm run package:desktop:dev` | A signed dev package builds | That it installs or runs |
| `npm run install:desktop` | The dev package updates in place and launches | Behaviour; check it in the running app |
| `npm run package:desktop` | An unsigned Store package builds, with every declared language in one `resources.pri` | Signing; that it runs |
| `npm run check:l10n` | winui-app-kit's catalog check: every key the code uses exists, every catalog has the same keys and placeholders, `kit.*` keys are the kit's, and the manifest languages match the family list | That the text is translated well |
| `npm run check:listing` | The Store listing and the current version's notes are complete and within store limits; the README tagline matches | That the text reads well, or the live listing |
| `npm run check:notices` | `HubDisplaySwitch/THIRD-PARTY-NOTICES.txt` matches the restored packages (`npm run generate:notices` rewrites it) | That a licence was read correctly; review new entries |
| `npm test` | Every portable harness under `tests/` passes on Linux: the switching policy and the settings file | Anything needing Windows or real devices; see each harness README |
| `npm run check:gitignore` | `.gitignore` ends with winui-app-kit's shared block, unchanged | Anything else in the file |
| `npm run validate` | The whole Linux gate passes: `format:check`, `check:gitignore`, `check:l10n`, `check:listing`, `check:notices`, then `npm test` | A Windows compile; also run `verify:desktop` |
| `npm run format` | Rewrites C# (not `external/`) to the root `.editorconfig`, winui-app-kit's copied unchanged | Behaviour; review the diff |

Before finishing a change, run `npm run validate` and `npm run verify:desktop`. Use the `desktop` skill for build and install requests.

## Release

Releases follow the `release` skill. `<Version>` in `Directory.Build.props` is the only version source.

## Guidance map

| Guide, doc, or skill | Read or use when | Governs |
| --- | --- | --- |
| [docs/development.md](docs/development.md) | Building, checking, or changing where the app writes data | Build setup, data locations |
| [.agents/guides/ui-conventions.md](.agents/guides/ui-conventions.md) | Any window, page, flyout, tile, popup or card | `HubDisplaySwitch/**/*.xaml(.cs)`, `HubDisplaySwitch/Windows/**`, `HubDisplaySwitch/Pages/**` |
| [.agents/guides/localization.md](.agents/guides/localization.md) | Adding or changing text, a language, or the language setting | `HubDisplaySwitch/Localization/**`, `Classes/AppLanguages.cs`, `release-notes/**` |
| [.agents/guides/settings.md](.agents/guides/settings.md) | Changing a setting, the settings file or how it loads | `HubDisplaySwitch/ViewModels/UserSettings.cs`, `HubDisplaySwitch/Classes/Settings/**` |
| [WinUI App Kit README](external/winui-app-kit/README.md), including its Gotchas | Using the kit's tray, host, flyout, dialogs, settings store, startup, theme, logging or localization, or its build files | `external/winui-app-kit/**` (read only) |
| [Windows build tools README](https://github.com/Dynamic-Solutions-Canada/windows-build-tools) | A Windows build, install or packaging problem | `windows-build.json` |
| `desktop` skill | Building, verifying or installing locally | Local Windows builds |
| `release` skill | Releasing a version | `release-notes/**`, `Directory.Build.props` `<Version>`, `.github/workflows/build-msix.yml` |
| [store-assets/microsoft/listings.md](store-assets/microsoft/listings.md) | Changing the Store listing or a claim it makes | `store-assets/microsoft/**` |

## Keeping docs current

| What changed | Update |
| --- | --- |
| A file, service or window added, moved or removed | The Architecture table above |
| Arbitration, the switch sequence or another rule users can see | Behaviour above, `README.md` Features, the manual checks in `docs/development.md`, `tests/HubDisplaySwitch.Policy.Tests/` |
| A test harness added or changed | Its README; `npm test` finds it by itself |
| A UI pattern, shared style or control | `.agents/guides/ui-conventions.md` |
| An npm command | The Commands table above, `docs/development.md`, the `desktop` skill, `paseo.json`, and `.github/workflows/ci.yml` if `validate` or the Windows compile changes |
| App source, packaging or build configuration moved to a new top-level path | The Windows path filter in `.github/workflows/ci.yml` |
| Where the app writes data, or a new kind of data | The data table in `docs/development.md` |
| How settings load, save or migrate | `.agents/guides/settings.md`, `tests/HubDisplaySwitch.Settings.Tests/` |
| winui-app-kit pin bumped | Copy the kit's `.editorconfig` and `build/shared.gitignore` (the last part of `.gitignore`) at that tag unchanged, then `npm run format` |
| Behaviour a Store listing claim describes | `store-assets/microsoft/listings.json` and "Claims to keep true" in `listings.md` |
| The README tagline or the en-US short description | Both together; `npm run check:listing` compares them |
| A language or catalog rule | `.agents/guides/localization.md`, the `localization` block in `windows-build.json` |
| A package added, removed or updated | `npm run generate:notices`, and commit the notices with the change |
| A guide or skill moved or removed | The guidance map above and every skill that names it |
