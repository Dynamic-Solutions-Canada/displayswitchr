> **Read when:** adding or changing any user-visible text, a language, or the language setting.
> **Governs:** `HubDisplaySwitch/Localization/**`, `HubDisplaySwitch/Classes/AppLanguages.cs`, `release-notes/**`, `store-assets/microsoft/listings.json`

# Localization

The app ships the nine-locale set from the standards' [locales.md](https://github.com/Dynamic-Solutions-Canada/app-standards/blob/main/standards/locales.md): nine locale identities in `Classes/AppLanguages.cs`, read from seven wording catalogs (en-CA and fr-CA reuse en-US and fr-FR wording).

## Interface text

- UI text lives in `Localization/Catalogs/*.json`, keyed by the English text and embedded in the exe.
- In code, use `UiText.Get("English text")` or `UiText.Format("... {{name}} ...", ("name", value))`.
- In XAML, use `l10n:Localize.Text|Content|Header|ToolTip|...="English text"` with `xmlns:l10n="using:WinUIAppKit.Localization"`; it relabels live on a language change.
- Shared labels (Settings, On and Off, About, the About page, the Settings rows, Cancel) are winui-app-kit's `kit.*` keys, translated in the kit; use them and never define a `kit.` key here.
- Add every new key to all seven catalogs, then run `npm run check:l10n` (winui-app-kit's check, configured by the `localization` block in `windows-build.json`). Keys the code builds from constants (`SwitchReason`, the About description) are listed there as `extra_key_patterns`.
- `AppDescription` is the one identifier key: the package manifest's `ms-resource:Resources/<Key>` syntax needs one, and the kit's check exempts manifest keys from the English-key rule. It is both the About page description and the manifest description, written into `resources.pri` for every language at packaging.
- Never translate logs, settings values, device names or setup names. Switch reasons are `SwitchReason` keys, translated only in the popup.

## Language choice

- The language follows Windows unless `UserSettings.Language` is set with the kit's Language row on the Settings page (empty means "Use system setting").
- Don't set `ApplicationLanguages.PrimaryLanguageOverride`: the kit resolves "Use system setting" from the package language list, and `App` clears one left by 1.0.0.10 to 1.0.0.11.
- The "Our other apps" page follows the app's language through `PromoLocalization.LocaleProvider`, set in `MainWindow`.

## Adding a locale

A new locale lands in one change: the catalog, `AppLanguages`, `Package.appxmanifest` `<Resources>`, every record in the Store `listings.json`, the release notes for the next version, and the README `## Languages` section.

## Review status

Every catalog, the listing and the release notes are AI-drafted; none has had a native-speaker review yet. Record a review here, per wording catalog, when one happens.
