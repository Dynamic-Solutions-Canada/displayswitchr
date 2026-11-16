> **Read when:** adding or changing any window, page, flyout, setup tile, popup, dialog or card.
> **Governs:** `HubDisplaySwitch/**/*.xaml`, `HubDisplaySwitch/**/*.xaml.cs`, `HubDisplaySwitch/Windows/**`, `HubDisplaySwitch/Pages/**`, `HubDisplaySwitch/App.xaml`

# UI conventions

This app's own interface conventions. The shared family rules are in the standards' [ui.md](https://github.com/Dynamic-Solutions-Canada/app-standards/blob/main/standards/ui.md) and [tray.md](https://github.com/Dynamic-Solutions-Canada/app-standards/blob/main/standards/tray.md); this file records only what is specific to this app.

## Settings window and pages

- `SettingsWindow` has a custom 48 px title bar (16 px icon, caption-style title) over a left `NavigationView`. The menu is built in code by `RebuildSetupItems`: a "Setups" header, one item per setup (its name and glyph), then "Add setup", which opens a new setup and never stays selected. Settings is the built-in settings item; "Our other apps" and About are footer items, in that order.
- Renaming a setup or changing its icon updates its menu item in place (`UpdateSetupItem`); only adding or deleting a setup rebuilds the menu, so the open page is kept.
- Every page is `ScrollViewer` then a `StackPanel` with `Padding="36,12,36,36"`, `Spacing="8"` and `MaxWidth="960"`, a `TitleTextBlockStyle` title, then sections. A section is a `BodyStrongTextBlockStyle` heading (`Margin="2,16,0,2"`; `2,0,0,2` or `2,8,0,2` for the first under the title) above a `Border Style="{StaticResource CardBorderStyle}"` card. `CardBorderStyle` lives in `App.xaml`; use it rather than repeating brushes. These page values are repeated per page today and move to the kit's shared styles later.
- The setup page replaces the title with a header row: a 56 px icon button (opens the glyph picker flyout), the name `TextBox` in 24 px semibold, and Delete on the right.
- Settings controls sit inside the card with their own `Header` (`ToggleSwitch`, `ComboBox`); a note under a control, or under a radio option, is a `CaptionTextBlockStyle` text block in `TextFillColorSecondaryBrush`.
- Settings apply and save on change. There is no Save or Cancel button.
- A yes/no question (delete a setup, reset settings) goes through the kit's `ConfirmDialog.ConfirmAsync` on the settings window, with the action named on the confirm button; it defaults to Cancel (UI-6). A picker that needs room (detect a hub) is a `ContentDialog` shown from the settings window. Neither is ever shown from the flyout or the popup.

## Tray flyout

- `SwitcherWindow` is three rows: a `BodyStrongTextBlockStyle` header with the Auto-switch / Paused toggle, the setup tiles (built in code), and a footer on `LayerOnAcrylicFillColorDefaultBrush` with a top `CardStrokeColorDefaultBrush` border holding the status line and the Settings button.
- A setup tile is one borderless, transparent button across the whole row: a 40 px round glyph badge (accent fill when active, subtle fill otherwise), name and a caption with the displays and short audio name, and a state label on the right (Active, Default, Hub connected, Hub not connected, or none). The active tile also gets the subtle fill background. Clicking a tile dismisses the flyout, then switches.
- The flyout is rebuilt from the engine state each time it opens and on a language change.

## Popup

- `OsdWindow` is one row: a 32 px column holding a `ProgressRing` while switching or a 24 px accent `FontIcon` when done, then a title (`BodyStrongTextBlockStyle`, 15 px) and a secondary caption, both single-line and trimmed. It never takes focus (see AGENTS.md).

## Buttons

- Button content with an icon is a horizontal `StackPanel` (`Spacing="8"`) with a 14 px `FontIcon`, then the label.
- An icon-only button has both `l10n:Localize.ToolTip` and `l10n:Localize.Name` (its automation name).

## Text

- Visible text follows the catalog rule under "Conventions and gotchas" in [AGENTS.md](../../AGENTS.md).
