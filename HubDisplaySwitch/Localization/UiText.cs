using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HubDisplaySwitch.Localization;

/// <summary>
/// Display-only wording, same scheme as Persistent: catalogs are keyed by the English source text and
/// embedded in the exe (Localization/Catalogs/*.json). The language follows Windows unless the user picks
/// one in Settings. Logs, settings and device names are never translated.
/// </summary>
internal static class UiText
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static readonly Dictionary<string, Dictionary<string, string>> Catalogs = new(StringComparer.Ordinal);
    private static string? _override;

    /// <summary>Raised after the language changes; listeners re-read their text.</summary>
    internal static event Action? Changed;

    /// <summary>The resolved UI locale, one of <see cref="SupportedLanguages.Catalogs"/>' keys.</summary>
    internal static string Locale => _override ?? SupportedLanguages.Resolve(PreferredLanguages());

    /// <summary>Set the user's choice ("" or null = match Windows). Raises <see cref="Changed"/> when <paramref name="notify"/>.</summary>
    internal static void SetOverride(string? language, bool notify = true)
    {
        string? normalized = SupportedLanguages.NormalizeOverride(language);
        bool changed = normalized != _override;
        _override = normalized;
        // Let WinUI's own strings follow too. Windows persists this per package, so also sync it at startup
        // (notify: false) in case it was left over from an earlier choice. Needs package identity.
        try
        {
            string wanted = normalized ?? "";
            if (global::Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride != wanted)
                global::Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = wanted;
        }
        catch { }
        if (changed && notify) Changed?.Invoke();
    }

    private static IEnumerable<string> PreferredLanguages()
    {
        // ApplicationLanguages.Languages already applies PrimaryLanguageOverride, so read the user's Windows list.
        try { return global::Windows.System.UserProfile.GlobalizationPreferences.Languages.ToArray(); }
        catch { return [CultureInfo.CurrentUICulture.Name]; }
    }

    /// <summary>Look up an application-owned key (its English text). Device and setup names are passed as values, never keys.</summary>
    internal static string Get(string key)
    {
        string catalog = SupportedLanguages.Catalogs[Locale];
        return ReadCatalog(catalog).GetValueOrDefault(key) ?? ReadCatalog(SupportedLanguages.Default).GetValueOrDefault(key) ?? key;
    }

    /// <summary>Fill <c>{{name}}</c> placeholders once, so a value can't become another placeholder.</summary>
    internal static string Format(string key, params (string Name, object? Value)[] values)
    {
        var parameters = values.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
        return Regex.Replace(Get(key), @"\{\{\s*(\w+)\s*}}", match =>
            parameters.TryGetValue(match.Groups[1].Value, out var value) ? Convert.ToString(value, CultureInfo.CurrentCulture) ?? "" : match.Value);
    }

    private static Dictionary<string, string> ReadCatalog(string locale)
    {
        lock (Catalogs)
        {
            if (Catalogs.TryGetValue(locale, out var catalog)) return catalog;
            try
            {
                using Stream source = typeof(UiText).Assembly.GetManifestResourceStream($"HubDisplaySwitch.Localization.{locale}.json")
                    ?? throw new InvalidDataException("Missing bundled language catalog");
                catalog = JsonSerializer.Deserialize<Dictionary<string, string>>(source) ?? [];
            }
            catch (Exception error)
            {
                Logger.Warn("Language catalog {0} unavailable: {1}", locale, error.GetType().Name);
                catalog = [];
            }
            Catalogs.Add(locale, catalog);
            return catalog;
        }
    }
}
