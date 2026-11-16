// Portable harness for the settings file (CONF-3, CONF-4): the kit's real JsonSettingsStore with the
// app's real schema steps (SettingsMigrations), settings class and setup model, run against real files
// in a temporary folder. Each scenario writes the file a real install could have, loads it the way
// SettingsManager does, and checks what the user would get back. Exits non-zero on the first failure.
using System.Text.Json.Nodes;
using HubDisplaySwitch.Classes.Settings;
using HubDisplaySwitch.Services;
using HubDisplaySwitch.ViewModels;
using WinUIAppKit.Settings;

var scenarios = new List<(string Name, Action<string> Body)>
{
    ("with no file, it is a first run and the file is written with a schema version", FirstRun),
    ("a file from before versioning keeps its setups and settings", CurrentShapeUnversioned),
    ("a saved false for a setting that defaults to true stays false", FalseStaysFalse),
    ("zero setups is a valid saved state, not an unreadable file", ZeroSetups),
    ("the two-state file becomes a default Desk setup and a hub-triggered TV setup", TwoStateFile),
    ("a file that isn't JSON is kept as .bad and defaults are written", NotJson),
    ("a JSON file of another shape is kept as .bad", OtherShape),
    ("a current file loads without a backup", CurrentVersion),
    ("a file from a newer version is backed up and loaded as far as this version understands", NewerVersion),
    ("Reset keeps the old file and writes defaults", ResetKeepsOldFile),
};

