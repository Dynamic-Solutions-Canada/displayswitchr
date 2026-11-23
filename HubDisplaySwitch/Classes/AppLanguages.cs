using WinUIAppKit.Localization;

namespace HubDisplaySwitch.Classes;

/// <summary>
/// The interface languages DisplaySwitchr ships: the same nine identities, in the same order, as
/// Persistent and WSL Monitor. Canadian English reuses US wording and Canadian French reuses French
/// wording; they stay separate choices so the picker and Windows' preferences match them exactly. Keep
/// this list, <c>Localization/Catalogs/*.json</c>, the manifest's <c>&lt;Resource Language&gt;</c>
/// entries and the Store <c>listings.json</c> in step.
/// </summary>
public static class AppLanguages
{
    /// <summary>Manifest resource prefix of the embedded catalogs (see the csproj).</summary>
    public const string CatalogPrefix = "HubDisplaySwitch.Localization.";

    /// <summary>The supported set, in picker order.</summary>
    public static SupportedLanguages Supported { get; } = new("en-US",
    [
        ("en-US", "en-US", "English (United States)"),
        ("en-CA", "en-US", "English (Canada)"),
        ("es-ES", "es-ES", "Español (España)"),
        ("fr-FR", "fr-FR", "Français (France)"),
        ("fr-CA", "fr-FR", "Français (Canada)"),
        ("de-DE", "de-DE", "Deutsch (Deutschland)"),
        ("zh-CN", "zh-CN", "简体中文（中国）"),
        ("pt-BR", "pt-BR", "Português (Brasil)"),
        ("ja-JP", "ja-JP", "日本語（日本）"),
    ]);
}
