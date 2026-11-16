> **Read when:** adding or changing a setting, the settings file, or how it loads, saves or migrates.
> **Governs:** `HubDisplaySwitch/ViewModels/UserSettings.cs`, `HubDisplaySwitch/Classes/Settings/**`

# Settings

Settings live in `settings.json` under `%AppData%\HubDisplaySwitch\` (redirected into the package's data folder when packaged), written through the kit's `JsonSettingsStore`. Where the app writes data, and what survives uninstall, is in [docs/development.md](../../docs/development.md).

- Settings JSON uses `WhenWritingNull`, not `WhenWritingDefault`: several bools default to true, and an omitted false would load back as true.
- Settings edits apply and save immediately; call `MainWindow.Engine.SettingsChanged()` after editing setups.
- `SettingsManager` migrates the first (unpackaged, PowerShell and WinForms era) two-state settings file (`Connected` / `Disconnected`) into a default "Desk" setup plus a hub-triggered "TV" setup.
- `RestoreSettings` never throws (it falls back to defaults and sets `WasReset`, which opens Settings) and writes the file only when it changed. Zero setups is a valid saved state, not "unreadable". The single instance is claimed in `App` before settings load, so a second launch never touches the file.
- Paths handed to other processes (Explorer) must use `SettingsManager.PhysicalSettingsDir` (MSIX VFS).