string root = Path.Combine(Path.GetTempPath(), "hds-settings-tests-" + Guid.NewGuid().ToString("N"));
try
{
    foreach (var (name, body) in scenarios)
    {
        string folder = Path.Combine(root, scenarios.FindIndex(s => s.Name == name).ToString());
        Directory.CreateDirectory(folder);
        DisplayConfig.Monitors.Clear();
        AudioDevices.Devices.Clear();
        try
        {
            body(Path.Combine(folder, "settings.json"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL: " + name);
            Console.WriteLine("  " + ex.Message);
            return 1;
        }

        Console.WriteLine("PASS: " + name);
    }
}
finally
{
    Directory.Delete(root, recursive: true);
}

Console.WriteLine($"All {scenarios.Count} scenarios passed.");
return 0;

// ── Scenarios ───────────────────────────────────────────────────────────

void FirstRun(string path)
{
    var result = Store(path).Load();

    Check(result.Outcome == SettingsLoadOutcome.FirstRun, "the outcome is FirstRun");
    Check(File.Exists(path), "the file was written");
    Check(FileVersion(path) == SettingsMigrations.Steps.Count, "the file carries the current schema version");
}

void CurrentShapeUnversioned(string path)
{
    // As written by every WinUI build before schema versions: a hand-kept Version and the setups list.
    string original = """
        {
          "Version": 2,
          "AppTheme": 2,
          "Language": "fr-FR",
          "ApplyOnStart": true,
          "ShowOsd": true,
          "Paused": true,
          "Setups": [
            { "Id": "desk", "Name": "Bureau", "IsDefault": true, "Displays": [ { "Id": "d1", "Name": "BenQ", "RefreshRate": 0 } ] },
            { "Id": "tv", "Name": "TV", "Hubs": [ { "DeviceId": "USB\\VID_1A40&PID_0101\\6&1", "Name": "Hub", "ExactPort": true } ],
              "Displays": [ { "Id": "d2", "Name": "LG TV", "RefreshRate": 120 } ], "AudioId": "a2", "AudioName": "LG TV" }
          ]
        }
        """;
    File.WriteAllText(path, original);

    var result = Store(path).Load();
    var s = result.Settings;

    Check(result.Outcome == SettingsLoadOutcome.Migrated, "the outcome is Migrated");
    Check(File.ReadAllText(path + ".v0.bak") == original, "the original file is kept as settings.json.v0.bak");
    Check(s.Setups.Select(x => x.Name).SequenceEqual(new[] { "Bureau", "TV" }), "both setups are kept, in order");
    Check(s.Setups[1].Hubs.Single().ExactPort, "the hub's exact-port choice is kept");
    Check(s.Setups[1].Displays.Single().RefreshRate == 120, "the TV's refresh rate is kept");
    Check(s.Paused && s.Language == "fr-FR" && (int)s.AppTheme == 2, "pause, language and theme are kept");

    var saved = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    Check(!saved.ContainsKey("Version"), "the old hand-kept Version is gone");
    Check(FileVersion(path) == SettingsMigrations.Steps.Count, "the file carries the current schema version");
}

void FalseStaysFalse(string path)
{
    File.WriteAllText(path, """{ "Version": 2, "ApplyOnStart": false, "ShowOsd": false, "Startup": false, "Setups": [] }""");
    var store = Store(path);
    store.Load();

    var reloaded = store.Load().Settings;
    Check(!reloaded.ApplyOnStart && !reloaded.ShowOsd && !reloaded.Startup, "false is still false after saving and loading again");
}

void ZeroSetups(string path)
{
    File.WriteAllText(path, """{ "Version": 2, "Setups": [] }""");
    var result = Store(path).Load();

    Check(result.Outcome == SettingsLoadOutcome.Migrated, "the outcome is Migrated, not Unreadable");
    Check(result.Settings.Setups.Count == 0, "there are still no setups");
    Check(!File.Exists(path + ".bad"), "nothing was kept as .bad");
}

void TwoStateFile(string path)
{
    // The first format: one hub, and what to show when it is connected or disconnected.
    DisplayConfig.Monitors.Add(new MonitorInfo("d1", "BenQ", true));
    DisplayConfig.Monitors.Add(new MonitorInfo("d2", "LG TV", false));
    AudioDevices.Devices.Add(new AudioDevice("a1", "Speakers (Realtek Audio)", true, true));
    AudioDevices.Devices.Add(new AudioDevice("a2", "LG TV (AMD High Definition Audio Device)", true, false));
    File.WriteAllText(path, """
        {
          "HubDeviceId": "USB\\VID_1A40&PID_0101\\6&1&0&2",
          "HubName": "Couch hub",
          "MatchExactPort": true,
          "ApplyOnStart": false,
          "Connected": { "MonitorName": "LG TV", "MonitorId": "d2", "Mode": "OnlyThis" },
          "Disconnected": { "MonitorName": "BenQ", "MonitorId": "d1", "Mode": "OnlyThis" },
          "RefreshRates": { "LG TV": 120 }
        }
        """);

    var result = Store(path).Load();
    var s = result.Settings;
    s.CompleteInitialization();

    Check(result.Outcome == SettingsLoadOutcome.Migrated, "the outcome is Migrated");
    Check(File.Exists(path + ".v0.bak"), "the two-state file is kept as settings.json.v0.bak");
    Check(!s.ApplyOnStart, "switching on start stays off");

    var desk = s.Setups[0];
    var tv = s.Setups[1];
    Check(desk.Name == "Desk" && desk.IsDefault && desk.Hubs.Count == 0, "Desk is the default setup");
    Check(desk.Displays.Single().Name == "BenQ" && desk.AudioId == "a1", "Desk shows the desk monitor with the speakers");
    Check(tv.Name == "TV" && !tv.IsDefault, "TV is a hub setup");
    Check(tv.Hubs.Single() is { Name: "Couch hub", ExactPort: true }, "TV is triggered by the couch hub on its exact port");
    Check(tv.Displays.Single() is { Name: "LG TV", RefreshRate: 120 }, "TV shows the TV at its saved refresh rate");
    Check(tv.AudioId == "a2", "TV uses the TV's own audio");
}

void NotJson(string path)
{
    File.WriteAllText(path, "{ this is not json");
    var result = Store(path).Load();

    Check(result.Outcome == SettingsLoadOutcome.Unreadable, "the outcome is Unreadable");
    Check(File.ReadAllText(path + ".bad") == "{ this is not json", "the file is kept as settings.json.bad");
    Check(FileVersion(path) == SettingsMigrations.Steps.Count, "defaults were written with the current schema version");
}

void OtherShape(string path)
{
    File.WriteAllText(path, """{ "Something": "else" }""");
    var result = Store(path).Load();

    Check(result.Outcome == SettingsLoadOutcome.Unreadable, "the outcome is Unreadable");
    Check(File.Exists(path + ".bad"), "the file is kept as settings.json.bad");
}

void CurrentVersion(string path)
{
    var store = Store(path);
    store.Load();
    var result = store.Load();

    Check(result.Outcome == SettingsLoadOutcome.Loaded, "the outcome is Loaded");
    Check(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.bak").Any(), "no backup was made");
}

void NewerVersion(string path)
{
    int newer = SettingsMigrations.Steps.Count + 1;
    File.WriteAllText(path, $$"""{ "SchemaVersion": {{newer}}, "Paused": true, "Setups": [ { "Id": "x", "Name": "Desk", "IsDefault": true } ], "FutureField": 1 }""");
    var result = Store(path).Load();

    Check(result.Outcome == SettingsLoadOutcome.NewerVersion, "the outcome is NewerVersion");
    Check(File.Exists($"{path}.v{newer}.bak"), "the newer file is backed up");
    Check(result.Settings.Paused && result.Settings.Setups.Single().Name == "Desk", "what this version understands is loaded");
}

void ResetKeepsOldFile(string path)
{
    var store = Store(path);
    store.Load();
    var s = store.Load().Settings;
    s.Paused = true;
    store.Save(s);

    var result = store.Reset();

    Check(result.Outcome == SettingsLoadOutcome.Reset, "the outcome is Reset");
    var kept = Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "settings.json.*.reset").Single();
    Check(File.ReadAllText(kept).Contains("\"Paused\": true"), "the old file is kept with its settings");
    Check(!store.Load().Settings.Paused, "the new file has the defaults");
}

// ── Helpers ─────────────────────────────────────────────────────────────

// The store exactly as SettingsManager creates it.
static JsonSettingsStore<UserSettings> Store(string path) => new(path, SettingsMigrations.Steps);

static int FileVersion(string path) => (int)JsonNode.Parse(File.ReadAllText(path))![JsonSettingsStore<UserSettings>.SchemaVersionProperty]!;

static void Check(bool condition, string what)
{
    if (!condition)
    {
        throw new Exception("FAILED: " + what);
    }
}
