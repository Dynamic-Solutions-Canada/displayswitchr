// Portable harness for SwitchEngine's switching policy (AGENTS.md "Behaviour"): which setup applies when
// the app starts and when hubs connect or disconnect, manual switches, pausing, and the parts of the
// switch sequence that decide what the user sees. It links the real engine, Setup model and hub
// matching, and drives them with the stand-ins in Fakes.cs on a virtual clock. Exits non-zero on the
// first failed check, after printing what the engine logged.
using HubDisplaySwitch.Classes.Settings;
using HubDisplaySwitch.Models;
using HubDisplaySwitch.Policy.Tests;
using HubDisplaySwitch.Services;
using Microsoft.UI.Dispatching;

// Device ids. A PnP id is "USB\VID_xxxx&PID_xxxx\<port path>"; the first two parts identify the model.
const string CouchHub = @"USB\VID_1A40&PID_0101\6&1&0&2";
const string CouchHubOtherPort = @"USB\VID_1A40&PID_0101\7&9&0&4";
const string BedroomHub = @"USB\VID_2109&PID_0817\5&3&0&1";
const string Mouse = @"USB\VID_046D&PID_C52B\5&3&0&3";

var scenarios = new List<(string Name, Action Body)>
{
    // Startup
    ("with no hub connected, the app starts on the default setup", StartsOnDefault),
    ("at startup, the setup higher in the list wins when several hubs are connected", StartupListOrderWins),
    ("nothing switches at startup when the setup is already in place", StartupAlreadyApplied),
    ("with switching on start off, the app starts without switching", StartupApplyOff),

    // Hubs connecting and disconnecting
    ("connecting a hub switches to its setup", HubConnectSwitches),
    ("the most recently connected hub wins", NewestHubWins),
    ("unplugging the newest hub returns to the previous connected hub's setup", FallBackToPreviousHub),
    ("unplugging the last hub returns to the default setup", FallBackToDefault),
    ("a hub matches its model on any port", HubMatchesAnyPort),
    ("a hub set to its exact port matches only that port", HubMatchesExactPort),
    ("a default setup's hubs never trigger it", DefaultSetupHubsIgnored),
    ("a failed USB query changes nothing", UsbQueryFails),

    // Manual switches, pausing, editing
    ("a USB change that leaves the desired setup alone keeps a manual switch", ManualSwitchSticks),
    ("a manual switch lasts until the desired setup changes", ManualSwitchEndsOnChange),
    ("while paused, connecting a hub doesn't switch", PausedDoesNotSwitch),
    ("editing setups re-ranks hubs without switching", SettingsChangedDoesNotSwitch),

    // Switch sequence
    ("a sleeping TV is waited for, then switched to", WaitsForSleepingDisplay),
    ("a display that never appears ends in an error", DisplayNeverAppears),
    ("a setup with no displays ends in an error", NoDisplaysChosen),
    ("HDMI audio that appears after the display is still made the default", LateAudioIsSet),
    ("a TV that drops back to 60 Hz is corrected afterwards", RefreshRestoredInBackground),
    ("with the popup turned off, switching shows nothing", PopupOff),
};

foreach (var (name, body) in scenarios)
{
    Fakes.Reset();
    try
    {
        body();
    }
    catch (Exception ex)
    {
        Console.WriteLine("FAIL: " + name);
        Console.WriteLine("  " + ex.Message);
        Console.WriteLine("  Engine log:");
        foreach (var line in NLog.Logger.Lines)
        {
            Console.WriteLine("    " + line);
        }

        return 1;
    }

    Console.WriteLine("PASS: " + name);
}

Console.WriteLine($"All {scenarios.Count} scenarios passed.");
return 0;

// ── Scenarios: startup ──────────────────────────────────────────────────

