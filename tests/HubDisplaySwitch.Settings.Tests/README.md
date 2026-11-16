# Settings file harness

Checks how the settings file loads, upgrades and resets (CONF-3, CONF-4; see [.agents/guides/settings.md](../../.agents/guides/settings.md)). It links winui-app-kit's real `JsonSettingsStore` and `AtomicFile`, and the app's real schema steps (`SettingsMigrations.cs`), settings class (`UserSettings.cs`) and setup model, and runs them against real files in a temporary folder. The display and audio lists, the kit's theme, window and language hooks, and the app's save call are the stand-ins in `Stubs.cs`.

Covers:

- A first run, which writes the file with the current schema version.
- Every file an existing install can have: one from before schema versions (setups kept, the old `Version` number dropped, the original kept as `settings.json.v0.bak`), zero setups (a valid state, not an unreadable file), and the first two-state format (converted into a default Desk setup and a hub-triggered TV setup, with the hub, refresh rate and the display's own audio).
- A saved `false` for a setting that defaults to `true` staying `false`.
- An unreadable file and a JSON file of another shape (kept as `.bad`, defaults written), a current file (no backup), a file from a newer version (backed up, loaded as far as this version understands), and Reset (old file kept as `.reset`, defaults written).

Does not prove:

- What `SettingsManager` does with each outcome in the app (first-run defaults from the displays in use, the settings window opening after a reset), the Reset button and its confirmation, or that packaged installs keep the file where the stand-in paths do. Those need the installed app; see the manual checks in [docs/development.md](../../docs/development.md).

Run with `npm test`, or alone with `external/winui-app-kit/scripts/dotnet run --project tests/HubDisplaySwitch.Settings.Tests`.
