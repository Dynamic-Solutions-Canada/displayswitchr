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
  Everything else is turned off. Each display can use its highest refresh rate, a chosen rate,
  or be left alone.
- **Which playback device and microphone to make default**, or leave either unchanged.

## Using it

- **Tray icon click:** a switcher flyout with one tile per setup, plus an auto-switch toggle.
- **Right-click:** quick menu with setups, Pause, Settings, Open logs folder and Exit.
- **Settings:** one page per setup, with *Detect by unplugging…* to find a hub. The General page
  covers start with Windows, switching on start, the popup, interface language and theme.
- **Languages:** English, Spanish, French, German, Simplified Chinese, Brazilian Portuguese and
  Japanese (plus Canadian English and French). Follows Windows unless you pick one in Settings.
- **While switching:** a small popup shows progress, including waiting for a TV to wake and for its
  HDMI audio to appear, then confirms the result.

## Build

From WSL in this folder (see the [Windows build tools README](https://github.com/Dynamic-Solutions-Canada/windows-build-tools)):

```bash
npm run verify:desktop     # Debug compile, including XAML
npm run install:desktop    # signed dev MSIX, update in place, launch
```
