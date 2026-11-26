# Store assets

| Folder | Holds |
| --- | --- |
| `microsoft/listings.json`, `listings.md` | The Store listing text in every locale, and the claims it makes (see `listings.md`) |
| `manifest.json` | Every Store screenshot in listing order: its raw source, theme, crop, en-US caption and alt text (LIST-8) |
| `capture/` | The capture script (`shots.ps1`), its guest helpers, the synthetic settings (`settings.base.json`) and each locale's translated sample data (`fixtures/<locale>.json`) |
| `raw/microsoft/<locale>/` | The raw captures, one folder per listing locale: the test VM's whole desktop at 1:1, each with the `.json` sidecar the capture helper writes |
| `microsoft/screenshots/<locale>/` | The listing screenshots, rendered from that locale's raw captures, with its generated first screenshot (`00-hero-dark.png`) |

## Screenshots

Captured from the real installed app with synthetic settings, never from a personal machine (LIST-8). Every shot is in dark mode on the Windows 11 dark wallpaper (`img19.jpg`). The manifest names a capture per listing locale (below); only the English ones, at 1920 x 1080 and 100% scale, exist so far. The captions in `manifest.json` are the en-US `screenshotCaptions` in `listings.json`, in the same order and count; each other locale has its own translation there. A new or changed screenshot is approved by the maintainer before it is committed.

The test VM has no hub, TV or speakers, so the hardware is simulated with devices Windows enumerates as real, and the app does the rest for real:

| Element | Source |
| --- | --- |
| The hub ("Generic USB Hub") | A QEMU virtual USB hub (VID 0409, PID 55AA), plugged in live through the QEMU monitor; plugging it in makes the app switch to the TV setup |
| The desk monitor ("DELL U2723QE") | The VM's VirtIO display, its EDID name set through QEMU's `virtio-vga` `outputs` property |
| The TV ("LG TV", 60 and 120 Hz) | The signed Virtual Display Driver (VirtualDrivers/Virtual-Display-Driver) with a generated EDID, keeping the driver's own manufacturer ID |
| The audio devices | Two QEMU HD Audio controllers on Windows' inbox driver; the TV's endpoint is renamed "LG TV" in the Sound control panel's Properties name box |

Shot 01 shows the tray flyout and uses the `tray` crop; 03 shows the switching popup, which sits just above the taskbar, and uses the `bottom` crop; 02 and 04 are the Settings window and use the default `window` crop. Shot 00 is the generated hero (below).

