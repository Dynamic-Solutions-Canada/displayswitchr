# DisplaySwitchr: Agent Instructions

DisplaySwitchr is a resident WinUI 3 tray app for people who move between a desk and a TV: it switches the display layout, refresh rates and default audio device when the USB hub for a keyboard, mouse or controller connects or disconnects. It ships as an MSIX (`HubDisplaySwitchMSIX/`) built from WSL through the shared Windows build bridge. Shared plumbing comes from the WinUI App Kit (`external/winui-app-kit`) and the "Our other apps" page from apps-promo (`external/promo`).

## Standards

This repo follows the shared standards at https://github.com/Dynamic-Solutions-Canada/app-standards. Read the index for this stack, https://github.com/Dynamic-Solutions-Canada/app-standards/blob/main/stacks/windows.md, then the topic file for the area you are changing. If `external/standards/` exists, it is the same content pinned for offline use. Where this file differs from the standards, this file wins and says why.

## Architecture

| Area | Files |
|---|---|
| Startup order | `App.xaml.cs` (kit `AppBootstrap`) |
| Host (hidden, resident, owns tray + engine, exit steps) | `MainWindow.xaml(.cs)` (kit `HostWindow`) |
| Tray flyout (click tray icon) | `Windows/SwitcherWindow.xaml(.cs)` |
| On-screen popup | `Windows/OsdWindow.xaml(.cs)` (kit `OsdController`) |
| Settings window (kit `SettingsShell`) + pages | `SettingsWindow.xaml(.cs)`, `Pages/SetupPage`, `Pages/GeneralPage` (kit Startup, Language and Theme rows), `Pages/DetectHubDialog.cs` |
| Setup model | `Models/Setup.cs` |
| Arbitration and switch sequence | `Services/SwitchEngine.cs` |
| Displays (CCD `SetDisplayConfig`, `ChangeDisplaySettingsEx`) | `Services/DisplayConfig.cs` |
| USB presence + change events (WMI) | `Services/UsbDevices.cs`; the device record and hub matching in `Services/UsbDevice.cs` |
| Default audio device (Core Audio + undocumented `IPolicyConfig`) | `Services/AudioDevices.cs` |
| Settings, schema steps, reset | `ViewModels/UserSettings.cs`, `Classes/Settings/SettingsManager.cs` (kit versioned `JsonSettingsStore`), `Classes/Settings/SettingsMigrations.cs` |
| Shared plumbing: tray, host, dialogs, single instance, startup, theme, chrome, logging, localization | `external/winui-app-kit` submodule, `ProjectReference` in the csproj |
| UI P/Invoke | kit `WinUIAppKit.Interop.NativeMethods` (via `using static`; an app-side `NativeMethods` must never redeclare a kit name) |
| UI text / languages | `Classes/AppLanguages.cs`, `Localization/Catalogs/*.json` (kit `UiText` / `Localize`) |
| About page (kit `AboutPage`), repository, privacy policy and update check | `Classes/AboutContent.cs` |
| Tray icon artwork | `Classes/TrayIconRenderer.cs` (kit `TrayIconRasterizer`) |
| "Our other apps" page | `external/promo` submodule (shared apps-promo catalog), imported in the csproj |

## Behaviour

- **Setup** (`Models/Setup.cs`): trigger hubs, displays (main, mirror, refresh rates) and playback device (and calls). Exactly one setup is the **default** (devices plugged straight into the PC); hubs trigger the others.
- **Arbitration** (`Services/SwitchEngine.cs`): the most recently connected hub's setup wins; when its hub goes, fall back to the previous still-connected hub's setup, then the default. At startup, setups higher in the list win. Manual switches (flyout, tray menu, "Switch now") stick until the desired setup next changes.
- **Switch sequence:** popup, then the layout (retried for about 60 s while a TV wakes), then *Settle* (refresh rates and audio until stable; TV HDMI audio appears after its display), then the Done popup, then *Background* re-checks (TVs often drop back to 60 Hz).
- Windows doesn't allow display changes before sign-in; the sign-in screen uses the last layout.

## Conventions and gotchas

- **WinUI App Kit** owns the shared plumbing and build configuration; its README "Gotchas" apply here and are not repeated. The app's identity values (tray GUID, mutex, message names, startup task id) live in `MainWindow` and never change. Behaviour the app needs from the kit goes into the kit's own repo as a general option or event, never as an edit under `external/`.
- **The popup must never activate:** it is driven only through the kit's `OsdController`; details in [.agents/guides/ui-conventions.md](.agents/guides/ui-conventions.md).
- **Every user-visible string goes through the catalogs** (`UiText` in code, `l10n:Localize.*` in XAML, every key in all seven catalogs, then `npm run check:l10n`). Never translate logs, settings values, device names or setup names. Details in [.agents/guides/localization.md](.agents/guides/localization.md).
- **Packaging** is winui-app-kit's `build/build-msix.ps1`, driven by `windows-build.json` and `HubDisplaySwitchMSIX/Package.appxmanifest`; it keeps every language in one `resources.pri`, and the product name is never translated. Icons come from `assets/brand/` through `npm run icons` (committed output, never edited by hand). Build files, icons and notices are covered in `docs/development.md`.
- **Settings:** read [.agents/guides/settings.md](.agents/guides/settings.md) before changing `UserSettings` or `SettingsManager`; a change to the file's shape is a new step in `SettingsMigrations`.
- **PowerShell build scripts must be ASCII.**
- **Product name:** users see "DisplaySwitchr", one word (history in `store-assets/microsoft/listings.md`). The package identities, Store ID, exe, project and namespace names, data folder `HubDisplaySwitch`, mutex, tray GUID, startup task id and host window title "Hub Display Switch Host" keep the old name so installs and settings carry over; never rename them. Release notes from before the rename stay as written.
- **Package identities** (Store `DynamicSolutionsCA.HubDisplaySwitch`, dev `TechnicallyReal.HubDisplaySwitch`) live in the manifest and `windows-build.json`; change both together. Details in `docs/development.md`.

