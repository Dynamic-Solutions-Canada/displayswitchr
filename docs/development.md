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

Packaging runs winui-app-kit's `build/build-msix.ps1`, configured by `windows-build.json` (the project, `msix_dir`, the output folder and the dev and Store identities). `<Version>` in `Directory.Build.props` is the only version source. Dev builds raise the revision only in the staged copy, so the tracked version changes only for a release.

The app icon and MSIX images come from `HubDisplaySwitchMSIX/generate-msix-images.ps1`; its output is committed.

## Checks

`npm run validate` is the Linux gate. It runs, in order:

- `npm run format:check`: every C# project outside `external/` matches the root `.editorconfig`, which is winui-app-kit's file copied unchanged from the tag the app pins; never hand-edit it. `npm run format` rewrites the files.
- `npm run check:l10n`: winui-app-kit's catalog check, configured by the `localization` block in `windows-build.json`. Every key the code and XAML use exists, every catalog has the same keys, no empty values and the same `{{placeholders}}`, no key is unused, the shared `kit.*` labels are the kit's own, and the manifest languages match the family list.
- `npm run check:listing`: the Store listing source and the release notes for the current `<Version>`, checked by winui-app-kit's shared listing check as `store-assets/microsoft/listings.md` describes.
- `npm run check:notices`: `HubDisplaySwitch/THIRD-PARTY-NOTICES.txt` matches the packages the last restore resolved (`format:check` restores first). After adding, removing or updating a package, run `npm run generate:notices` and commit the file with the change. The file ships in the package and the About page links to it (HYG-3); a licence the script has no text for fails the run.
- `npm test`: every portable harness under `tests/`, found by winui-app-kit's `scripts/run-tests` without a hand-kept list (it fails if it finds none). Each has a README saying what it covers and does not prove. Today there are two: the switching policy, and the settings file (loading, upgrading old files, reset) against the kit's real store.

Formatting and tests run through winui-app-kit's `scripts/dotnet`, which uses the .NET SDK pinned in `global.json` when it is installed and otherwise its Docker image, capped at 3 GB (`KIT_DOTNET_MEMORY`). Run them as a non-root user. Run `npm run validate` and `npm run verify:desktop` before finishing a change.

CI runs `npm run validate` on every push to `main` and every pull request, and compiles x64 and ARM64 on `windows-2025` when app source, packaging or build configuration changed (`.github/workflows/ci.yml`). CodeQL scans the C# and the workflows (`.github/workflows/codeql.yml`), and Dependabot proposes NuGet and action updates weekly.

### Manual checks in the installed app

The harness cannot see the screen, the USB hubs or the audio devices. Before a release, install the build (`npm run install:desktop`) and check:

- Connecting a setup's hub switches to it: the popup appears without taking focus, the displays, main display and refresh rates change, and the playback device and microphone follow. Disconnecting it goes back to the previous connected hub's setup, then the default.
- With a TV: the app waits for it to wake and for its HDMI audio, and corrects a drop back to 60 Hz afterwards.
- The tray: a click opens the flyout with one tile per setup and the auto-switch toggle; a tile switches by hand; the right-click menu lists the setups, Pause, Settings, Open logs folder and Exit.
- Settings: add, rename, change the icon of and delete a setup; "Detect by unplugging" finds a hub; start with Windows, switching on start, the popup toggle, theme, and a language change that relabels the windows and tray without reopening them.
- Upgrading over an earlier build keeps every setup and setting, and leaves one `settings.json.v0.bak` beside `settings.json`.
- Reset settings (General page) asks first, then shows one default setup for the displays in use, the default theme and language, and leaves Start with Windows as it was; a `settings.json.<time>.reset` file holds the old settings.
- Exit from the tray menu removes the tray icon and the log ends with "Exit requested".
- About: the icon, the version, "Check for updates" opens the Store's updates page, the five links open (source, privacy policy, support, licence, third-party notices), and "Open logs folder" opens the folder with the current log.
- A settings file that can't be read (replace `settings.json` with text that isn't JSON, then start the app): the settings window opens on Settings with a "Settings were reset" bar naming `settings.json.bad`, and the next start shows no bar.
- Narrator reads the tray icon as "Hub Display Switch" followed by the active setup, not "no setup".
- Light, dark and high contrast.

