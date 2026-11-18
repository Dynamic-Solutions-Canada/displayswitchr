# Store assets

| Folder | Holds |
| --- | --- |
| `microsoft/listings.json`, `listings.md` | The Store listing text in every locale, and the claims it makes (see `listings.md`) |
| `manifest.json` | Every Store screenshot in listing order: its raw source, theme, crop, en-US caption and alt text (LIST-8) |
| `raw/microsoft/` | The raw captures: the test VM's whole desktop at 1:1, each with the `.json` sidecar the capture helper writes |
| `microsoft/screenshots/` | The listing screenshots, rendered from the raw captures |

## Screenshots

Captured from the real installed app with synthetic settings, never from a personal machine (LIST-8). Every shot is in dark mode on the Windows 11 dark wallpaper (`img19.jpg`), at 1920 x 1080 and 100% scale. The captions in `manifest.json` are the en-US `screenshotCaptions` in `listings.json`, in the same order and count; each other locale has its own translation there. A new or changed screenshot is approved by the maintainer before it is committed.

The test VM has no hub, TV or speakers, so the hardware is simulated with devices Windows enumerates as real, and the app does the rest for real:

| Element | Source |
| --- | --- |
| The hub ("Generic USB Hub") | A QEMU virtual USB hub (VID 0409, PID 55AA), plugged in live through the QEMU monitor; plugging it in makes Hub switch to the TV setup |
| The desk monitor ("DELL U2723QE") | The VM's VirtIO display, its EDID name set through QEMU's `virtio-vga` `outputs` property |
| The TV ("LG TV", 60 and 120 Hz) | The signed Virtual Display Driver (VirtualDrivers/Virtual-Display-Driver) with a generated EDID, keeping the driver's own manufacturer ID |
| The audio devices | Two QEMU HD Audio controllers on Windows' inbox driver; the TV's endpoint is renamed "LG TV" in the Sound control panel's Properties name box |

Shot 00 shows the tray flyout and uses the `tray` crop; 02 shows the switching popup, which sits just above the taskbar, and uses the `bottom` crop; 01 and 03 are the Settings window and use the default `window` crop.

1. **Capture** on the Windows 11 test VM (`windows-dev`) with the windows-build-tools app-test helpers, in a throwaway session: an uncommitted compose override adds `-snapshot` (check it is in QEMU's command line before Windows boots), the named VirtIO display and the two audio controllers. Install the Release dev package, the display driver and the audio rename, run `screenshot-desktop.ps1 -Mode Prepare`, set any display Windows scaled back to 100% in Settings, then run the capture script phase by phase: the flyout and General page at the desk, the popup while the virtual hub is plugged in, and the TV setup page once the switch is done. Clear the focus rectangle with a click on blank page space, and save each shot with `Save-DesktopCapture`. Copy each `.png` and `.json` into `raw/microsoft/` under the manifest's names, run `app_test_clean`, then stop the VM and recreate it from the base compose so nothing persists.
2. **Render** with `npm run shots:render` (winui-app-kit's renderer; its README has the rules): each capture is cropped to 1366 x 768, with nothing composited, shaved or scaled, and written without an alpha channel to `microsoft/screenshots/` (ASSET-9). Recaptioning needs no recapture; a new look does.
3. **Check** with `npm run check:screenshots` (part of `npm run validate`): every capture has its sidecar and was taken dark, at 100% scale, with effects on and the dark wallpaper; every screenshot was written by the renderer at exactly 1366 x 768; the captions match the en-US `screenshotCaptions`; no stray file is present.
4. **Sync** to Partner Center with the next release's submission: the files in order and each locale's captions from `listings.json`, in the same upload archive as that submission's package, never a separate upload.

A UI change that shows in a screenshot means recapturing that shot.
