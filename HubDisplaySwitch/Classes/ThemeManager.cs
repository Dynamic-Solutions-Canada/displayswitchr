using HubDisplaySwitch.Classes.Settings;
using Microsoft.UI.Xaml;

namespace HubDisplaySwitch.Classes;

/// <summary>Applies the selected app theme (WinUI 3 ElementTheme) to app windows.</summary>
internal static class ThemeManager
{
    /// <summary>Raised after the theme setting changes, so open windows and the tray icon can refresh.</summary>
    public static event Action? ThemeChanged;

    public static void ApplySavedTheme(Window window) => Apply(SettingsManager.Current.AppTheme, window);

    public static void ApplyAndSaveTheme(int theme)
    {
        SettingsManager.Current.AppTheme = theme;
        SettingsManager.SaveSettings();
        var w = SettingsWindow.GetCurrent();
        if (w != null) Apply(theme, w);
        ThemeChanged?.Invoke();
    }

    public static ElementTheme Requested(int theme) =>
        theme switch { 1 => ElementTheme.Light, 2 => ElementTheme.Dark, _ => ElementTheme.Default };

    private static void Apply(int theme, Window window)
    {
        if (window.Content is FrameworkElement fe) fe.RequestedTheme = Requested(theme);
    }

    private static readonly global::Windows.UI.ViewManagement.UISettings s_uiSettings = new();

    /// <summary>True when the effective app theme is dark (saved choice, else Windows' app mode).</summary>
    public static bool IsDarkTheme()
    {
        int t = SettingsManager.Current.AppTheme;
        if (t == 1) return false;
        if (t == 2) return true;
        var fg = s_uiSettings.GetColorValue(global::Windows.UI.ViewManagement.UIColorType.Foreground);
        return fg.R > 128;
    }
}
