> **Read when:** adding or changing any window, page, flyout, setup tile, popup, dialog or card.
> **Governs:** `HubDisplaySwitch/**/*.xaml`, `HubDisplaySwitch/**/*.xaml.cs`, `HubDisplaySwitch/Windows/**`, `HubDisplaySwitch/Pages/**`, `HubDisplaySwitch/App.xaml`

# UI conventions

This app's own interface conventions. The shared family rules are in the standards' [ui.md](https://github.com/Dynamic-Solutions-Canada/app-standards/blob/main/standards/ui.md) and [tray.md](https://github.com/Dynamic-Solutions-Canada/app-standards/blob/main/standards/tray.md); this file records only what is specific to this app.

## Settings window and pages

- `SettingsWindow` hosts the kit's `SettingsShell` (UI-1, UI-2): its title bar, navigation (a 240-wide pane from 860 epx, an icon rail below), the built-in Settings item and the fixed footer ("Our other apps", then About). The window only fills `MenuItems`, in code, through `RebuildSetupItems`: a "Setups" header, one item per setup (its name and glyph, tagged with the setup id), then "Add setup", an action item (`SelectsOnInvoked="False"`) that the shell reports through `ActionInvoked` and never selects.
- Renaming a setup or changing its icon updates its menu item in place (`UpdateSetupItem`); only adding or deleting a setup rebuilds the menu, so the open page is kept.
- Every page is `ScrollViewer` then `StackPanel Style="{StaticResource PageContentStyle}"`, a `PageTitleStyle` title, then sections. A section is a `SectionHeaderStyle` heading (`FirstSectionHeaderStyle` for the first one under the title or the setup header row) above a `CardBorderStyle` card. These styles come from winui-app-kit's shared XAML (merged in `App.xaml`); never write page padding, card brushes or header margins inline.
- The Settings page uses the kit's `StartupSetting`, `LanguageSetting` and `ThemeSetting` rows and saves what they report; app-specific controls sit in the same cards with their own `Header`. After an unreadable settings file, the kit's `SettingsResetNotice` sits under the title.
- About is the kit's `AboutPage`, filled from `AboutContent.Create()`; never build an app About page or restyle its cards. Product-specific cards go in `AboutInfo.ExtraCards`.
- The footer items and the built-in Settings item use the kit's labels (`kit.nav.other-apps`, `kit.nav.about`, `kit.settings.title`).
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

- Button content with an icon is `StackPanel Style="{StaticResource IconLabelStyle}"` holding a `FontIcon Style="{StaticResource ButtonGlyphStyle}"`, then the label.
- An icon-only button has both `l10n:Localize.ToolTip` and `l10n:Localize.Name` (its automation name).

## Text

- Visible text follows the catalog rule under "Conventions and gotchas" in [AGENTS.md](../../AGENTS.md).