void StartsOnDefault()
{
    var world = new World();
    world.Engine.Start();
    world.RunSequence();

    Check(world.Engine.Active?.Name == "Desk", "the default setup is active");
    Check(world.FirstPopupReason == "App started", "the popup says the app started");
    Check(DisplayConfig.Calls.SequenceEqual(new[] { "layout:BenQ" }), "only the desk monitor was turned on");
    Check(World.Screen("BenQ").Hz == 144, "the desk monitor runs at its highest rate");
    Check(AudioDevices.Defaults[AudioFlow.Output] == "speakers", "the speakers are the default playback device");
    Check(world.LastPopup.State == OsdState.Done, "the last popup says it's done");
}

void StartupListOrderWins()
{
    UsbDevices.Present.Add(new UsbDevice(BedroomHub, "Bedroom hub"));
    UsbDevices.Present.Add(new UsbDevice(CouchHub, "Couch hub"));
    var world = new World();
    world.Engine.Start();
    world.RunSequence();

    Check(world.Engine.Active?.Name == "Couch", "Couch, listed above Bedroom, wins at startup");
    Check(world.Engine.IsHubConnected(world.Setup("Bedroom")), "Bedroom's hub is still seen as connected");
}

void StartupAlreadyApplied()
{
    var world = new World();
    var desk = World.Screen("BenQ");
    desk.Active = true;
    desk.Hz = 144;
    DisplayConfig.PrimaryId = desk.Id;
    AudioDevices.Defaults[AudioFlow.Output] = "speakers";

    world.Engine.Start();
    world.RunSequence();

    Check(world.Engine.Active?.Name == "Desk", "Desk is recorded as the active setup");
    Check(DisplayConfig.Calls.Count == 0, "the layout was left alone");
    Check(world.Popups.Count == 0, "no popup was shown");
}

void StartupApplyOff()
{
    UsbDevices.Present.Add(new UsbDevice(CouchHub, "Couch hub"));
    var world = new World();
    SettingsManager.Current.ApplyOnStart = false;
    world.Engine.Start();
    world.RunSequence();

    Check(world.Engine.Active?.Name == "Couch", "Couch is recorded as the setup that should be active");
    Check(DisplayConfig.Calls.Count == 0, "the layout was left alone");
    Check(world.Popups.Count == 0, "no popup was shown");
}

// ── Scenarios: hubs ─────────────────────────────────────────────────────

void HubConnectSwitches()
{
    var world = World.StartedOnDesk();
    world.Plug(CouchHub);

    Check(world.Engine.Active?.Name == "Couch", "Couch is active");
    Check(world.FirstPopupReason == "Hub connected", "the popup says a hub connected");
    Check(World.Screen("LG TV").Active && !World.Screen("BenQ").Active, "only the TV is on");
    Check(AudioDevices.Defaults[AudioFlow.Output] == "tv-audio", "the TV's audio is the default playback device");
}

void NewestHubWins()
{
    var world = World.StartedOnDesk();
    world.Plug(CouchHub);
    world.Plug(BedroomHub);

    Check(world.Engine.Active?.Name == "Bedroom", "Bedroom, connected last, is active");
}

void FallBackToPreviousHub()
{
    var world = World.StartedOnDesk();
    world.Plug(CouchHub);
    world.Plug(BedroomHub);
    world.Unplug(BedroomHub);

    Check(world.Engine.Active?.Name == "Couch", "Couch, whose hub is still connected, is active again");
    Check(world.FirstPopupReason == "Hub disconnected · previous setup", "the popup says it went back to the previous setup");
}

void FallBackToDefault()
{
    var world = World.StartedOnDesk();
    world.Plug(CouchHub);
    world.Unplug(CouchHub);

    Check(world.Engine.Active?.Name == "Desk", "Desk, the default, is active again");
    Check(world.FirstPopupReason == "Hub disconnected", "the popup says the hub disconnected");
}

void HubMatchesAnyPort()
{
    var world = World.StartedOnDesk();
    world.Plug(CouchHubOtherPort);

    Check(world.Engine.Active?.Name == "Couch", "the same hub model on another port switches to Couch");
}