## Commands

Clone with submodules (`git submodule update --init`). Windows commands go through winui-app-kit's `scripts/windows-build`, never Windows tools run against the WSL source; setup and the platform override are in `docs/development.md`.

| Command | Proves | Does not prove |
| --- | --- | --- |
| `npm run verify:desktop` | The app compiles on Windows (Debug), XAML included | That it packages or runs |
| `npm run package:desktop:dev` | A signed dev package builds | That it installs or runs |
| `npm run install:desktop` | The dev package updates in place and launches | Behaviour; check it in the running app |
| `npm run package:desktop` | An unsigned Store package builds, with every declared language in one `resources.pri` | Signing; that it runs |
| `npm test` | Every portable harness under `tests/` passes on Linux: the switching policy and the settings file | Anything needing Windows or real devices |
| `npm run validate` | The Linux gate: format, gitignore, icon, l10n, listing, notices, screenshot and hero checks, then `npm test` | A Windows compile; also run `verify:desktop` |
| `npm run format` | Rewrites C# (not `external/`) to the root `.editorconfig`, winui-app-kit's copied unchanged | Behaviour; review the diff |

`docs/development.md` covers each check `validate` runs and the generators (`icons`, `hero`, `shots:render`, `generate:notices`). Before finishing a change, run `npm run validate` and `npm run verify:desktop`; use the `desktop` skill for builds and installs.

## Release

Use the `release` skill. `<Version>` in `Directory.Build.props` is the only version source. A `v*` tag creates a notes-only GitHub Release (`build-msix.yml`; a paid Store app attaches no package) and uploads a Store draft (`store-publish.yml`); submitting the draft is a separate manual run, described in `docs/development.md`.

## Guidance map

| Guide, doc, or skill | Read or use when | Governs |
| --- | --- | --- |
| [docs/development.md](docs/development.md) | Building, checking, or changing where the app writes data | Build setup, data locations |
| [.agents/guides/ui-conventions.md](.agents/guides/ui-conventions.md) | Any window, page, flyout, tile, popup or card | `HubDisplaySwitch/**/*.xaml(.cs)`, `HubDisplaySwitch/Windows/**`, `HubDisplaySwitch/Pages/**` |
| [.agents/guides/localization.md](.agents/guides/localization.md) | Adding or changing text, a language, or the language setting | `HubDisplaySwitch/Localization/**`, `Classes/AppLanguages.cs`, `release-notes/**` |
| [.agents/guides/settings.md](.agents/guides/settings.md) | Changing a setting, the settings file or how it loads | `HubDisplaySwitch/ViewModels/UserSettings.cs`, `HubDisplaySwitch/Classes/Settings/**` |
| [WinUI App Kit README](external/winui-app-kit/README.md), including its Gotchas | Using any kit API, shared XAML or build file | `external/winui-app-kit/**` (read only) |
| [Windows build tools README](https://github.com/Dynamic-Solutions-Canada/windows-build-tools) | A Windows build, install or packaging problem | `windows-build.json` |
| `desktop` skill | Building, verifying or installing locally | Local Windows builds |
| `release` skill | Releasing a version | `release-notes/**`, `<Version>`, `build-msix.yml`, `store-publish.yml` |
| [store-assets/microsoft/listings.md](store-assets/microsoft/listings.md), [store-assets/README.md](store-assets/README.md) | Changing the Store listing, a claim it makes, or the screenshots | `store-assets/**` |

## Keeping docs current

| What changed | Update |
| --- | --- |
| A file, service or window added, moved or removed | The Architecture table above |
| Arbitration, the switch sequence or another rule users can see | Behaviour above, `README.md` Features, the manual checks in `docs/development.md`, the policy tests |
| A UI pattern, shared style or control | `.agents/guides/ui-conventions.md` |
| An npm command | The Commands table above, `docs/development.md`, the `desktop` skill, and `.github/workflows/ci.yml` if `validate` or the Windows compile changes |
| App source, packaging or build configuration moved to a new top-level path | The Windows path filter in `.github/workflows/ci.yml` |
| Where the app writes data, or a new kind of data | The data table in `docs/development.md` |
| How settings load, save or migrate | `.agents/guides/settings.md`, `tests/HubDisplaySwitch.Settings.Tests/` |
| winui-app-kit pin bumped | The `uses:` tag in `store-publish.yml` and `credential-expiry.yml`; the kit's `.editorconfig` and `build/shared.gitignore` (the end of `.gitignore`) copied unchanged, then `npm run format` |
| Behaviour a listing claim or screenshot shows | `listings.json` and "Claims to keep true" in `listings.md`; recapture affected shots as `store-assets/README.md` describes, approved before committing |
| A language or catalog rule | `.agents/guides/localization.md`, the `localization` block in `windows-build.json` |
| A package added, removed or updated | `npm run generate:notices`, and commit the notices with the change |
| A guide or skill moved or removed | The guidance map above and every skill that names it |
