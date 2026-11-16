using CommunityToolkit.Mvvm.ComponentModel;
using HubDisplaySwitch.Models;
using System.Text.Json.Serialization;

namespace HubDisplaySwitch.ViewModels;

/// <summary>
/// User settings. All [ObservableProperty] fields generate INotifyPropertyChanged via
/// CommunityToolkit.Mvvm source generators and serialize to settings.json.
/// </summary>
public partial class UserSettings : ObservableObject
{
    /// <summary>Settings file format. 2 = setups.</summary>
    public int Version { get; set; } = 2;

    /// <summary>App theme. 0 = System default, 1 = Light, 2 = Dark.</summary>
    [ObservableProperty]
    public partial int AppTheme { get; set; }

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

    // ── Settings window geometry ─────────────────────────────────────
    [ObservableProperty] public partial int SettingsWindowX { get; set; }
    [ObservableProperty] public partial int SettingsWindowY { get; set; }
    [ObservableProperty] public partial int SettingsWindowWidth { get; set; }
    [ObservableProperty] public partial int SettingsWindowHeight { get; set; }

    [JsonIgnore] public Setup? DefaultSetup => Setups.FirstOrDefault(s => s.IsDefault);


    [JsonIgnore] private bool _initializing = true;

    partial void OnAppThemeChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;
        Classes.ThemeManager.ApplyAndSaveTheme(newValue);
    }

    partial void OnLanguageChanged(string value)
    {
        if (_initializing) return;
        Localization.UiText.SetOverride(value);
        Classes.Settings.SettingsManager.SaveSettings();
    }

    /// <summary>Repairs any nulls from older/partial settings files.</summary>
    public void CompleteInitialization()
    {
        Language ??= "";
        Localization.UiText.SetOverride(Language, notify: false);
        _initializing = false;
        Setups ??= new();
        RefreshRates = new Dictionary<string, int>(RefreshRates ?? new(), StringComparer.OrdinalIgnoreCase);
        foreach (var s in Setups)
        {
            s.Hubs ??= new();
            s.Displays ??= new();
            // Older builds kept refresh preferences per display globally; carry them into each setup.
            foreach (var d in s.Displays)
                if (d.RefreshRate == 0 && RefreshRates.TryGetValue(d.Name, out var r)) d.RefreshRate = r;
        }
        RefreshRates.Clear();
    }
}