1. **Capture** each listing locale as in "Localized captures" below, then run `app_test_clean`, stop the VM and recreate it from the base compose so nothing persists.
2. **Render** with `npm run shots:render` (winui-app-kit's renderer; its README has the rules): each capture is cropped to 1366 x 768 (a 200% capture in logical pixels, then halved), with nothing composited or shaved, and written without an alpha channel to `microsoft/screenshots/` (ASSET-9). Recaptioning needs no recapture; a new look does.
3. **Check** with `npm run check:screenshots` (part of `npm run validate`): every listing locale has its captures, each with its sidecar, taken dark, at 100% or 200% scale, with effects on and the dark wallpaper; every screenshot was written by the renderer at exactly 1366 x 768; the captions match the en-US `screenshotCaptions`; no stray file is present.
4. **Sync** with the next Store release: the Store workflow's `screenshots` input uploads every locale's set, its hero first, with that locale's captions from `listings.json`, and keeps the listing's logos. Check the draft in Partner Center before submitting it.

A UI change that shows in a screenshot means recapturing that shot.

## Localized captures

Every screenshot is moving to a capture per listing locale (en-US, es-ES, fr-FR, de-DE, zh-CN, pt-BR and ja-JP; en-CA and fr-CA reuse en-US's and fr-FR's through `copyFrom`), so Windows, the app and its sample data all show the listing's language, at 200% display scale (3840 x 2160 for the 1920 x 1080 desktop). The kit's renderer crops a 200% capture in logical pixels and halves it to 1366 x 768. Status: captured on the VM at 200% in every listing locale (November 26, 2026), awaiting approval; `npm run validate` fails until every locale's captures are committed (the Store workflow runs it before publishing); the en-US and one other locale go to the maintainer for approval, the rest must pass the checks.

- **What is translated:** the setup names, the text a user typed, come from `capture/fixtures/<locale>.json` (Desk and TV; for example Schreibtisch and Fernseher in de-DE). The names Windows gives, the hub (Generic USB Hub in English), the desk speakers (Speakers) and the audio device in brackets after each playback device (High Definition Audio Device), are Windows' own names in the display language, read at capture time from its driver strings for that language (`C:\Windows\System32\DriverStore\<language>\*.inf_loc`, through `capture/windows-strings.ps1`); nothing is translated by hand. They have to be set, because Windows names a device once, in the language it was installed in, and the VM was installed in English: `capture/name-audio-devices.ps1` renames the audio devices, and `shots.ps1` writes the hub's name. Windows itself leaves some untranslated, such as the hub in pt-BR. The monitor names (DELL U2723QE, LG TV) are the displays' own EDID names and the TV's audio endpoint is renamed LG TV, so they stay the same in every language.
- **What the app does not translate:** nothing in these screens; every catalog string has its translation in all six languages. Device names come from Windows and the hardware.
- **The app's language** stays at "Use system setting" (an empty `Language` in `settings.json`), as the Settings shot's alt text says, so the app follows Windows' display language; the script stops when Windows is not in the fixture's `language`.
- **No translated text in the script:** `shots.ps1` finds everything by automation ID (`SetupNavItem_<setup id>`, `HubTrigger`, `BehaviourHeader`, `AudioHeader`, and WinUI's own `SettingsItem`), the popup's window title or the tray tooltip's app name. `name-audio-devices.ps1` and `set-display-scale.ps1` use window classes, control IDs, SetupAPI and the display API, so they work in any language.

For each locale, one locale per VM session (a display language change needs a new sign-in, so the host loops over locales with a cold VM cycle between them; the script captures one locale per run). Run the host commands from the repo root with `source /development/windows-build-tools/vm/app-test/app-test.sh` and `app_test_use win11`:

1. **Language:** on the base disk, install the language pack once (`Install-Language <locale> -ExcludeFeatures`, about 26 minutes and 3.4 GB at peak, then `guest-slim.ps1 -Step Clean`), switch Windows to the locale with `app_test_language <locale>` (display language, input, home location and format; `en-US` restores), then cold-cycle the VM (`vm/remote.sh down`, then the `-snapshot` start: about 80 seconds to a desktop in that language).
2. **Desktop:** in the throwaway session (an uncommitted compose override adds `-snapshot`, which must show in QEMU's command line before Windows boots, the named VirtIO display at 3840 x 2160 and the two audio controllers), install the Release dev package and the display driver (its EDID needs a 3840 x 2160 mode), then copy the helpers and run them (after the install, whose `app_test_put` claims `C:\apptest` for this run; a folder made before the claim is refused as unowned):
   ```sh
   app_test_ssh 'mkdir C:\apptest\capture\fixtures'
   for f in shots.ps1 settings.base.json fixtures/<locale>.json name-audio-devices.ps1 windows-strings.ps1 set-display-scale.ps1; do
       app_test_put "store-assets/capture/$f" "capture/$f"
   done
   app_test_ui 'C:\apptest\capture\name-audio-devices.ps1'
   ```
   Plug the virtual hub in and out once through the QEMU monitor (so Windows knows its name; the capture script reads it), turn both displays on with `app_test_run 'DisplaySwitch.exe /extend'`, run `screenshot-desktop.ps1 -Mode Prepare -Scale 200`, then `app_test_run '& C:\apptest\capture\set-display-scale.ps1 -Percent 200'`. The TV is active only after a switch and would otherwise come up at Windows' recommended 300%; setting it while both displays are on stores its 200%, and `/extend` must come before Prepare, which it would otherwise undo.
3. **Capture:** `app_test_ui` passes no arguments, so run each phase through `app_test_run`, which does: `app_test_run '& C:\apptest\capture\shots.ps1 -Locale <locale> -Phase base'`, then `-Phase popup` started in the background (it waits up to 30 seconds for the popup) just before the host plugs in the virtual hub, then `-Phase setup` once the switch is done. Keep, as `02-popup-dark`, the frame that shows the full switching text with the progress ring drawn largest (the ring is an animated arc, so frames differ). Copy each `.png` and `.json` back with `app_test_get out/<locale>/<name>.png` into `raw/microsoft/<locale>/` under the manifest's names. Afterwards run `app_test_clean`, stop the VM and recreate it from the base compose.
4. **Render and check** with `npm run shots:render`, `npm run hero` and `npm run validate`: the check fails for any listing locale without its capture, so a locale is done only when all of its captures are committed.

## The first screenshot: a generated hero

Store search shows only the first screenshot, at about 320 x 180, so it is a generated hero (D-123): an orange panel with the icon, name, headline and subline, beside the real tray flyout from that locale's `raw/microsoft/<locale>/00-flyout-dark.png`, enlarged whole. Each listing locale has its own, drawn from its own capture and its `heroHeadline` and `heroSubline` in `listings.json`; en-CA and fr-CA use en-US's and fr-FR's. Its caption is the subline. `npm run hero` draws them (devkit, through winui-app-kit's `scripts/devkit`), and `npm run check:hero` in `validate` fails when one no longer matches its inputs. New or changed headlines are approved by the maintainer, like any screenshot.
