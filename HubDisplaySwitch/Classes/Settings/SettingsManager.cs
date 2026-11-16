using HubDisplaySwitch.Models;
using HubDisplaySwitch.Services;
using HubDisplaySwitch.ViewModels;
using System.IO;
using System.Text.Json.Nodes;
using WinUIAppKit;
using WinUIAppKit.Settings;
using WinUIAppKit.Theming;

namespace HubDisplaySwitch.Classes.Settings;

/// <summary>
/// The app's settings: %AppData%\HubDisplaySwitch\settings.json (redirected into package data when
/// packaged), loaded and saved atomically by the kit's <see cref="JsonSettingsStore{T}"/> (nulls omitted,
/// defaults written: several bools default to true, so an omitted false would load back as true).
/// Also migrates the first, two-state settings file. Edits apply and save immediately.
/// </summary>
public static class SettingsManager
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>Folder name under %AppData% for settings and logs (must match NLog.config).</summary>
    public const string DataFolderName = "HubDisplaySwitch";

    private static readonly JsonSettingsStore<UserSettings> Store =
        new(Path.Combine(AppEnvironment.DataDirectory(DataFolderName), "settings.json"));

    /// <summary>
    /// The real on-disk folder for settings and logs, for handing to another process (e.g. Explorer):
    /// packaged %AppData% writes land in the package's LocalCache\Roaming.
    /// </summary>
    internal static string PhysicalSettingsDir => AppEnvironment.PhysicalDataDirectory(DataFolderName);

    /// <summary>The current user settings. Valid after <see cref="RestoreSettings"/>.</summary>
    public static UserSettings Current { get; private set; } = new();

    /// <summary>True when no settings file existed and first-run defaults were created.</summary>
    public static bool IsFirstRun { get; private set; }

    /// <summary>True when the file couldn't be used and defaults replaced it (shown to the user; not a first run).</summary>
    public static bool WasReset { get; private set; }

    /// <summary>
    /// Restores settings from disk, migrating the earlier two-state format, or creates first-run defaults;
    /// applies the saved theme. Never throws: anything unusable falls back to defaults so the app still starts.
    /// Writes the file only when it changed (first run, reset, migration), so a second instance that
    /// launches just to signal the first doesn't rewrite it.
    /// </summary>
    public static void RestoreSettings()
    {
        bool changed;
        try
        {
            var (node, unreadable) = ReadNode();
            if (node != null && !node.ContainsKey("Setups") && node.ContainsKey("Connected"))
            {
                File.Copy(Store.FilePath, Store.FilePath + ".v1.bak", overwrite: true);
                Current = MigrateV1(node);
                Logger.Info("Migrated two-state settings to setups");
                changed = true;
            }
            else if (unreadable || (node != null && !node.ContainsKey("Setups")))
            {
                Store.Load(out _);   // logs the problem and keeps the file as .bad
                Logger.Warn("Settings unusable; recreating defaults");
                Current = CreateFirstRun();
                WasReset = changed = true;
            }
            else
            {
                Current = Store.Load(out bool firstRun);
                IsFirstRun = firstRun;
                if (firstRun)
                {
                    Logger.Info("No settings yet; creating first-run defaults");
                    Current = CreateFirstRun();
                }
                else Logger.Info("Settings restored ({Count} setups)", Current.Setups?.Count ?? 0);   // zero setups is valid
                changed = firstRun;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Couldn't restore settings; using defaults");
            Current = CreateFirstRun();
            WasReset = changed = true;
        }

        changed |= Current.CompleteInitialization();
        AppTheme.Set(Current.AppTheme);
        if (changed) SaveSettings();
    }

    /// <summary>The settings file as JSON (null when missing), or unreadable when it exists but isn't a JSON object.</summary>
    private static (JsonObject? Node, bool Unreadable) ReadNode()
    {
        if (!File.Exists(Store.FilePath)) return (null, false);
        try
        {
            return JsonNode.Parse(File.ReadAllText(Store.FilePath)) is JsonObject node ? (node, false) : (null, true);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Settings file isn't valid JSON");
            return (null, true);
        }
    }

    /// <summary>Saves the current settings to disk.</summary>
    public static void SaveSettings() => Store.Save(Current);

    /// <summary>First run: one default setup with whatever displays and audio are in use right now.</summary>
    private static UserSettings CreateFirstRun()
    {
        var s = new UserSettings();
        var desk = new Setup { Name = WinUIAppKit.Localization.UiText.Get("Desk"), Glyph = Glyphs.Desktop, IsDefault = true };
        try
        {
            foreach (var m in DisplayConfig.GetMonitors().Where(m => m.Active))
                desk.Displays.Add(new DisplayRef { Id = m.Id, Name = m.Name });
            desk.MainDisplayId = desk.Displays.FirstOrDefault()?.Id ?? "";
            if (AudioDevices.CurrentDefault() is { } a) { desk.AudioId = a.Id; desk.AudioName = a.Name; }
        }
        catch (Exception ex) { Logger.Warn(ex, "First-run detection failed"); }
        s.Setups.Add(desk);
        return s;
    }

    /// <summary>
    /// The first (unpackaged) version had one hub with "connected"/"disconnected" actions.
    /// Turn that into a default "Desk" setup plus a hub-triggered "TV" setup.
    /// </summary>
    private static UserSettings MigrateV1(JsonObject v1)
    {
        var s = new UserSettings
        {
            ApplyOnStart = (bool?)v1["ApplyOnStart"] ?? true,
            ShowOsd = (bool?)v1["ShowOsd"] ?? true,
            Paused = (bool?)v1["Paused"] ?? false,
        };
        if (v1["RefreshRates"] is JsonObject rr)
            foreach (var (k, v) in rr) if (v != null) s.RefreshRates[k] = (int)v;

        List<AudioDevice> audio;
        try { audio = AudioDevices.List(); } catch { audio = new(); }
        List<MonitorInfo> monitors;
        try { monitors = DisplayConfig.GetMonitors(); } catch { monitors = new(); }

        Setup FromAction(JsonNode? a, string name, string glyph)
        {
            var setup = new Setup { Name = name, Glyph = glyph };
            string monName = (string?)a?["MonitorName"] ?? "", monId = (string?)a?["MonitorId"] ?? "";
            string mode = (string?)a?["Mode"] ?? "OnlyThis";
            if (monName.Length > 0) setup.Displays.Add(new DisplayRef { Id = monId, Name = monName });
            setup.MainDisplayId = monId.Length > 0 ? monId : monName;
            if (mode is "ExtendAsMain" or "Duplicate")
            {
                foreach (var m in monitors.Where(m => m.Id != monId && m.Name != monName))
                    setup.Displays.Add(new DisplayRef { Id = m.Id, Name = m.Name });
                setup.Mirror = mode == "Duplicate";
            }
            // Audio: a device named after the display (HDMI/DisplayPort audio), if there is one.
            // Prefer active devices: stale endpoints from old driver installs often keep the display's name.
            var match = audio.Where(d => monName.Length > 0 && d.Name.Contains(monName, StringComparison.OrdinalIgnoreCase))
                             .OrderByDescending(d => d.Active).FirstOrDefault();
            if (match != null) { setup.AudioId = match.Id; setup.AudioName = match.Name; }
            return setup;
        }

        var desk = FromAction(v1["Disconnected"], "Desk", Glyphs.Desktop);
        desk.IsDefault = true;
        string tvName = (string?)v1["Connected"]?["MonitorName"] ?? "\u0000";
        if (desk.AudioId.Length == 0 && audio.FirstOrDefault(d => d.IsDefault) is { } cur &&
            !cur.Name.Contains(tvName, StringComparison.OrdinalIgnoreCase))
        { desk.AudioId = cur.Id; desk.AudioName = cur.Name; }

        var tv = FromAction(v1["Connected"], "TV", Glyphs.Game);
        string hubId = (string?)v1["HubDeviceId"] ?? "";
        if (hubId.Length > 0)
            tv.Hubs.Add(new HubRef { DeviceId = hubId, Name = (string?)v1["HubName"] ?? "USB hub", ExactPort = (bool?)v1["MatchExactPort"] ?? false });

        s.Setups.Add(desk);
        s.Setups.Add(tv);
        return s;
    }
}
