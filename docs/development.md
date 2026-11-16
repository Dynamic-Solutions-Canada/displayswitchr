> **Scope:** Building, checking and installing Hub Display Switch, and where it stores data.
> **Governs:** `Directory.Build.props`, `package.json`, `windows-build.json`, `scripts/**`, `HubDisplaySwitchMSIX/**`

# Hub Display Switch development

## Prerequisites

- WSL with this repository cloned inside it, including submodules: clone with `--recurse-submodules`, or run `git submodule update --init` (the app builds on `external/winui-app-kit` and `external/promo`).
- [lefthook](https://lefthook.dev) for the git hooks in `lefthook.yml`: run `lefthook install` once after cloning.
- Node.js for the root `npm run` commands, Python 3 for the catalog check, and either the .NET SDK pinned in `global.json` or Docker for formatting and tests.
- The [shared Windows build tools](https://github.com/Dynamic-Solutions-Canada/windows-build-tools), which run the Windows build from WSL and document their own Windows-side requirements, the disposable Windows TEMP copy and the local build VM.

## Build and install

Run these from the repository root in WSL. The build platform defaults to the Windows build host's architecture; add `-- --platform x64` or `-- --platform ARM64` to override it.

| Command | What it does | What it proves |
| --- | --- | --- |
| `npm run verify:desktop` | Debug compile on Windows | The app compiles, XAML included |
| `npm run package:desktop:dev` | Signed dev MSIX | The package builds; not that it installs or runs |
| `npm run install:desktop` | Builds the dev MSIX, updates the installed copy in place and launches it | That it installs and starts; check behaviour in the app |
| `npm run package:desktop` | Unsigned Store MSIX | The Store package builds, with every declared language in one `resources.pri` |

`npm run install:desktop -- --skip-build` reinstalls the last successful dev package, and `-- --no-launch` installs without launching. Never uninstall to get past an update error.

`<Version>` in `Directory.Build.props` is the only version source. Dev builds raise the revision only in the staged copy, so the tracked version changes only for a release.

The app icon and MSIX images come from `HubDisplaySwitchMSIX/generate-msix-images.ps1`; its output is committed.

## Checks

`npm run validate` is the Linux gate. It runs, in order:

- `npm run format:check`: every C# project outside `external/` matches the root `.editorconfig`, which is winui-app-kit's file copied unchanged from the tag the app pins; never hand-edit it. `npm run format` rewrites the files.
- `npm run check:l10n`: every key the code and XAML use exists in the English catalog, and every other catalog has exactly the same keys, no empty values and the same `{{placeholders}}`. It does not check the languages declared in the package manifest.
- `npm test`: every portable harness under `tests/`, found by `scripts/run-tests` without a hand-kept list. Each has a README saying what it covers and does not prove. Today there is one, for the switching policy.

Formatting and tests run through `scripts/dotnet`, which uses the .NET SDK pinned in `global.json` when it is installed and otherwise its Docker image, capped at 3 GB (`KIT_DOTNET_MEMORY`). Run them as a non-root user. Run `npm run validate` and `npm run verify:desktop` before finishing a change.

CI runs `npm run validate` on every push to `main` and every pull request, and compiles x64 and ARM64 on `windows-2025` when app source, packaging or build configuration changed (`.github/workflows/ci.yml`). CodeQL scans the C# and the workflows (`.github/workflows/codeql.yml`), and Dependabot proposes NuGet and action updates weekly.

### Manual checks in the installed app

The harness cannot see the screen, the USB hubs or the audio devices. Before a release, install the build (`npm run install:desktop`) and check:

- Connecting a setup's hub switches to it: the popup appears without taking focus, the displays, main display and refresh rates change, and the playback device and microphone follow. Disconnecting it goes back to the previous connected hub's setup, then the default.
- With a TV: the app waits for it to wake and for its HDMI audio, and corrects a drop back to 60 Hz afterwards.
- The tray: a click opens the flyout with one tile per setup and the auto-switch toggle; a tile switches by hand; the right-click menu lists the setups, Pause, Settings, Open logs folder and Exit.
- Settings: add, rename, change the icon of and delete a setup; "Detect by unplugging" finds a hub; start with Windows, switching on start, the popup toggle, theme, and a language change that relabels the windows and tray without reopening them.
- About: the version, and "Open logs folder" opens the folder with the current log.
- Light, dark and high contrast.

## Releases

There is no release process yet. The app is not yet in the Microsoft Store, and its package identity is a placeholder until the name is reserved.

## Where the app stores data

Hub Display Switch collects no telemetry or crash reports. The only network request is the "Our other apps" page, which downloads a public list of apps and their icons from GitHub when it is opened.

It writes:

| What | Where | After uninstall |
| --- | --- | --- |
| Settings (`settings.json`) | `%AppData%\HubDisplaySwitch\` | Removed, see below |
| A backup of settings from the first, two-state version (`settings.json.v1.bak`), made once when they are migrated | `%AppData%\HubDisplaySwitch\` | Removed, see below |
| Logs (`logs.<start time>.txt`, the last few kept) | `%AppData%\HubDisplaySwitch\` | Removed, see below |
| "Our other apps" list and icons | The package's local folder, `PromotedApps\` | Removed with the package |
| Start with Windows | The package's startup task | Removed with the package |
| Display layout, refresh rates and default audio devices | Windows' own display and sound settings | Kept: they are Windows settings, left as the last switch set them |

Every install, including dev installs from `install:desktop`, is an MSIX package, so Windows redirects `%AppData%\HubDisplaySwitch\` into the package's own data folder and deletes it on uninstall. The About page's "Open logs folder" opens the real location. A copy run without a package (straight from a build output folder) writes to the real `%AppData%\HubDisplaySwitch\`, puts the "Our other apps" cache in `%LocalAppData%\TechnicallyReal\PromotedApps\`, and, for start with Windows, adds a `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value; all of these remain until removed by hand. A packaged copy removes such Run values when it starts.

This list has not yet been confirmed against a real uninstall.

There is nothing to back up or export beyond `settings.json`. To delete all data, uninstall the app.
