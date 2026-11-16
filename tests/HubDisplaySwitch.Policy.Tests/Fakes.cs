// Stand-ins for everything SwitchEngine talks to outside its own policy: the WinUI dispatcher and its
// timers, WMI device enumeration, the display (CCD) and audio (Core Audio) APIs, the settings store,
// interface text and NLog. Each scenario calls Fakes.Reset() first, so no state leaks between them.
// Same-named types in the same namespaces let the production files compile here unchanged.
// That is why the namespaces below don't match this folder (IDE0130 is off for this file).
#pragma warning disable IDE0130

namespace Microsoft.UI.Dispatching
{
    /// <summary>The callback type DispatcherQueue.TryEnqueue takes.</summary>
    public delegate void DispatcherQueueHandler();

    /// <summary>
    /// A single-threaded dispatcher on a virtual clock. Nothing runs until a scenario calls
    /// <see cref="Advance"/>, which runs queued work and fires due timers in time order.
    /// </summary>
    public sealed class DispatcherQueue
    {
        readonly List<DispatcherQueueTimer> _timers = new();
        readonly Queue<DispatcherQueueHandler> _queued = new();

        /// <summary>Virtual time in milliseconds since the scenario started.</summary>
        public long Now { get; private set; }

        public DispatcherQueueTimer CreateTimer()
        {
            var timer = new DispatcherQueueTimer(this);
            _timers.Add(timer);
            return timer;
        }

        public bool TryEnqueue(DispatcherQueueHandler handler)
        {
            _queued.Enqueue(handler);
            return true;
        }

        /// <summary>
        /// Moves the clock forward by <paramref name="ms"/>, running queued work first and after every
        /// timer tick. Throws if timers keep firing without the clock moving (a runaway loop).
        /// </summary>
        public void Advance(long ms)
        {
            long end = Now + ms;
            RunQueued();

            for (int guard = 0; ; guard++)
            {
                if (guard > 100_000)
                {
                    throw new InvalidOperationException("Timers kept firing; the engine is looping.");
                }

                var next = _timers
                    .Where(t => t.IsRunning && t.Due <= end)
                    .OrderBy(t => t.Due)
                    .FirstOrDefault();
                if (next == null)
                {
                    break;
                }

                Now = next.Due;
                next.Fire();
                RunQueued();
            }

            Now = end;
        }

        void RunQueued()
        {
            while (_queued.Count > 0)
            {
                _queued.Dequeue()();
            }
        }
    }

    /// <summary>A timer on the owning <see cref="DispatcherQueue"/>'s virtual clock.</summary>
    public sealed class DispatcherQueueTimer
    {
        readonly DispatcherQueue _queue;

        internal DispatcherQueueTimer(DispatcherQueue queue) => _queue = queue;

        public TimeSpan Interval { get; set; }

        /// <summary>WinUI's default is a repeating timer.</summary>
        public bool IsRepeating { get; set; } = true;

        public bool IsRunning { get; private set; }

        /// <summary>When the next tick is due, in virtual milliseconds.</summary>
        internal long Due { get; private set; }

        public event Action<DispatcherQueueTimer, object?>? Tick;

        public void Start()
        {
            IsRunning = true;
            Due = _queue.Now + Math.Max(1, (long)Interval.TotalMilliseconds);
        }

        public void Stop() => IsRunning = false;

        /// <summary>Schedules the next tick (or stops a one-shot timer), then raises Tick. A handler may restart it.</summary>
        internal void Fire()
        {
            if (IsRepeating)
            {
                Due += Math.Max(1, (long)Interval.TotalMilliseconds);
            }
            else
            {
                IsRunning = false;
            }

            Tick?.Invoke(this, null);
        }
    }
}

namespace HubDisplaySwitch.Services
{
    /// <summary>
    /// The WMI half of UsbDevices. The matching half (ModelPrefix, IsPresent) is the production file
    /// UsbDevice.cs, linked unchanged.
    /// </summary>
    public sealed partial class UsbDevices : IDisposable
    {
        static UsbDevices? _instance;

        /// <summary>The USB devices "plugged in" right now.</summary>
        public static List<UsbDevice> Present { get; } = new();

        /// <summary>When set, List() throws, as a failed WMI query would.</summary>
        public static bool FailList { get; set; }

        public static List<UsbDevice> List()
        {
            if (FailList)
            {
                throw new InvalidOperationException("WMI query failed (stand-in)");
            }

            return new List<UsbDevice>(Present);
        }

        public event Action? Changed;

        public UsbDevices() => _instance = this;

        /// <summary>Plugs a device in and raises the change notification, as WMI would.</summary>
        public static void Plug(string deviceId, string name = "USB hub")
        {
            Present.Add(new UsbDevice(deviceId, name));
            _instance?.Changed?.Invoke();
        }

