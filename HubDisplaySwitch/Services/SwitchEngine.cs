using HubDisplaySwitch.Classes.Settings;
using WinUIAppKit.Localization;
using HubDisplaySwitch.Models;
using HubDisplaySwitch.ViewModels;
using Microsoft.UI.Dispatching;

namespace HubDisplaySwitch.Services;

public enum OsdState { Working, Done, Error }

/// <summary>Why a switch happened. The values are <see cref="UiText"/> keys: logged as-is, translated in the popup.</summary>
public static class SwitchReason
{
    public const string Manual = "Switched manually", AppStarted = "App started", HubConnected = "Hub connected",
                        HubDisconnected = "Hub disconnected", PreviousSetup = "Hub disconnected · previous setup";
}

/// <summary>
/// Watches USB hubs, decides which setup should be active, and runs the switch sequence:
///   Apply layout ─► (wait for display ⟲) ─► Settle: refresh rates + audio until stable ─► Done
///                ─► Background: keep re-checking refresh/audio quietly for ~30 s.
/// Rules: the most recently connected hub's setup wins; when its hub goes, fall back to the previous
/// still-connected hub's setup, then the default setup. At startup, setups higher in the list win.
/// Runs on the UI thread (DispatcherQueue timers). Reads settings live from <see cref="SettingsManager.Current"/>.
/// </summary>
public sealed class SwitchEngine : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    static UserSettings Settings => SettingsManager.Current;

    /// <summary>The setup most recently applied (automatically or manually).</summary>
    public Setup? Active { get; private set; }
    public bool IsSwitching => _phase is Phase.Apply or Phase.WaitForDisplay or Phase.Settle;

    public event Action? StateChanged;
    public event Action<string, string, OsdState>? Popup;

    readonly DispatcherQueue _dispatcher;
    readonly UsbDevices? _usb;
    readonly DispatcherQueueTimer _debounce;
    readonly DispatcherQueueTimer _step;

    /// <summary>Setup id → when its hub was connected (for "last connected wins").</summary>
    readonly Dictionary<string, DateTime> _connectedAt = new();
    string? _desiredId;

    enum Phase { Idle, Apply, WaitForDisplay, Settle, Background }
    Phase _phase = Phase.Idle;
    Setup? _target;
    string _reason = "";
    int _attempts, _ticks, _stable;
    bool _outDone, _micDone;
    bool AudioDone => _outDone && _micDone;

    const int ApplyDelayMs = 250;                           // let the popup paint before screens go blank
    const int DisplayRetryMs = 2500, DisplayRetries = 24;  // wait up to ~60 s for a TV to wake
    const int SettleMs = 1500, SettleStable = 2, SettleMaxTicks = 20;
    const int BackgroundMs = 3000, BackgroundTicks = 10, BackgroundStable = 4;

    public SwitchEngine(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _debounce = dispatcher.CreateTimer();
        _debounce.Interval = TimeSpan.FromMilliseconds(400);
        _debounce.IsRepeating = false;
        _debounce.Tick += (_, _) => CheckHubs(initial: false);

        _step = dispatcher.CreateTimer();
        _step.Tick += (_, _) => Step();

        try
        {
            _usb = new UsbDevices();
            _usb.Changed += () => _dispatcher.TryEnqueue(() => { _debounce.Stop(); _debounce.Start(); });
        }
        catch (Exception ex) { Logger.Error(ex, "Couldn't watch USB devices"); }
    }

    public void Start() => CheckHubs(initial: true);

    // ---- hubs ----------------------------------------------------------------------------------
    public bool IsHubConnected(Setup s) => _connectedAt.ContainsKey(s.Id);

    void CheckHubs(bool initial)
    {
        List<UsbDevice> devices;
        try { devices = UsbDevices.List(); }
        catch (Exception ex) { Logger.Error(ex, "USB query failed"); return; }

        var hubSetups = Settings.Setups.Where(s => !s.IsDefault && s.Hubs.Count > 0).ToList();
        string? newlyConnected = null;
        for (int i = 0; i < hubSetups.Count; i++)
        {
            var s = hubSetups[i];
            bool present = s.Hubs.Any(h => UsbDevices.IsPresent(devices, h.DeviceId, h.ExactPort));
            if (present && !_connectedAt.ContainsKey(s.Id))
            {
                // At startup, order by list position (higher in the list = more recent = wins).
                _connectedAt[s.Id] = initial ? DateTime.MinValue.AddMinutes(hubSetups.Count - i) : DateTime.Now;
                if (!initial) { Logger.Info("Hub for '{Setup}' connected", s.Name); newlyConnected = s.Id; }
            }
            else if (!present && _connectedAt.Remove(s.Id))
            {
                Logger.Info("Hub for '{Setup}' disconnected", s.Name);
            }
        }
        foreach (var gone in _connectedAt.Keys.Where(id => hubSetups.All(s => s.Id != id)).ToList())
        {
            _connectedAt.Remove(gone);
        }

        var desired = Desired();
        if (initial)
        {
            _desiredId = desired?.Id;
            Logger.Info("Start: hubs connected for [{Hubs}]; setup = {Setup}",
                string.Join(", ", hubSetups.Where(IsHubConnected).Select(s => s.Name)), desired?.Name ?? "(none)");
            if (desired != null && Settings.ApplyOnStart && !Settings.Paused && !IsAlreadyApplied(desired))
            {
                Apply(desired, SwitchReason.AppStarted);
            }
            else
            {
                Active ??= desired;
                if (desired != null)
                {
                    Logger.Info("'{Setup}' is already in place; nothing to switch", desired.Name);
                }
            }
        }
        else if (desired?.Id != _desiredId)
        {
            _desiredId = desired?.Id;
            if (desired != null && !Settings.Paused)
            {
                Apply(desired, desired.Id == newlyConnected ? SwitchReason.HubConnected
                             : desired.IsDefault ? SwitchReason.HubDisconnected : SwitchReason.PreviousSetup);
            }
        }
        StateChanged?.Invoke();
    }

    /// <summary>
    /// True when the setup is fully in effect already: exactly its displays are on, its main display is
    /// primary, refresh rates match its preferences, and its playback/recording devices are the defaults.
    /// Used at startup so restarts (sign-in, updates) don't switch or show a popup when nothing would change.
    /// </summary>
    static bool IsAlreadyApplied(Setup s)
    {
        try
        {
            var active = DisplayConfig.GetMonitors().Where(m => m.Active).ToList();
            bool Matches(MonitorInfo m, DisplayRef d) =>
                (d.Id.Length > 0 && d.Id.Equals(m.Id, StringComparison.OrdinalIgnoreCase)) || d.Name.Equals(m.Name, StringComparison.OrdinalIgnoreCase);

            if (s.Displays.Count == 0 || active.Count != s.Displays.Count)
            {
                return false;
            }

            if (!s.Displays.All(d => active.Any(m => Matches(m, d))))
            {
                return false;
            }

            if (s.Mirror && s.Displays.Count > 1)
            {
                return false;   // can't cheaply verify cloning; just apply
            }

            if (s.Displays.Count > 1 && s.Main is { } main && !DisplayConfig.IsPrimary(main.Id, main.Name))
            {
                return false;
            }

            foreach (var m in active)
            {
                var d = s.Displays.First(x => Matches(m, x));
                if (DisplayConfig.RefreshRateNeedsChange(m.Id, m.Name, d.RefreshRate))
                {
                    return false;
                }
            }

            bool IsDefault(AudioFlow flow, string id, string name) =>
                name.Length == 0 ||
                AudioDevices.CurrentDefault(flow) is { } cur &&
                ((id.Length > 0 && cur.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) || cur.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return IsDefault(AudioFlow.Output, s.AudioId, s.AudioName) && IsDefault(AudioFlow.Input, s.MicId, s.MicName);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Couldn't check whether '{Setup}' is already applied", s.Name);
            return false;
        }
    }

    Setup? Desired() =>
        _connectedAt.OrderByDescending(kv => kv.Value)
                    .Select(kv => Settings.Setups.FirstOrDefault(s => s.Id == kv.Key))
                    .FirstOrDefault(s => s != null)
        ?? Settings.DefaultSetup;

    // ---- settings / pause ----------------------------------------------------------------------
    /// <summary>Call after setups were edited: re-evaluates hubs without switching right away.</summary>
    public void SettingsChanged()
    {
        if (Active != null)
        {
            Active = Settings.Setups.FirstOrDefault(x => x.Id == Active.Id) ?? Active;
        }

        _connectedAt.Clear();
        try
        {
            var devices = UsbDevices.List();
            var hubSetups = Settings.Setups.Where(s => !s.IsDefault && s.Hubs.Count > 0).ToList();
            for (int i = 0; i < hubSetups.Count; i++)
            {
                if (hubSetups[i].Hubs.Any(h => UsbDevices.IsPresent(devices, h.DeviceId, h.ExactPort)))
                {
                    _connectedAt[hubSetups[i].Id] = hubSetups[i].Id == Active?.Id
                        ? DateTime.Now : DateTime.MinValue.AddMinutes(hubSetups.Count - i);
                }
            }
        }
        catch (Exception ex) { Logger.Error(ex, "USB query failed"); }
        _desiredId = Desired()?.Id;
        StateChanged?.Invoke();
    }

    public void SetPaused(bool paused)
    {
        Settings.Paused = paused;
        SettingsManager.SaveSettings();
        Logger.Info(paused ? "Auto-switching paused" : "Auto-switching resumed");
        StateChanged?.Invoke();
    }

    // ---- switch sequence -----------------------------------------------------------------------
    /// <param name="reason">One of <see cref="SwitchReason"/>.</param>
    public void Apply(Setup setup, string reason)
    {
        _target = setup;
        Active = setup;
        _reason = reason;
        _attempts = 0;
        _outDone = setup.AudioName.Length == 0;
        _micDone = setup.MicName.Length == 0;
        Show(SwitchingTo(setup), UiText.Get(reason), OsdState.Working);
        Go(Phase.Apply, ApplyDelayMs);
        StateChanged?.Invoke();
    }

    void Go(Phase phase, int delayMs)
    {
        _phase = phase;
        _ticks = 0; _stable = 0;
        _step.Stop();
        _step.Interval = TimeSpan.FromMilliseconds(Math.Max(1, delayMs));
        _step.IsRepeating = true;
        _step.Start();
    }

    void Finish() { _step.Stop(); _phase = Phase.Idle; StateChanged?.Invoke(); }

    void Step()
    {
        var s = _target;
        if (s == null) { Finish(); return; }

        switch (_phase)
        {
            case Phase.Apply:
            case Phase.WaitForDisplay:
                _step.Stop();
                try
                {
                    bool lastTry = _attempts >= DisplayRetries - 1;
                    ApplyLayout(s, skipMissing: lastTry);
                    Logger.Info("Switched to '{Setup}': {Displays} ({Reason})", s.Name, s.DescribeDisplays(), _reason);
                    Show(SwitchingTo(s), UiText.Get("Setting up displays…"), OsdState.Working);
                    Go(Phase.Settle, SettleMs);
                }
                catch (MonitorNotFoundException ex) when (++_attempts < DisplayRetries)
                {
                    if (_attempts == 1)
                    {
                        Logger.Info("{Message}; waiting for it", ex.Message);
                    }

                    Show(UiText.Format("Waiting for {{display}}…", ("display", ex.DisplayName)), UiText.Get("Turn it on if it's off"), OsdState.Working);
                    Go(Phase.WaitForDisplay, DisplayRetryMs);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Couldn't switch to '{Setup}'", s.Name);
                    string detail = ex switch
                    {
                        MonitorNotFoundException missing => UiText.Format("{{display}} isn't connected or is switched off", ("display", missing.DisplayName)),
                        NoDisplaysChosenException => UiText.Get("This setup has no displays chosen"),
                        _ => ex.Message,   // a Windows error code; not worth translating
                    };
                    Show(UiText.Format("Couldn't switch to {{setup}}", ("setup", s.Name)), detail, OsdState.Error);
                    Finish();
                }
                break;

            case Phase.Settle:
                {
                    _ticks++;
                    var changes = RefreshPass(s);
                    string? audioMsg = AudioPass(s, quiet: false);
                    if (changes.Count > 0 || audioMsg != null)
                    {
                        _stable = 0;
                        Show(SwitchingTo(s), audioMsg ?? UiText.Format("Refresh rate: {{change}}", ("change", changes[^1])), OsdState.Working);
                    }
                    else if (AudioDone)
                    {
                        _stable++;
                    }
                    else
                    {
                        Show(SwitchingTo(s),
                              !_outDone ? UiText.Format("Waiting for audio: {{device}}", ("device", ShortAudio(s.AudioName)))
                                        : UiText.Format("Waiting for microphone: {{device}}", ("device", ShortAudio(s.MicName))),
                              OsdState.Working);
                    }

                    if (_stable >= SettleStable || _ticks >= SettleMaxTicks)
                    {
                        if (!_outDone)
                        {
                            Logger.Warn("Playback device '{Audio}' didn't become available", s.AudioName);
                        }

                        if (!_micDone)
                        {
                            Logger.Warn("Recording device '{Mic}' didn't become available", s.MicName);
                        }

                        Show(s.Name, Summary(s), OsdState.Done);
                        Go(Phase.Background, BackgroundMs);
                        StateChanged?.Invoke();
                    }
                    break;
                }

            case Phase.Background:
                {
                    _ticks++;
                    var changes = RefreshPass(s);
                    string? audioMsg = AudioDone ? null : AudioPass(s, quiet: true);
                    if (changes.Count > 0 || audioMsg != null)
                    {
                        _stable = 0;
                        Show(s.Name, audioMsg ?? UiText.Format("Refresh rate restored: {{change}}", ("change", changes[^1])), OsdState.Done);
                    }
                    else
                    {
                        _stable++;
                    }

                    if (_ticks >= BackgroundTicks || (_stable >= BackgroundStable && AudioDone))
                    {
                        Finish();
                    }

                    break;
                }

            default:
                Finish();
                break;
        }
    }

    static void ApplyLayout(Setup s, bool skipMissing)
    {
        var displays = s.DisplaysMainFirst.Select(d => ((string?)d.Id, d.Name)).ToList();
        if (displays.Count == 0)
        {
            throw new NoDisplaysChosenException();
        }

        DisplayConfig.SetActive(displays, s.Mirror, skipMissing);
        if (!s.Mirror && displays.Count > 1 && s.Main is { } main)
        {
            Thread.Sleep(600);
            DisplayConfig.SetPrimary(main.Id, main.Name);
        }
    }

    /// <summary>Apply the setup's refresh preference to each of its active displays. Returns a description of each change made.</summary>
    static List<string> RefreshPass(Setup s)
    {
        var changes = new List<string>();
        try
        {
            foreach (var m in DisplayConfig.GetMonitors().Where(m => m.Active))
            {
                var d = s.Displays.FirstOrDefault(x => x.Id.Length > 0 && x.Id.Equals(m.Id, StringComparison.OrdinalIgnoreCase))
                     ?? s.Displays.FirstOrDefault(x => x.Name.Equals(m.Name, StringComparison.OrdinalIgnoreCase));
                int pref = d?.RefreshRate ?? 0;   // displays outside the setup (shouldn't be on) get the highest rate
                if (pref < 0)
                {
                    continue;
                }

                var msg = DisplayConfig.ApplyRefreshRate(m.Id, m.Name, pref);
                if (msg != null) { Logger.Info("Refresh: {Change}", msg); changes.Add(msg); }
            }
        }
        catch (Exception ex) { Logger.Error(ex, "Refresh check failed"); }
        return changes;
    }

    /// <summary>Try to set the setup's playback and recording devices. Returns a message if it changed something.</summary>
    string? AudioPass(Setup s, bool quiet)
    {
        var parts = new List<string>();
        if (!_outDone && TrySet(AudioFlow.Output, s.AudioId, s.AudioName, ref _outDone))
        {
            parts.Add(UiText.Format("Audio: {{device}}", ("device", ShortAudio(s.AudioName))));
        }

        if (!_micDone && TrySet(AudioFlow.Input, s.MicId, s.MicName, ref _micDone))
        {
            parts.Add(UiText.Format("Mic: {{device}}", ("device", ShortAudio(s.MicName))));
        }

        return parts.Count == 0 ? null : string.Join("  ·  ", parts);
    }

    /// <summary>Returns true if it changed the default device. Sets <paramref name="done"/> once nothing is left to do.</summary>
    bool TrySet(AudioFlow flow, string id, string name, ref bool done)
    {
        if (name.Length == 0) { done = true; return false; }
        try
        {
            switch (AudioDevices.SetDefault(flow, id, name))
            {
                case AudioResult.Done:
                    Logger.Info("{Flow}: {Device}", flow, name);
                    done = true;
                    return true;
                case AudioResult.AlreadySet:
                    done = true;
                    return false;
                case AudioResult.NotFound when _ticks == 1:
                    Logger.Warn("{Flow} device '{Device}' not found", flow, name);
                    return false;
                default:
                    return false;   // not ready yet (e.g. HDMI audio appears a few seconds after the display)
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "{Flow} switch failed", flow);
            done = true;
            return false;
        }
    }

    string Summary(Setup s)
    {
        var parts = new List<string>();
        if (s.Main is { } main)
        {
            int hz = 0;
            try { hz = DisplayConfig.GetCurrentRefreshRate(main.Id, main.Name); } catch { }
            if (hz > 0)
            {
                parts.Add($"{hz} Hz");
            }
        }
        if (s.AudioName.Length > 0)
        {
            parts.Add(_outDone ? UiText.Format("Audio: {{device}}", ("device", ShortAudio(s.AudioName))) : UiText.Get("Audio not available"));
        }

        if (s.MicName.Length > 0 && !_micDone)
        {
            parts.Add(UiText.Get("Mic not available"));
        }

        parts.Add(UiText.Get(_reason));
        return string.Join("  ·  ", parts);
    }

    static string SwitchingTo(Setup s) => UiText.Format("Switching to {{setup}}…", ("setup", s.Name));

    /// <summary>"LG TV (AMD High Definition Audio Device)" → "LG TV".</summary>
    public static string ShortAudio(string name)
    {
        int i = name.IndexOf(" (", StringComparison.Ordinal);
        return i > 0 ? name[..i] : name;
    }

    void Show(string title, string subtitle, OsdState state)
    {
        if (Settings.ShowOsd)
        {
            Popup?.Invoke(title, subtitle, state);
        }
    }

    public void Dispose()
    {
        _step.Stop();
        _debounce.Stop();
        _usb?.Dispose();
    }
}

public sealed class NoDisplaysChosenException() : InvalidOperationException("This setup has no displays chosen");
