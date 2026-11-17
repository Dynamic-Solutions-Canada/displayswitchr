using HubDisplaySwitch.Models;
using HubDisplaySwitch.Services;
using HubDisplaySwitch.ViewModels;
using System.IO;
using WinUIAppKit;
using WinUIAppKit.Localization;
using WinUIAppKit.Settings;
using WinUIAppKit.Theming;

namespace HubDisplaySwitch.Classes.Settings;

/// <summary>
/// The app's settings: %AppData%\HubDisplaySwitch\settings.json (redirected into package data when
/// packaged), loaded and saved atomically by the kit's versioned <see cref="JsonSettingsStore{T}"/>
/// (CONF-3): it stamps <c>SchemaVersion</c>, runs <see cref="SettingsMigrations"/> on older files after
/// backing them up, and writes defaults (only nulls are omitted, because several bools default to
/// true). Edits apply and save immediately.
/// </summary>
public static class SettingsManager
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>Folder name under %AppData% for settings and logs (named to the shared NLog config in <see cref="App"/>).</summary>
    public const string DataFolderName = "HubDisplaySwitch";

    private static readonly JsonSettingsStore<UserSettings> Store =
        new(Path.Combine(AppEnvironment.DataDirectory(DataFolderName), "settings.json"), SettingsMigrations.Steps);

    /// <summary>The current user settings. Valid after <see cref="RestoreSettings"/>; replaced by <see cref="Reset"/>.</summary>
    public static UserSettings Current { get; private set; } = new();

    /// <summary>True when no settings file existed and first-run defaults were created.</summary>
    public static bool IsFirstRun { get; private set; }

    /// <summary>
    /// The load result when the file was unreadable and the user has not been told yet (CONF-2); null
    /// otherwise. Taken once by the Settings page through <see cref="TakeResetNotice"/>.
    /// </summary>
    private static SettingsLoadResult<UserSettings>? _pendingResetNotice;

    /// <summary>True on the launch that found the settings file unreadable, until the Settings page shows the notice.</summary>
    public static bool NeedsResetNotice => _pendingResetNotice != null;

    /// <summary>Hand the pending reset notice to the page that shows it, once; null when there is none.</summary>
    public static SettingsLoadResult<UserSettings>? TakeResetNotice()
    {
        var notice = _pendingResetNotice;
        _pendingResetNotice = null;
        return notice;
    }

    /// <summary>
    /// Restores settings from disk, upgrading older files, or creates first-run defaults; applies the
    /// saved theme. Never throws: the store keeps an unusable file as <c>.bad</c> and returns defaults,
    /// so the app still starts. Called after the single instance is claimed, so a second launch that
    /// only signals the first never touches the file.
    /// </summary>
    public static void RestoreSettings()
    {
        var result = Store.Load();
        Current = result.Settings;
        bool changed = false;

        switch (result.Outcome)
        {
            case SettingsLoadOutcome.FirstRun:
                Logger.Info("No settings yet; creating first-run defaults");
                IsFirstRun = true;
                Current = CreateDefaults();
                changed = true;
                break;

            case SettingsLoadOutcome.Unreadable:
                Logger.Warn("Settings unusable; recreating defaults");
                _pendingResetNotice = result.NeedsResetNotice ? result : null;
                Current = CreateDefaults();
                changed = true;
                break;

            default:
                // Loaded, Migrated or NewerVersion. Zero setups is a valid saved state.
                Logger.Info("Settings restored ({Outcome}, {Count} setups)", result.Outcome, Current.Setups?.Count ?? 0);
                break;
        }

        changed |= Current.CompleteInitialization();
        AppTheme.Set(Current.AppTheme);
        if (changed)
        {
            SaveSettings();
        }
    }

    /// <summary>
    /// "Reset settings" (CONF-4): keeps the current file as <c>settings.json.&lt;UTC time&gt;.reset</c>,
    /// then starts again from the first-run defaults (one default setup for the displays and audio in
    /// use now). Applies the default theme and language at once. Start with Windows is Windows' state
    /// and is left as it is. Callers refresh their views and call
    /// <c>MainWindow.Engine.SettingsChanged()</c>.
    /// </summary>
    public static void Reset()
    {
        Store.Reset();
        Current = CreateDefaults();
        Current.CompleteInitialization();
        AppTheme.Set(Current.AppTheme);
        UiText.SetOverride(Current.Language);
        SaveSettings();
    }

    /// <summary>Saves the current settings to disk.</summary>
    public static void SaveSettings() => Store.Save(Current);

    /// <summary>The defaults: one default setup with whatever displays and audio are in use right now.</summary>
    private static UserSettings CreateDefaults()
    {
        var s = new UserSettings();
        var desk = new Setup { Name = UiText.Get("Desk"), Glyph = Glyphs.Desktop, IsDefault = true };
        try
        {
            foreach (var m in DisplayConfig.GetMonitors().Where(m => m.Active))
            {
                desk.Displays.Add(new DisplayRef { Id = m.Id, Name = m.Name });
            }

            desk.MainDisplayId = desk.Displays.FirstOrDefault()?.Id ?? "";
            if (AudioDevices.CurrentDefault() is { } a)
            {
                desk.AudioId = a.Id;
                desk.AudioName = a.Name;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "First-run detection failed");
        }

        s.Setups.Add(desk);
        return s;
    }
}
