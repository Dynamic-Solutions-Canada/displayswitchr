# Microsoft Store listing text

`listings.json` is the draft listing text, in the same `schemaVersion: 1` / `locales` shape as
Persistent's, for the app's nine locale identities: `en-US`, `en-CA`, `es-ES`, `fr-FR`, `fr-CA`,
`de-DE`, `zh-CN`, `pt-BR` and `ja-JP` (the same set the package manifest declares). Seven records hold
wording; `en-CA` is `{ "copyFrom": "en-US" }` and `fr-CA` is `{ "copyFrom": "fr-FR" }`, matching the
app's own catalogs. Fields are semantic names, not Partner Center CSV field IDs: when importing, merge
into the real Partner Center export and keep its opaque `Field`/`ID` cells.

Translated wording uses the same terms as the in-app catalogs (`HubDisplaySwitch/Localization/Catalogs`)
so the listing matches what people see. It was drafted without native-speaker review.

| Text | Limit | Longest draft |
| --- | --- | --- |
| Full description | 4,000 characters | 2,531 (fr-FR) |
| Short description | 270 characters | 253 (fr-FR) |
| Features | Up to 20, each up to 200 characters | 13, longest 146 (fr-FR) |
| Release notes | 1,500 characters | "First release." |
| Keywords | Up to 7, each up to 40 characters, 21 words combined | 7 per locale |
| Screenshot captions | One per screenshot, up to 200 characters | 4 per locale |

## Claims to keep true

- Displays and audio only change while the user is signed in; the sign-in screen keeps the last
  layout. Don't promise switching at boot or while signed out.
- Audio: separate output and input pickers, each with "Don't change". Setting the default endpoint
  uses Windows' undocumented policy interface, so recheck it on new Windows builds before
  release notes claim audio switching.
- Privacy: no account, ads, analytics or telemetry. Settings and logs are local. The only network
  request is the "Our other apps" page fetching the public promo manifest from GitHub.
- The interface is in English, Spanish, French, German, Simplified Chinese, Brazilian Portuguese and
  Japanese, following Windows or a choice in Settings. The shared "Our other apps" page stays English
  (it comes from the `apps-promo` submodule), and the listing says so. Device and setup names are
  never translated. Requires Windows 11 (the manifest's minimum version).

## Screenshots (not captured yet)

Planned set, in caption order: tray flyout above the taskbar, a setup page (TV), the on-screen
popup mid-switch, and the General settings page. English captures can serve every listing (as with
Persistent), or capture each language by switching Interface language in Settings. Capture the real installed app with no personal
device names, hub IDs or network names visible; use light and dark themes alternately. Microsoft
requires at least one 1366 x 768 or larger PNG; see
[screenshots and images](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images).

## Still needed before submission

- Reserve the name in Partner Center, then set `<Identity>` in `HubDisplaySwitchMSIX/Package.appxmanifest`
  and `dev_identity` in `windows-build.json` together.
- Pricing, category (suggested: Utilities & tools > Display), age rating questionnaire, and a
  privacy policy URL if Partner Center asks for one.
- Add the product ID to the shared promo catalog (`apps-promo` `ids.json`) once it exists.
