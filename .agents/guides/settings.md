> **Read when:** adding or changing a setting, the settings file, or how it loads, saves, migrates or resets.
> **Governs:** `HubDisplaySwitch/ViewModels/UserSettings.cs`, `HubDisplaySwitch/Classes/Settings/**`, `tests/HubDisplaySwitch.Settings.Tests/**`

# Settings

Settings live in `settings.json` under `%AppData%\HubDisplaySwitch\` (redirected into the package's data folder when packaged), loaded and saved by winui-app-kit's versioned `JsonSettingsStore` (CONF-3). How the store reports first run, migration, an unreadable file, a newer file and a reset, and which backups it keeps, is in the kit README's "Settings" section; its "Gotchas" cover serialisation (nulls omitted, defaults written) and paths handed to other processes. Where the app writes data, and what survives uninstall, is in [docs/development.md](../../docs/development.md).

## Schema steps

- The store writes `SchemaVersion` and owns it; `UserSettings` never declares a version of its own.
- `Classes/Settings/SettingsMigrations.cs` holds the steps, in order. The step at index n upgrades a file from version n to n + 1 by editing its JSON. Steps are never removed or reordered once shipped.
- A change to the file's shape (a renamed or moved property, a new meaning for a value) is a new step at the end, with a scenario in `tests/HubDisplaySwitch.Settings.Tests/`. Adding a property with a safe default needs no step.
- Every file written before versioning is version 0, whatever its shape, so step 1 must leave a current-shape file as it is. It does three things: drops the old hand-kept `Version` number, keeps any file that has `Setups` (zero setups is a valid saved state), and converts the first two-state file (`Connected` / `Disconnected`, from the PowerShell and WinForms builds) into a default "Desk" setup plus a hub-triggered "TV" setup. Any other file makes it throw, so the store keeps it as `.bad`.
- A step that throws resets the user's setups, so a step throws only for a file it cannot make sense of.
- Steps stay free of WinUI and the kit's app types, so the settings harness can run them against real files.

## In the app

- `SettingsManager.RestoreSettings` replaces the store's empty defaults with one default "Desk" setup for the displays and audio in use, on a first run and after an unreadable file. After an unreadable file it keeps the load result until the Settings page takes it: `MainWindow` opens the settings window on the Settings page on that launch, even a silent one, and the page shows the kit's `SettingsResetNotice` (CONF-2). The store has already written defaults, so the notice is shown once. The single instance is claimed in `App` before settings load, because the store writes the file it loads.
- Edits apply and save immediately; call `MainWindow.Engine.SettingsChanged()` after editing setups.
- "Reset settings" (General page, CONF-4) is the kit's `ResetSetting` row: it asks (its `KeptNote` adds that the setups are replaced by one default setup), then runs the page's reset, where `SettingsManager.Reset` keeps the old file as `settings.json.<UTC time>.reset` and starts again from the defaults. It replaces `SettingsManager.Current`, so nothing may keep a reference to the old object: the settings window rebuilds its menu and opens the new default setup, and the engine re-reads setups. Start with Windows is Windows' state and is left alone.
- `UserSettings.CompleteInitialization` repairs nulls and carries old per-display refresh rates and window placement into their current fields.