void HubMatchesExactPort()
{
    var world = World.StartedOnDesk(couchExactPort: true);
    world.Plug(CouchHubOtherPort);
    Check(world.Engine.Active?.Name == "Desk", "the same model on another port is ignored");

    world.Plug(CouchHub);
    Check(world.Engine.Active?.Name == "Couch", "the saved port switches to Couch");
}

void DefaultSetupHubsIgnored()
{
    var world = World.StartedOnDesk();
    world.Setup("Desk").Hubs.Add(new HubRef { DeviceId = Mouse, Name = "Mouse" });
    world.Engine.Apply(world.Setup("Couch"), SwitchReason.Manual);
    world.RunSequence();
    world.Plug(Mouse);

    Check(world.Engine.Active?.Name == "Couch", "plugging in the default setup's hub doesn't switch to it");
    Check(!world.Engine.IsHubConnected(world.Setup("Desk")), "the default setup is never seen as hub-connected");
}

void UsbQueryFails()
{
    var world = World.StartedOnDesk();
    UsbDevices.FailList = true;
    world.Plug(CouchHub);

    Check(world.Engine.Active?.Name == "Desk", "Desk stays active");
    Check(NLog.Logger.Lines.Any(l => l.StartsWith("ERROR USB query failed")), "the failure is logged");
}

// ── Scenarios: manual switches, pausing, editing ────────────────────────

void ManualSwitchSticks()
{
    var world = World.StartedOnDesk();
    world.Engine.Apply(world.Setup("Couch"), SwitchReason.Manual);
    world.RunSequence();
    int layouts = DisplayConfig.Calls.Count;
    world.Plug(Mouse);

    Check(world.Engine.Active?.Name == "Couch", "Couch, chosen by hand, stays active");
    Check(DisplayConfig.Calls.Count == layouts, "the layout wasn't changed again");
}

void ManualSwitchEndsOnChange()
{
    var world = World.StartedOnDesk();
    world.Engine.Apply(world.Setup("Couch"), SwitchReason.Manual);
    world.RunSequence();
    world.Plug(BedroomHub);

    Check(world.Engine.Active?.Name == "Bedroom", "a hub connecting replaces the manual choice");
}

void PausedDoesNotSwitch()
{
    var world = World.StartedOnDesk();
    world.Engine.SetPaused(true);
    int layouts = DisplayConfig.Calls.Count;
    world.Plug(CouchHub);

    Check(SettingsManager.Current.Paused, "the paused setting is on");
    Check(SettingsManager.Saves == 1, "pausing saved the settings");
    Check(world.Engine.Active?.Name == "Desk", "Desk stays active while paused");
    Check(DisplayConfig.Calls.Count == layouts, "the layout wasn't changed");
    Check(world.Engine.IsHubConnected(world.Setup("Couch")), "the hub is still tracked while paused");
}

void SettingsChangedDoesNotSwitch()
{
    var world = World.StartedOnDesk();
    world.Plug(CouchHub);
    world.Plug(BedroomHub);
    world.Engine.Apply(world.Setup("Couch"), SwitchReason.Manual);
    world.RunSequence();
    int layouts = DisplayConfig.Calls.Count;

    world.Engine.SettingsChanged();
    world.RunSequence();
    Check(DisplayConfig.Calls.Count == layouts, "editing setups didn't switch");

    // The active setup now ranks as the most recent, so losing the other hub keeps it.
    world.Unplug(BedroomHub);
    Check(world.Engine.Active?.Name == "Couch", "Couch stays active after Bedroom's hub goes");
    Check(DisplayConfig.Calls.Count == layouts, "the layout wasn't changed");
}

// ── Scenarios: switch sequence ──────────────────────────────────────────

