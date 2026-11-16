using CommunityToolkit.Mvvm.ComponentModel;
using HubDisplaySwitch.Models;
using System.Text.Json.Serialization;
using WinUIAppKit.Theming;
using WinUIAppKit.Windowing;

namespace HubDisplaySwitch.ViewModels;

/// <summary>
/// User settings. All [ObservableProperty] fields generate INotifyPropertyChanged via
/// CommunityToolkit.Mvvm source generators and serialize to settings.json.
/// </summary>
public partial class UserSettings : ObservableObject
{
    /// <summary>Settings file format. 2 = setups.</summary>
    public int Version { get; set; } = 2;

    /// <summary>App theme. Stored as 0 = System default, 1 = Light, 2 = Dark (same numbers as before the kit).</summary>
    [ObservableProperty]
    public partial AppThemeChoice AppTheme { get; set; }

    /// <summary>Interface language, e.g. "fr-CA". Empty = match Windows.</summary>
    [ObservableProperty]
    public partial string Language { get; set; } = "";

    /// <summary>Start at sign-in (StartupTask when packaged, Run key when unpackaged). Registered once on first run.</summary>
    [ObservableProperty]
    public partial bool Startup { get; set; } = true;

    /// <summary>Switch to the right setup when the app starts (e.g. after sign-in).</summary>
    [ObservableProperty]
    public partial bool ApplyOnStart { get; set; } = true;

    /// <summary>Show the on-screen popup while switching.</summary>
    [ObservableProperty]
    public partial bool ShowOsd { get; set; } = true;

    /// <summary>Auto-switching paused (manual switching still works).</summary>
    [ObservableProperty]
    public partial bool Paused { get; set; }

    /// <summary>The setups, in priority order (top wins at startup when several hubs are connected).</summary>
    public List<Setup> Setups { get; set; } = new();

    /// <summary>Legacy (pre per-setup) display name → refresh preference; migrated into <see cref="DisplayRef.RefreshRate"/> on load.</summary>
    public Dictionary<string, int> RefreshRates { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Last known app version string.</summary>
    [ObservableProperty]
    public partial string LastKnownVersion { get; set; } = "";

    /// <summary>Settings window position and size (physical pixels), restored when it reopens.</summary>
    public WindowPlacement? SettingsWindow { get; set; }

    // Older builds stored the settings window geometry as four numbers; read once and moved into SettingsWindow.
    public int? SettingsWindowX { get; set; }
    public int? SettingsWindowY { get; set; }
    public int? SettingsWindowWidth { get; set; }
    public int? SettingsWindowHeight { get; set; }

    [JsonIgnore] public Setup? DefaultSetup => Setups.FirstOrDefault(s => s.IsDefault);


    [JsonIgnore] private bool _initializing = true;

    partial void OnAppThemeChanged(AppThemeChoice value)
    {
        if (_initializing) return;
        WinUIAppKit.Theming.AppTheme.Set(value);
        Classes.Settings.SettingsManager.SaveSettings();
    }

    partial void OnLanguageChanged(string value)
    {
        if (_initializing) return;
        Classes.Settings.SettingsManager.SaveSettings();
        WinUIAppKit.Localization.UiText.SetOverride(value);   // open windows relabel on UiText.Changed
    }

    /// <summary>Repairs any nulls from older/partial settings files and migrates old fields. Returns true if it changed what's on disk.</summary>
    public bool CompleteInitialization()
    {
        Language ??= "";
        bool legacyWindow = SettingsWindowX != null || SettingsWindowY != null || SettingsWindowWidth != null || SettingsWindowHeight != null;
        if (SettingsWindow == null && SettingsWindowWidth is > 0 && SettingsWindowHeight is > 0)
        {
            SettingsWindow = new WindowPlacement
            {
                X = SettingsWindowX ?? 0,
                Y = SettingsWindowY ?? 0,
                Width = SettingsWindowWidth.Value,
                Height = SettingsWindowHeight.Value,
            };
        }
        SettingsWindowX = SettingsWindowY = SettingsWindowWidth = SettingsWindowHeight = null;
        _initializing = false;
        bool changed = legacyWindow || Setups == null || RefreshRates is { Count: > 0 };
        Setups ??= new();
        RefreshRates = new Dictionary<string, int>(RefreshRates ?? new(), StringComparer.OrdinalIgnoreCase);
        foreach (var s in Setups)
        {
            s.Hubs ??= new();
            s.Displays ??= new();
            // Older builds kept refresh preferences per display globally; carry them into each setup.
            foreach (var d in s.Displays)
            {
                if (d.RefreshRate == 0 && RefreshRates.TryGetValue(d.Name, out var r))
                {
                    d.RefreshRate = r;
                }
            }
        }
        RefreshRates.Clear();
        return changed;
    }
}
