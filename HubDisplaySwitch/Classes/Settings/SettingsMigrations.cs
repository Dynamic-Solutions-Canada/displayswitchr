using HubDisplaySwitch.Models;
using HubDisplaySwitch.Services;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace HubDisplaySwitch.Classes.Settings;

/// <summary>
/// The settings file's schema steps, run in order by the kit's versioned <c>JsonSettingsStore</c>
/// (CONF-3). The step at index n upgrades a file from schema version n to n + 1, editing its JSON in
/// place. A file written before versioning has no <c>SchemaVersion</c> and counts as version 0,
/// whatever its shape. Steps are never removed or reordered once shipped.
/// </summary>
/// <remarks>
/// Kept free of WinUI and the kit's app types so the portable settings harness can run every step
/// against real files. A step that throws makes the store keep the file as <c>.bad</c> and write
/// defaults, so a step throws only for a file it cannot make sense of.
/// </remarks>
public static class SettingsMigrations
{
    /// <summary>Every step, in order; the current schema version is the count.</summary>
    public static IReadOnlyList<Action<JsonObject>> Steps { get; } = [ToVersion1];

    /// <summary>
    /// 0 to 1: the first versioned schema. Two shapes of unversioned file exist:
    /// <list type="bullet">
    /// <item>The setups format (any build of the WinUI app): kept as it is, minus the hand-kept
    /// <c>Version</c> number that <c>SchemaVersion</c> replaces.</item>
    /// <item>The first, two-state format (<c>Connected</c> / <c>Disconnected</c>, from the PowerShell and
    /// WinForms builds): converted into a default "Desk" setup plus a hub-triggered "TV" setup.</item>
    /// </list>
    /// Anything else is not a settings file this app wrote.
    /// </summary>
    /// <exception cref="InvalidDataException">The file has neither shape.</exception>
    public static void ToVersion1(JsonObject root)
    {
        root.Remove("Version");

        // Zero setups (an empty or null list) is a valid saved state, not an unreadable file.
        if (root.ContainsKey("Setups"))
        {
            return;
        }

        if (!root.ContainsKey("Connected"))
        {
            throw new InvalidDataException("The settings file has neither setups nor the two-state format.");
        }

        var converted = TwoStateToSetups(root);
        root.Clear();
        foreach (var (key, value) in converted)
        {
            root[key] = value?.DeepClone();
        }
    }

    /// <summary>
    /// Converts the two-state file. Each state named one display and how to show it; the display's own
    /// audio device (HDMI or DisplayPort audio) becomes the setup's playback device when there is one.
    /// Reads the displays and audio devices present now, so the setups match this PC.
    /// </summary>
    private static JsonObject TwoStateToSetups(JsonObject v1)
    {
        List<AudioDevice> audio;
        try
        {
            audio = AudioDevices.List();
        }
        catch
        {
            audio = new();
        }

        List<MonitorInfo> monitors;
        try
        {
            monitors = DisplayConfig.GetMonitors();
        }
        catch
        {
            monitors = new();
        }

        var desk = FromAction(v1["Disconnected"], "Desk", Glyphs.Desktop, monitors, audio);
        desk.IsDefault = true;

        // The desk keeps the current default playback device when its display has none of its own,
        // unless that device belongs to the TV.
        string tvName = (string?)v1["Connected"]?["MonitorName"] ?? "\u0000";
        if (desk.AudioId.Length == 0 && audio.FirstOrDefault(d => d.IsDefault) is { } current &&
            !current.Name.Contains(tvName, StringComparison.OrdinalIgnoreCase))
        {
            desk.AudioId = current.Id;
            desk.AudioName = current.Name;
        }

        var tv = FromAction(v1["Connected"], "TV", Glyphs.Game, monitors, audio);
        string hubId = (string?)v1["HubDeviceId"] ?? "";
        if (hubId.Length > 0)
        {
            tv.Hubs.Add(new HubRef
            {
                DeviceId = hubId,
                Name = (string?)v1["HubName"] ?? "USB hub",
                ExactPort = (bool?)v1["MatchExactPort"] ?? false,
            });
        }

        var refreshRates = new JsonObject();
        if (v1["RefreshRates"] is JsonObject rates)
        {
            foreach (var (name, rate) in rates)
            {
                if (rate != null)
                {
                    refreshRates[name] = (int)rate;
                }
            }
        }

        var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        return new JsonObject
        {
            ["ApplyOnStart"] = (bool?)v1["ApplyOnStart"] ?? true,
            ["ShowOsd"] = (bool?)v1["ShowOsd"] ?? true,
            ["Paused"] = (bool?)v1["Paused"] ?? false,
            ["RefreshRates"] = refreshRates,
            ["Setups"] = JsonSerializer.SerializeToNode(new List<Setup> { desk, tv }, options),
        };
    }

    /// <summary>One two-state action ("show the desktop on this display") as a setup.</summary>
    private static Setup FromAction(JsonNode? action, string name, string glyph, List<MonitorInfo> monitors, List<AudioDevice> audio)
    {
        var setup = new Setup { Name = name, Glyph = glyph };
        string monName = (string?)action?["MonitorName"] ?? "";
        string monId = (string?)action?["MonitorId"] ?? "";
        string mode = (string?)action?["Mode"] ?? "OnlyThis";
        if (monName.Length > 0)
        {
            setup.Displays.Add(new DisplayRef { Id = monId, Name = monName });
        }

        setup.MainDisplayId = monId.Length > 0 ? monId : monName;
        if (mode is "ExtendAsMain" or "Duplicate")
        {
            foreach (var m in monitors.Where(m => m.Id != monId && m.Name != monName))
            {
                setup.Displays.Add(new DisplayRef { Id = m.Id, Name = m.Name });
            }

            setup.Mirror = mode == "Duplicate";
        }

        // Prefer active devices: stale endpoints from old driver installs often keep the display's name.
        var match = audio
            .Where(d => monName.Length > 0 && d.Name.Contains(monName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.Active)
            .FirstOrDefault();
        if (match != null)
        {
            setup.AudioId = match.Id;
            setup.AudioName = match.Name;
        }

        return setup;
    }
}
