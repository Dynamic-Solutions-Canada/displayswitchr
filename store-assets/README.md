# Store assets

| Folder | Holds |
| --- | --- |
| `microsoft/listings.json`, `listings.md` | The Store listing text in every locale, and the claims it makes (see `listings.md`) |
| `manifest.json` | Every Store screenshot in listing order: its raw source, theme, crop, en-US caption and alt text (LIST-8) |
| `capture/` | The capture script (`shots.ps1`), its guest helpers, the synthetic settings (`settings.base.json`) and each locale's translated sample data (`fixtures/<locale>.json`) |
| `raw/microsoft/` | The raw captures: the test VM's whole desktop at 1:1, each with the `.json` sidecar the capture helper writes |
| `microsoft/screenshots/` | The listing screenshots, rendered from the raw captures; the generated first screenshot is in a folder per locale (`<locale>/00-hero-dark.png`) |

## Screenshots

Captured from the real installed app with synthetic settings, never from a personal machine (LIST-8). Every shot is in dark mode on the Windows 11 dark wallpaper (`img19.jpg`). The current captures are English, at 1920 x 1080 and 100% scale; they are being replaced by localized captures at 200% (below). The captions in `manifest.json` are the en-US `screenshotCaptions` in `listings.json`, in the same order and count; each other locale has its own translation there. A new or changed screenshot is approved by the maintainer before it is committed.

The test VM has no hub, TV or speakers, so the hardware is simulated with devices Windows enumerates as real, and the app does the rest for real:

| Element | Source |
| --- | --- |
| The hub ("Generic USB Hub") | A QEMU virtual USB hub (VID 0409, PID 55AA), plugged in live through the QEMU monitor; plugging it in makes the app switch to the TV setup |
| The desk monitor ("DELL U2723QE") | The VM's VirtIO display, its EDID name set through QEMU's `virtio-vga` `outputs` property |
| The TV ("LG TV", 60 and 120 Hz) | The signed Virtual Display Driver (VirtualDrivers/Virtual-Display-Driver) with a generated EDID, keeping the driver's own manufacturer ID |
| The audio devices | Two QEMU HD Audio controllers on Windows' inbox driver; the TV's endpoint is renamed "LG TV" in the Sound control panel's Properties name box |

Shot 01 shows the tray flyout and uses the `tray` crop; 03 shows the switching popup, which sits just above the taskbar, and uses the `bottom` crop; 02 and 04 are the Settings window and use the default `window` crop. Shot 00 is the generated hero (below).

