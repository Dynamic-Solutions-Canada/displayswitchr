# Microsoft Store listing text

`listings.json` is the draft listing text in the shared `schemaVersion: 1` / `locales` shape (LIST-3), for the app's nine locale identities: `en-US`, `en-CA`, `es-ES`, `fr-FR`, `fr-CA`, `de-DE`, `zh-CN`, `pt-BR` and `ja-JP` (the same set the package manifest declares). Seven records hold wording; `en-CA` is `{ "copyFrom": "en-US" }` and `fr-CA` is `{ "copyFrom": "fr-FR" }`, matching the app's own catalogs. Fields are semantic names, not Partner Center CSV field IDs: when importing, merge into the real Partner Center export and keep its opaque `Field`/`ID` cells. No publisher reads this file yet, so it is a draft (LIST-9).

Release notes are not in this file. Each version's "What's new" text is `release-notes/<version>.md` (`en-US`) plus one `release-notes/<version>.<identity>.md` per other wording catalog, the same text the GitHub Release uses (REL-6, LIST-10).

Translated wording uses the same terms as the in-app catalogs (`HubDisplaySwitch/Localization/Catalogs`) so the listing matches what people see. Every translation, in the listing and in the release notes, was drafted without native-speaker review.

`npm run check:listing` (part of `npm run validate`) checks this file and the current version's notes: the locales and aliases, every field, the limits below, matching caption counts, no em dashes, and that the README tagline is the first sentence of the `en-US` short description.

## Limits

| Text | Limit | Longest draft |
| --- | --- | --- |
| Full description | 4,000 characters | 2,630 (fr-FR) |
| Short description | 270 characters | 253 (fr-FR) |
| Features | Up to 20, each up to 200 characters | 13, longest 146 (fr-FR) |
| Keywords | Up to 7, each up to 40 characters, 21 words combined | 7 per locale, at most 17 words (es-ES, fr-FR) |
| Screenshot captions | One per screenshot, up to 200 characters | 4 per locale |
| Release notes (`release-notes/`) | 1,500 characters | 559 (1.0.0, de-DE) |

## Claims to keep true

- Displays and audio only change while the user is signed in; the sign-in screen keeps the last layout. Don't promise switching at boot or while signed out.
- Audio: separate output and input pickers, each with "Don't change". Setting the default endpoint uses Windows' undocumented policy interface, so recheck it on new Windows builds before release notes claim audio switching.
- Privacy: no account, ads, analytics or telemetry. Settings and logs are local. The only network request is the "Our other apps" page fetching the public promo manifest from GitHub.
- The "Our other apps" page follows the app's language; only the app descriptions it downloads (from the shared apps-promo catalog) are in English.
- The interface is in English, Spanish, French, German, Simplified Chinese, Brazilian Portuguese and Japanese, following Windows or a choice in Settings. Device and setup names are never translated.
- Requires Windows 11 (the manifest's minimum version).

## Console-only fields

| Field | Value |
| --- | --- |
| Category | Not set; suggested Utilities & tools > Display |
| Pricing | Paid, US $0.99 (price tier Tier1012), with the same free trial as the other paid apps; the trial length is set in Partner Center |
| Age rating | Questionnaire not completed |
| Privacy policy URL | Not available yet; it comes from Ryan (D-017) |

## Screenshots (not captured yet)

Planned set, in caption order: tray flyout above the taskbar, a setup page (TV), the on-screen popup mid-switch, and the General settings page. English captures can serve every listing, or capture each language by switching Interface language in Settings. Capture the real installed app with no personal device names, hub IDs or network names visible, and alternate light and dark themes. Files, their order and alt text go in `store-assets/manifest.json` when they exist (LIST-8). Microsoft requires at least one 1366 x 768 or larger PNG; see [screenshots and images](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images).

## Open questions before submission

- Reserve the name in Partner Center, then set `<Identity>` in `HubDisplaySwitchMSIX/Package.appxmanifest` and the identities in `windows-build.json` together.
- Category, age rating and the privacy policy URL (above).
- Add the product ID to the shared promo catalog (apps-promo) once the listing is live.
