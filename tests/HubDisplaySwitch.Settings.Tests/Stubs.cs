// Stand-ins for what the settings code touches outside the settings file: the display and audio
// lists the two-state conversion reads, the kit's theme, window and language types, and the app's
// save call. Same-named types in the same namespaces let the production files compile here unchanged,
// which is why the namespaces below don't match this folder (IDE0130 is off for this file).
#pragma warning disable IDE0130

namespace HubDisplaySwitch.Services
{
    public sealed record MonitorInfo(string Id, string Name, bool Active);

    public sealed record AudioDevice(string Id, string Name, bool Active, bool IsDefault);

    /// <summary>The displays Windows can see; each scenario sets them.</summary>
    public static class DisplayConfig
    {
        public static List<MonitorInfo> Monitors { get; } = new();

        public static List<MonitorInfo> GetMonitors() => new(Monitors);
    }

    /// <summary>The playback devices; each scenario sets them.</summary>
    public static class AudioDevices
    {
        public static List<AudioDevice> Devices { get; } = new();

        public static List<AudioDevice> List() => new(Devices);
    }
}

namespace HubDisplaySwitch.Classes.Settings
{
    /// <summary>The app's save call, which UserSettings makes when the theme or language changes.</summary>
    public static class SettingsManager
    {
        public static void SaveSettings()
        {
        }
    }
}

namespace WinUIAppKit.Theming
{
    /// <summary>The kit's theme choice, with the kit's numbers (they are what the file stores).</summary>
    public enum AppThemeChoice
    {
        System = 0,
        Light = 1,
        Dark = 2,
    }

    public static class AppTheme
    {
        public static void Set(AppThemeChoice choice)
        {
        }
    }
}

namespace WinUIAppKit.Windowing
{
    /// <summary>The kit's saved window position, with the kit's property names.</summary>
    public sealed class WindowPlacement
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
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

        public static void SetOverride(string? language)
        {
        }
    }
}

#pragma warning restore IDE0130