void WaitsForSleepingDisplay()
{
    var world = World.StartedOnDesk();
    var tv = World.Screen("LG TV");
    tv.Connected = false;
    world.Plug(CouchHub, settle: false);
    world.Queue.Advance(6_000);

    Check(world.LastPopup.Title == "Waiting for LG TV…", "the popup says it's waiting for the TV");
    Check(World.Screen("BenQ").Active, "the desk monitor is still on while waiting");

    tv.Connected = true;
    world.RunSequence();
    Check(tv.Active && !World.Screen("BenQ").Active, "the TV came on once it woke");
    Check(world.LastPopup.State == OsdState.Done, "the switch finished");
}

void DisplayNeverAppears()
{
    var world = World.StartedOnDesk();
    World.Screen("LG TV").Connected = false;
    world.Plug(CouchHub);

    Check(world.LastPopup.State == OsdState.Error, "the popup shows an error");
    Check(world.LastPopup.Title == "Couldn't switch to Couch", "the error names the setup");
    Check(World.Screen("BenQ").Active, "the desk monitor stays on");
    Check(!world.Engine.IsSwitching, "the engine stopped trying");
}

void NoDisplaysChosen()
{
    var world = World.StartedOnDesk();
    world.Setup("Couch").Displays.Clear();
    world.Plug(CouchHub);

    Check(world.LastPopup.State == OsdState.Error, "the popup shows an error");
    Check(world.LastPopup.Subtitle == "This setup has no displays chosen", "the error says why");
}

void LateAudioIsSet()
{
    var world = World.StartedOnDesk();
    var tvAudio = AudioDevices.Endpoints.Single(e => e.Id == "tv-audio");
    tvAudio.Active = false;
    world.Plug(CouchHub, settle: false);

    // Past the first settle pass, which reports the TV's refresh rate change instead.
    world.Queue.Advance(5_000);

    Check(AudioDevices.Defaults[AudioFlow.Output] == "speakers", "the speakers stay default until the TV's audio appears");
    Check(world.LastPopup.Subtitle == "Waiting for audio: LG TV", "the popup says it's waiting for the TV's audio");

    tvAudio.Active = true;
    world.RunSequence();
    Check(AudioDevices.Defaults[AudioFlow.Output] == "tv-audio", "the TV's audio became the default");
}

void RefreshRestoredInBackground()
{
    var world = World.StartedOnDesk();
    world.Plug(CouchHub, settle: false);

    // Run until the Done popup, then have the TV drop back to 60 Hz as it re-syncs.
    for (int i = 0; i < 100 && world.LastPopup.State != OsdState.Done; i++)
    {
        world.Queue.Advance(500);
    }

    var tv = World.Screen("LG TV");
    Check(tv.Hz == 120, "the TV was set to its highest rate");
    tv.Hz = 60;
    world.RunSequence();

    Check(tv.Hz == 120, "the background check set it back to 120 Hz");
    Check(world.Popups.Any(p => p.Subtitle.StartsWith("Refresh rate restored")), "the popup said the rate was restored");
}

void PopupOff()
{
    var world = World.StartedOnDesk();
    SettingsManager.Current.ShowOsd = false;
    world.Popups.Clear();
    world.Plug(CouchHub);

    Check(world.Engine.Active?.Name == "Couch", "it still switched");
    Check(world.Popups.Count == 0, "no popup was shown");
}

// ── Helpers ─────────────────────────────────────────────────────────────

static void Check(bool condition, string what)
{
    if (!condition)
    {
        throw new Exception("FAILED: " + what);
    }
}

/// <summary>
/// One scenario's world: a desk monitor, a TV and a projector; speakers, the TV's HDMI audio and a mic;
/// and three setups in list order: Desk (default), Couch (hub on the couch, TV) and Bedroom (hub in
/// the bedroom, projector). The engine records every popup.
/// </summary>
sealed class World
{
    public DispatcherQueue Queue { get; } = new();
    public SwitchEngine Engine { get; }
    public List<(string Title, string Subtitle, OsdState State)> Popups { get; } = new();

