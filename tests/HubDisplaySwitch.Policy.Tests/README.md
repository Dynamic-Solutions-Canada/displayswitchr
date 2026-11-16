# Switching policy harness

Checks the switching rules in `SwitchEngine` (see "Behaviour" in [AGENTS.md](../../AGENTS.md)). It links the production engine, the `Setup` model and the hub-matching rules in `UsbDevice.cs`, and replaces the WinUI dispatcher, WMI, the display and audio APIs, settings, interface text and NLog with the stand-ins in `Fakes.cs`. Timers run on a virtual clock, so a scenario that waits a minute for a TV finishes at once and always takes the same path.

Covers:

- Startup: the default setup with no hub, list order when several hubs are connected, nothing switched when the setup is already in place, and switching on start turned off.
- Hubs: a hub connecting, the newest hub winning, falling back to the previous connected hub's setup and then the default, a hub matched by model on any port or only on its saved port, a default setup's hubs never triggering it, and a failed USB query.
- Manual switches, pausing and editing: a manual switch kept until the desired setup changes, no switching while paused, and editing setups re-ranking hubs without switching.
- The switch sequence: waiting for a sleeping TV, giving up on a display that never appears, a setup with no displays, HDMI audio that appears after its display, a TV dropping back to 60 Hz afterwards, and the popup turned off.

Does not prove:

- That the CCD display calls, `ChangeDisplaySettingsEx`, Core Audio, the undocumented `IPolicyConfig` or WMI behave as the stand-ins do. The display and audio stand-ins are small models of what the engine expects, not of Windows.
- Mirrored and multi-display layouts (the engine waits 600 ms on a real thread between the layout and the primary display, so the scenarios use one display per setup), and the "already in place" check for those.
- Anything on screen: the popup, the flyout, the settings pages or the tray. Those need the installed app; see the manual checks in [docs/development.md](../../docs/development.md).

Run with `npm test`, or alone with `external/winui-app-kit/scripts/dotnet run --project tests/HubDisplaySwitch.Policy.Tests`.
