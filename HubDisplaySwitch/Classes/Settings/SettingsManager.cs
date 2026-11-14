using HubDisplaySwitch.Models;
using HubDisplaySwitch.Services;
using HubDisplaySwitch.ViewModels;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace HubDisplaySwitch.Classes.Settings;

/// <summary>
/// Manages app settings, serialized as JSON to %AppData%\HubDisplaySwitch\settings.json
/// (redirected to package data when packaged).
/// </summary>
public static class SettingsManager
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    internal static string SettingsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HubDisplaySwitch");

    private static string SettingsFilePath => Path.Combine(SettingsDir, "settings.json");

    /// <summary>
    /// The real on-disk folder for settings and logs, for handing to another process (e.g. Explorer).
    /// When packaged, %AppData% writes are redirected into the package's LocalCache\Roaming (MSIX VFS),
    /// which other processes don't see through <see cref="SettingsDir"/>.
    /// </summary>
    internal static string PhysicalSettingsDir
    {
        get
        {
            if (MainWindow.IsPackaged)
            {
                try
                {
                    return Path.Combine(global::Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path, "Roaming", "HubDisplaySwitch");
                }
                catch { }
            }
            return SettingsDir;
        }
    }

    // Not WhenWritingDefault: several bools default to true, so an omitted false would load back as true.
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null, // PascalCase to match property names
    };

    private static UserSettings _current = new();

    /// <summary>The current user settings.</summary>
    public static UserSettings Current
    {
        get => _current ??= new UserSettings();
        set => _current = value;
    }

    /// <summary>True when no settings file existed and first-run defaults were created.</summary>
    public static bool IsFirstRun { get; private set; }

    /// <summary>Restores settings from disk, migrating the earlier two-state format, or creates first-run defaults.</summary>
    public static UserSettings RestoreSettings(string? filePath = null)
    {
        filePath ??= SettingsFilePath;
        try
        {
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                var node = JsonNode.Parse(json)?.AsObject();
                if (node != null && node.ContainsKey("Setups"))
                {
                    var deserialized = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);
                    if (deserialized != null)
                    {
                        _current = deserialized;
                        _current.CompleteInitialization();
                        Logger.Info("Settings restored ({Count} setups)", _current.Setups.Count);
                        return _current;
                    }
                }
                else if (node != null && node.ContainsKey("Connected"))
                {
                    File.Copy(filePath, filePath + ".v1.bak", overwrite: true);
                    _current = MigrateV1(node);
                    _current.CompleteInitialization();
                    SaveSettings(filePath);
                    Logger.Info("Migrated two-state settings to setups");
                    return _current;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error restoring settings");
        }

        Logger.Warn("Settings not found or unreadable — creating first-run defaults");
        IsFirstRun = true;
        _current = CreateFirstRun();
        _current.CompleteInitialization();
        SaveSettings(filePath);
        return _current;
    }

    /// <summary>Saves the current settings to disk.</summary>
    public static void SaveSettings(string? filePath = null)
    {
        filePath ??= SettingsFilePath;
        try
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (directory != null && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string json = JsonSerializer.Serialize(_current, JsonOptions);
            string tmp = filePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error saving settings");
        }
    }

    /// <summary>First run: one default setup with whatever displays and audio are in use right now.</summary>
    private static UserSettings CreateFirstRun()
    {
        var s = new UserSettings();
        var desk = new Setup { Name = "Desk", Glyph = Glyphs.Desktop, IsDefault = true };
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
