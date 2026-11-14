# Hub Display Switch

A Windows 11 tray app that switches your **displays, refresh rates and default audio device** when
the USB hub for your keyboard, mouse or controller is connected or disconnected. For example, it
can move to the TV with TV audio when the couch hub connects, and go back to the desk monitor and
speakers when it's removed.

## Setups

Each **setup** says:

- **When to use it:**
  - as the default (devices plugged straight into the PC), or
  - when one of its USB hubs is connected. If several hubs are connected, the most recently
    connected one wins.
- **Which displays to turn on**, and which one is the main display (optionally mirrored).
  Everything else is turned off.
- **Which playback device to make default**, and optionally use it for calls too.

## Using it

- **Tray icon click:** a switcher flyout with one tile per setup, plus an auto-switch toggle.
- **Right-click:** quick menu with setups, Pause, Settings, Open logs folder and Exit.
- **Settings:** one page per setup, with *Detect by unplugging…* to find a hub. The General page
  covers start with Windows, switching on start, the popup, theme and refresh rates per display.
- **While switching:** a small popup shows progress, including waiting for a TV to wake and for its
  HDMI audio to appear, then confirms the result.

## Build

From WSL in this folder (see `.codex/docs/windows-development.md`):

```bash
npm run verify:desktop -- --platform x64     # Debug compile, including XAML
npm run install:desktop -- --platform x64    # signed dev MSIX, update in place, launch
```