        /// <summary>Unplugs a device and raises the change notification.</summary>
        public static void Unplug(string deviceId)
        {
            Present.RemoveAll(d => d.DeviceId == deviceId);
            _instance?.Changed?.Invoke();
        }

        internal static void Reset()
        {
            _instance = null;
            Present.Clear();
            FailList = false;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>A monitor Windows can see. Id is the connector's device path (stable across reboots).</summary>
    public sealed record MonitorInfo(string Id, string Name, bool Active);

    /// <summary>Thrown when a setup's display is not connected (a TV that is still asleep).</summary>
    public sealed class MonitorNotFoundException(string name)
        : Exception($"'{name}' isn't connected or is switched off")
    {
        public string DisplayName { get; } = name;
    }

    /// <summary>
    /// A small model of the display layout: which screens are connected and on, which is primary, and
    /// each one's refresh rate. Matching is by id, then by name, as in the real CCD code.
    /// </summary>
    public static class DisplayConfig
    {
        /// <summary>One physical display.</summary>
        public sealed class Screen(string id, string name, params int[] rates)
        {
            public string Id { get; } = id;
            public string Name { get; } = name;

            /// <summary>False while a TV is asleep: Windows can't see it.</summary>
            public bool Connected { get; set; } = true;
            public bool Active { get; set; }

            /// <summary>Rates available at the current resolution, highest first.</summary>
            public int[] Rates { get; } = rates.OrderByDescending(r => r).ToArray();
            public int Hz { get; set; } = rates.Min();
        }

        public static List<Screen> Screens { get; } = new();
        public static string? PrimaryId { get; set; }

        /// <summary>Every layout change, as "layout:Name+Name" or "primary:Name".</summary>
        public static List<string> Calls { get; } = new();

        static Screen? Find(string? id, string name) =>
            Screens.FirstOrDefault(s => s.Connected && !string.IsNullOrEmpty(id) && s.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            ?? Screens.FirstOrDefault(s => s.Connected && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        public static List<MonitorInfo> GetMonitors() =>
            Screens.Where(s => s.Connected).Select(s => new MonitorInfo(s.Id, s.Name, s.Active)).ToList();

        /// <summary>Turns on exactly the listed displays (the first is primary) and turns the rest off.</summary>
        public static void SetActive(IReadOnlyList<(string? id, string name)> monitors, bool mirror, bool skipMissing = false)
        {
            var found = new List<Screen>();
            foreach (var (id, name) in monitors)
            {
                var screen = Find(id, name);
                if (screen != null)
                {
                    found.Add(screen);
                }
                else if (!skipMissing)
                {
                    throw new MonitorNotFoundException(name);
                }
            }

            if (found.Count == 0)
            {
                throw new MonitorNotFoundException(monitors[0].name);
            }

            foreach (var screen in Screens)
            {
                screen.Active = found.Contains(screen);
            }

            PrimaryId = found[0].Id;
            Calls.Add("layout:" + string.Join("+", found.Select(s => s.Name)));
        }

        public static void SetPrimary(string? id, string name)
        {
            var screen = Find(id, name) ?? throw new MonitorNotFoundException(name);
            PrimaryId = screen.Id;
            Calls.Add("primary:" + screen.Name);
        }

        public static bool IsPrimary(string? id, string name) =>
            Find(id, name) is { Active: true } screen && screen.Id == PrimaryId;

        public static int GetCurrentRefreshRate(string? id, string name) =>
            Find(id, name) is { Active: true } screen ? screen.Hz : 0;

        /// <summary>preferred: 0 = highest available, &gt;0 = that rate or the closest lower one. Returns a description if it changed.</summary>
        public static string? ApplyRefreshRate(string? id, string name, int preferred)
        {
            if (Find(id, name) is not { Active: true } screen)
            {
                return null;
            }

            int target = Target(screen, preferred);
            if (target == screen.Hz)
            {
                return null;
            }

            string change = $"{screen.Name}: {screen.Hz} -> {target} Hz";
            screen.Hz = target;
            return change;
        }

        public static bool RefreshRateNeedsChange(string? id, string name, int preferred) =>
            preferred >= 0 && Find(id, name) is { Active: true } screen && Target(screen, preferred) != screen.Hz;

        static int Target(Screen screen, int preferred) =>
            preferred == 0
                ? screen.Rates[0]
                : screen.Rates.Where(r => r <= preferred).DefaultIfEmpty(screen.Rates[^1]).First();

        internal static void Reset()
        {
            Screens.Clear();
            PrimaryId = null;
            Calls.Clear();
        }
    }

    public sealed record AudioDevice(string Id, string Name, bool Active, bool IsDefault);

    public enum AudioResult { Done, AlreadySet, NotReady, NotFound }

    public enum AudioFlow { Output = 0, Input = 1 }

    /// <summary>
    /// A small model of the audio endpoints: which exist, which are active (HDMI audio becomes active
    /// seconds after its display) and which is the default, per direction.
    /// </summary>
    public static class AudioDevices
    {
        /// <summary>One playback or recording endpoint.</summary>
        public sealed class Endpoint(AudioFlow flow, string id, string name)
        {
            public AudioFlow Flow { get; } = flow;
            public string Id { get; } = id;
            public string Name { get; } = name;
            public bool Active { get; set; } = true;
        }

        public static List<Endpoint> Endpoints { get; } = new();
        public static Dictionary<AudioFlow, string> Defaults { get; } = new();

        public static AudioResult SetDefault(AudioFlow flow, string id, string name)
        {
            var candidates = Endpoints
                .Where(e => e.Flow == flow)
                .Where(e => (id.Length > 0 && e.Id == id) || e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (candidates.Count == 0)
            {
                return AudioResult.NotFound;
            }

            var pick = candidates.FirstOrDefault(e => e.Active);
            if (pick == null)
            {
                return AudioResult.NotReady;
            }

            if (Defaults.GetValueOrDefault(flow) == pick.Id)
            {
                return AudioResult.AlreadySet;
            }

            Defaults[flow] = pick.Id;
            return AudioResult.Done;
        }

        public static AudioDevice? CurrentDefault(AudioFlow flow = AudioFlow.Output) =>
            Endpoints
                .Where(e => e.Flow == flow && Defaults.GetValueOrDefault(flow) == e.Id)
                .Select(e => new AudioDevice(e.Id, e.Name, e.Active, true))
                .FirstOrDefault();

        internal static void Reset()
        {
            Endpoints.Clear();
            Defaults.Clear();
        }
    }
}

namespace HubDisplaySwitch.ViewModels
{
    using HubDisplaySwitch.Models;

    /// <summary>
    /// The settings SwitchEngine reads. The real class needs the MVVM source generator and the kit's
    /// theme and window types; only these members matter to the policy, and DefaultSetup is the
    /// production rule (the first setup marked default).
    /// </summary>
    public class UserSettings
    {
        public bool ApplyOnStart { get; set; } = true;
        public bool ShowOsd { get; set; } = true;
        public bool Paused { get; set; }
        public List<Setup> Setups { get; set; } = new();
        public Setup? DefaultSetup => Setups.FirstOrDefault(s => s.IsDefault);
    }
}

namespace HubDisplaySwitch.Classes.Settings
{
    using HubDisplaySwitch.ViewModels;

    /// <summary>The settings store: in memory, counting saves.</summary>
    public static class SettingsManager
    {
        public static UserSettings Current { get; set; } = new();
        public static int Saves { get; private set; }

        public static void SaveSettings() => Saves++;

        internal static void Reset()
        {
            Current = new UserSettings();
            Saves = 0;
        }
    }
}

namespace WinUIAppKit.Localization
{
    /// <summary>Interface text: English only. Keys are the English text, as in the app's catalogs.</summary>
    public static class UiText
    {
        public static string Get(string key) => key;

        public static string Format(string key, params (string Name, object? Value)[] values)
        {
            string text = key;
            foreach (var (name, value) in values)
            {
                text = text.Replace("{{" + name + "}}", value?.ToString() ?? "");
            }

            return text;
        }
    }
}

namespace NLog
{
    /// <summary>A logger that keeps lines in memory, so a failing scenario can print what the engine logged.</summary>
    public sealed class Logger
    {
        public static List<string> Lines { get; } = new();

        public void Info(string message, params object?[] args) => Lines.Add("INFO " + Render(message, args));
        public void Warn(string message, params object?[] args) => Lines.Add("WARN " + Render(message, args));
        public void Warn(Exception ex, string message, params object?[] args) => Lines.Add("WARN " + Render(message, args) + ": " + ex.Message);
        public void Error(Exception ex, string message, params object?[] args) => Lines.Add("ERROR " + Render(message, args) + ": " + ex.Message);

        /// <summary>Appends the arguments after the template; enough to read a log, not NLog's rendering.</summary>
        static string Render(string message, object?[] args) =>
            args.Length == 0 ? message : message + " [" + string.Join(", ", args) + "]";
    }

    public static class LogManager
    {
        public static Logger GetCurrentClassLogger() => new();
    }
}

namespace HubDisplaySwitch.Policy.Tests
{
    using HubDisplaySwitch.Classes.Settings;
    using HubDisplaySwitch.Services;

    /// <summary>Resets every stand-in, so each scenario starts from nothing.</summary>
    public static class Fakes
    {
        public static void Reset()
        {
            UsbDevices.Reset();
            DisplayConfig.Reset();
            AudioDevices.Reset();
            SettingsManager.Reset();
            NLog.Logger.Lines.Clear();
        }
    }
}

#pragma warning restore IDE0130