1. **Capture** on the Windows 11 test VM (`windows-dev`) with the windows-build-tools app-test helpers, in a throwaway session: an uncommitted compose override adds `-snapshot` (check it is in QEMU's command line before Windows boots), the named VirtIO display and the two audio controllers. Install the Release dev package, the display driver and the audio rename, run `screenshot-desktop.ps1 -Mode Prepare`, set any display Windows scaled back to 100% in Settings, then run the capture script phase by phase: the flyout and General page at the desk, the popup while the virtual hub is plugged in, and the TV setup page once the switch is done. Clear the focus rectangle with a click on blank page space, and save each shot with `Save-DesktopCapture`. Copy each `.png` and `.json` into `raw/microsoft/` under the manifest's names, run `app_test_clean`, then stop the VM and recreate it from the base compose so nothing persists.
2. **Render** with `npm run shots:render` (winui-app-kit's renderer; its README has the rules): each capture is cropped to 1366 x 768, with nothing composited, shaved or scaled, and written without an alpha channel to `microsoft/screenshots/` (ASSET-9). Recaptioning needs no recapture; a new look does.
3. **Check** with `npm run check:screenshots` (part of `npm run validate`): every capture has its sidecar and was taken dark, at 100% scale, with effects on and the dark wallpaper; every screenshot was written by the renderer at exactly 1366 x 768; the captions match the en-US `screenshotCaptions`; no stray file is present.
4. **Sync** with the next Store release: the Store workflow's `screenshots` input uploads every locale's set, its hero first, with that locale's captions from `listings.json`, and keeps the listing's logos. Check the draft in Partner Center before submitting it.

A UI change that shows in a screenshot means recapturing that shot.

## Localized captures

Every screenshot is moving to a capture per listing locale (en-US, es-ES, fr-FR, de-DE, zh-CN, pt-BR and ja-JP; en-CA and fr-CA reuse en-US's and fr-FR's through `copyFrom`), so Windows, the app and its sample data all show the listing's language, at 200% display scale (3840 x 2160 for the 1920 x 1080 desktop). The kit's renderer crops a 200% capture in logical pixels and halves it to 1366 x 768. Status: the scripts below are not yet run on the VM; the en-US and one other locale go to the maintainer for approval, the rest must pass the checks.

- **What is translated:** the setup names, the text a user typed, come from `capture/fixtures/<locale>.json` (Desk and TV; for example Schreibtisch and Fernseher in de-DE). The names Windows gives, the hub (Generic USB Hub in English) and the desk speakers, are read from Windows at capture time, so they are in its display language exactly as a user of that language would see them; nothing is translated by hand. The monitor names (DELL U2723QE, LG TV) are the displays' own EDID names and the TV's audio endpoint is renamed LG TV, so they stay the same in every language.
- **What the app does not translate:** nothing in these screens; every catalog string has its translation in all six languages. Device names come from Windows and the hardware.
- **The app's language** is set by the script in `settings.json` (`Language`), from the fixture, so it never depends on Windows' language list.
- **No translated text in the script:** `shots.ps1` finds everything by automation ID (`SetupNavItem_<setup id>`, `HubTrigger`, `BehaviourHeader`, `AudioHeader`, and WinUI's own `SettingsItem`), the popup's window title or the tray tooltip's app name. `rename-tv-audio.ps1` and `set-display-scale.ps1` use window classes, control IDs and the display API, so they work in any language.

For each locale, in a throwaway session:

1. **Language:** on the base disk, install the language pack once (`Install-Language <locale> -ExcludeFeatures`, about 26 minutes, then `guest-slim.ps1 -Step Clean`), set Windows' display language, input and format to the locale (`Set-WinUILanguageOverride`, `Set-WinUserLanguageList`, `Set-Culture`), then cold-cycle the VM (`vm/remote.sh down`, then a `-snapshot` start: about 80 seconds to a desktop in that language). Set it back to en-US the same way when done.
2. **Desktop:** in the `-snapshot` session (the compose override with the named VirtIO display at 3840 x 2160 and the two audio controllers), install the Release dev package, the display driver (its EDID needs a 3840 x 2160 mode) and the audio rename (`capture/rename-tv-audio.ps1`), plug the virtual hub in and out once (so Windows knows its name; the capture script reads it), run `screenshot-desktop.ps1 -Mode Prepare`, then `capture/set-display-scale.ps1 -Percent 200`.
3. **Capture:** copy `capture/` to `C:\apptest\capture`, then run `shots.ps1 -Locale <locale> -Phase base`, `-Phase popup` while the host plugs in the virtual hub, and `-Phase setup` once the switch is done. Keep the popup frame that shows the spinner and the full text as `02-popup-dark`. Copy each `.png` and `.json` from `C:\apptest\out\<locale>\` into `raw/microsoft/<locale>/` under the manifest's names.
4. **Render and check** with `npm run shots:render`, `npm run hero` and `npm run validate`: the check fails for any listing locale without its capture, so a locale is done only when all of its captures are committed.

## The first screenshot: a generated hero

Store search shows only the first screenshot, at about 320 x 180, so it is a generated hero (D-123): an orange panel with the icon, name, headline and subline, beside the real tray flyout from `raw/microsoft/00-flyout-dark.png`, enlarged whole. Each listing locale has its own, drawn from its `heroHeadline` and `heroSubline` in `listings.json`; en-CA and fr-CA use en-US's and fr-FR's. Its caption is the subline. `npm run hero` draws them (devkit, through winui-app-kit's `scripts/devkit`), and `npm run check:hero` in `validate` fails when one no longer matches its inputs. New or changed headlines are approved by the maintainer, like any screenshot.