    public World(bool couchExactPort = false)
    {
        DisplayConfig.Screens.Add(new DisplayConfig.Screen("desk", "BenQ", 60, 144));
        DisplayConfig.Screens.Add(new DisplayConfig.Screen("tv", "LG TV", 60, 120));
        DisplayConfig.Screens.Add(new DisplayConfig.Screen("proj", "Projector", 60));
        AudioDevices.Endpoints.Add(new AudioDevices.Endpoint(AudioFlow.Output, "speakers", "Speakers (Realtek Audio)"));
        AudioDevices.Endpoints.Add(new AudioDevices.Endpoint(AudioFlow.Output, "tv-audio", "LG TV (AMD High Definition Audio Device)"));

        SettingsManager.Current.Setups = new List<Setup>
        {
            new()
            {
                Name = "Desk",
                IsDefault = true,
                Displays = { new DisplayRef { Id = "desk", Name = "BenQ" } },
                AudioId = "speakers",
                AudioName = "Speakers (Realtek Audio)",
            },
            new()
            {
                Name = "Couch",
                Hubs = { new HubRef { DeviceId = @"USB\VID_1A40&PID_0101\6&1&0&2", Name = "Couch hub", ExactPort = couchExactPort } },
                Displays = { new DisplayRef { Id = "tv", Name = "LG TV" } },
                AudioId = "tv-audio",
                AudioName = "LG TV (AMD High Definition Audio Device)",
            },
            new()
            {
                Name = "Bedroom",
                Hubs = { new HubRef { DeviceId = @"USB\VID_2109&PID_0817\5&3&0&1", Name = "Bedroom hub" } },
                Displays = { new DisplayRef { Id = "proj", Name = "Projector" } },
            },
        };

        Engine = new SwitchEngine(Queue);
        Engine.Popup += (title, subtitle, state) => Popups.Add((title, subtitle, state));
    }

    /// <summary>A world whose engine has started on the Desk setup and finished switching to it.</summary>
    public static World StartedOnDesk(bool couchExactPort = false)
    {
        var world = new World(couchExactPort);
        world.Engine.Start();
        world.RunSequence();
        Check(world.Engine.Active?.Name == "Desk", "the scenario starts on Desk");
        world.Popups.Clear();
        return world;
    }

    public Setup Setup(string name) => SettingsManager.Current.Setups.Single(s => s.Name == name);

    public static DisplayConfig.Screen Screen(string name) => DisplayConfig.Screens.Single(s => s.Name == name);

    public (string Title, string Subtitle, OsdState State) LastPopup =>
        Popups.Count > 0 ? Popups[^1] : ("", "", OsdState.Working);

    static readonly string[] Reasons =
    {
        SwitchReason.Manual, SwitchReason.AppStarted, SwitchReason.HubConnected,
        SwitchReason.HubDisconnected, SwitchReason.PreviousSetup,
    };

    /// <summary>The reason the latest switch gave: the subtitle of its first popup.</summary>
    public string? FirstPopupReason =>
        Popups.LastOrDefault(p => p.State == OsdState.Working && Reasons.Contains(p.Subtitle)).Subtitle;

    /// <summary>
    /// Runs well past a whole switch: up to 60 s waiting for a display, 30 s settling and 30 s of
    /// background checks.
    /// </summary>
    public void RunSequence() => Queue.Advance(180_000);

    public void Plug(string deviceId, bool settle = true)
    {
        UsbDevices.Plug(deviceId);
        if (settle)
        {
            RunSequence();
        }
    }

    public void Unplug(string deviceId)
    {
        UsbDevices.Unplug(deviceId);
        RunSequence();
    }

    static void Check(bool condition, string what)
    {
        if (!condition)
        {
            throw new Exception("FAILED: " + what);
        }
    }
}