## Releases

The full procedure is the [`release` skill](../.agents/skills/release/SKILL.md). In short: the notes for the new version are written as user-facing bullets in `release-notes/<version>.md` and one translation per other wording catalog, and approved first; then `<Version>` in `Directory.Build.props` and the notes are committed together as `Bump version to vX.Y.Z`, tagged `vX.Y.Z` (annotated) and pushed. The tag runs "Build and Package" (`.github/workflows/build-msix.yml`): the validate gate, an x64 and ARM64 package build, then a GitHub Release titled `Hub Display Switch X.Y.Z` with the notes as its body. It fails if the notes are missing.

The same tag runs "Publish to Microsoft Store" (`.github/workflows/store-publish.yml`): winui-app-kit's reusable Store workflow runs the gate, builds both architectures, uploads them to Partner Center with each locale's "What's new" from the notes files, and stops at a draft (REL-7). Its summary names the draft ID. Submitting the draft for certification is a separate, deliberate step: run "Publish to Microsoft Store" manually on the same tag with `submit_draft` set to that ID and `no_commit` unchecked. A manual run with `dry_run` checks everything without any Store call or secret.

The app is reserved in Partner Center as `DynamicSolutionsCA.HubDisplaySwitch` (Store ID 9P1S2QJFXZFW), paid at Tier1012, and not yet published. `credential-expiry.yml` fails weekly from 60 days before the Store secret expires (the organisation variable `STORE_SECRET_EXPIRES`). Both Store workflows call the kit's reusable ones; their `uses:` tag must equal the `external/winui-app-kit` pin.

Signed dev builds (`package:desktop:dev`, `install:desktop`) install under the separate dev identity `TechnicallyReal.HubDisplaySwitch`, so they never replace a Store install; `npm run install:desktop -- --store-identity` builds a signed bundle with the Store identity to test an update over one (Windows rejects a plain MSIX over an installed bundle). `npm run package:desktop` keeps the Store identity for upload.

## Where the app stores data

Hub Display Switch collects no telemetry or crash reports. It makes network requests only when the user asks: the "Our other apps" page downloads a public list of apps and their icons from GitHub when it is opened, and "Check for updates" on the About page asks GitHub for the latest release in a copy run without a package (a packaged copy opens the Microsoft Store's updates page instead).

It writes:

| What | Where | After uninstall |
| --- | --- | --- |
| Settings (`settings.json`) | `%AppData%\HubDisplaySwitch\` | Removed, see below |
| Settings backups: `settings.json.v<n>.bak` before an upgrade from schema version n (every install from before schema versions gets one `.v0.bak`), `settings.json.bad` for a file that could not be read, `settings.json.<UTC time>.reset` after Reset settings | `%AppData%\HubDisplaySwitch\` | Removed, see below |
| Logs (`logs.<start time>.txt`, the last few kept) | `%AppData%\HubDisplaySwitch\` | Removed, see below |
| "Our other apps" list and icons | The package's local folder, `PromotedApps\` | Removed with the package |
| Start with Windows | The package's startup task | Removed with the package |
| Display layout, refresh rates and default audio devices | Windows' own display and sound settings | Kept: they are Windows settings, left as the last switch set them |

Every install, including dev installs from `install:desktop`, is an MSIX package, so Windows redirects `%AppData%\HubDisplaySwitch\` into the package's own data folder and deletes it on uninstall. The About page's "Open logs folder" opens the real location. A copy run without a package (straight from a build output folder) writes to the real `%AppData%\HubDisplaySwitch\`, puts the "Our other apps" cache in `%LocalAppData%\TechnicallyReal\PromotedApps\`, and, for start with Windows, adds a `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value; all of these remain until removed by hand. A packaged copy removes such Run values when it starts.

Windows redirects only folders the packaged app creates itself: if `%AppData%\HubDisplaySwitch\` already existed when the package was first installed (from an unpackaged or PowerShell-era copy), the packaged app keeps using the real folder, and it remains after uninstall. Seen on 2026-11-16 on a machine upgraded from the unpackaged builds.

This list has not yet been confirmed against a real uninstall.

There is nothing to back up or export beyond `settings.json`. Reset settings (General page) starts again from the defaults and keeps the old file as a `.reset` backup. To delete all data, uninstall the app.
