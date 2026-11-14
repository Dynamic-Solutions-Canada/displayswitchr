using System.Text.Json.Serialization;

namespace HubDisplaySwitch.Models;

/// <summary>A USB device (normally a hub) that activates a setup when it's connected.</summary>
public sealed class HubRef
{
    /// <summary>Full PnP instance id, e.g. USB\VID_1A40&amp;PID_0101\6&amp;2838F1DC&amp;0&amp;2.</summary>
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>False: match this hub model (VID/PID) on any port. True: only this exact port.</summary>
    public bool ExactPort { get; set; }
}

/// <summary>A display, identified by its connector device path (Id) with the friendly name as fallback.</summary>
public sealed class DisplayRef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Refresh preference in this setup: 0 = highest available, -1 = don't change, &gt;0 = that rate (or the closest lower one).</summary>
    public int RefreshRate { get; set; }
}

/// <summary>A place you use the PC from: which hub(s) trigger it, which displays are on, and where audio goes.</summary>
public sealed class Setup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New setup";
    /// <summary>Segoe Fluent Icons glyph.</summary>
    public string Glyph { get; set; } = Glyphs.Monitor;

    /// <summary>The default setup: used when no hub setup is active (devices plugged straight into the PC).</summary>
    public bool IsDefault { get; set; }
    public List<HubRef> Hubs { get; set; } = new();

    /// <summary>Displays to turn on. Everything else is turned off.</summary>
    public List<DisplayRef> Displays { get; set; } = new();
    public string MainDisplayId { get; set; } = "";
    /// <summary>Same picture on all chosen displays instead of extending.</summary>
    public bool Mirror { get; set; }

    /// <summary>Playback (output) device to make default, incl. for calls. Empty name = don't change.</summary>
    public string AudioId { get; set; } = "";
    public string AudioName { get; set; } = "";

    /// <summary>Recording (input / microphone) device to make default, incl. for calls. Empty name = don't change.</summary>
    public string MicId { get; set; } = "";
    public string MicName { get; set; } = "";

    [JsonIgnore]
    public DisplayRef? Main =>
        Displays.FirstOrDefault(d => d.Id.Length > 0 && d.Id == MainDisplayId)
        ?? Displays.FirstOrDefault(d => d.Name == MainDisplayId)
        ?? Displays.FirstOrDefault();

    /// <summary>Displays with the main one first.</summary>
    [JsonIgnore]
    public IEnumerable<DisplayRef> DisplaysMainFirst => Main is { } m ? Displays.Where(d => d != m).Prepend(m) : Displays;

    public string DescribeDisplays()
    {
        if (Displays.Count == 0) return "No displays chosen";
        if (Displays.Count == 1) return Displays[0].Name;
        var names = DisplaysMainFirst.Select(d => d.Name).ToList();
        return Mirror ? "Mirror: " + string.Join(", ", names) : string.Join(" + ", names);
    }
}

/// <summary>Segoe Fluent Icons glyphs used by the app.</summary>
public static class Glyphs
{
    public const string Monitor = "", Desktop = "", Game = "", Home = "", Keyboard = "",
                        Video = "", Music = "", Work = "", People = "", Projector = "",
                        Speaker = "", Usb = "", Settings = "";
    public static readonly string[] SetupChoices = { Desktop, Monitor, Game, Home, Keyboard, Video, Music, Work, People, Projector };
}
