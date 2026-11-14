using HubDisplaySwitch.Classes.Settings;
using HubDisplaySwitch.Services;
using HubDisplaySwitch.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HubDisplaySwitch.Pages;

/// <summary>App-wide settings (the NavigationView's built-in Settings item).</summary>
public sealed partial class GeneralPage : Page
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public UserSettings Settings => SettingsManager.Current;
    private bool _initializing = true, _updatingStartupSwitch;

    public GeneralPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            ThemeCombo.SelectedIndex = Math.Clamp(Settings.AppTheme, 0, 2);
            AutoSwitch.IsOn = !Settings.Paused;
            _updatingStartupSwitch = true;
            try { StartupSwitch.IsOn = await MainWindow.IsStartupEnabledAsync(); }
            catch (Exception ex) { Logger.Warn(ex, "Couldn't read startup state"); }
            finally { _updatingStartupSwitch = false; }
            _initializing = false;
        };
    }

    private void Persist(object sender, RoutedEventArgs e)
    {
        if (!_initializing) SettingsManager.SaveSettings();
    }

    private void AutoSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        MainWindow.Engine.SetPaused(!AutoSwitch.IsOn);
    }

    private async void StartupSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingStartupSwitch || _initializing) return;
        _updatingStartupSwitch = true;
        try
        {
            bool result = await MainWindow.SetStartupAsync(StartupSwitch.IsOn);
            StartupSwitch.IsOn = result;   // the user or policy may refuse
            Settings.Startup = result;
            SettingsManager.SaveSettings();
        }
        catch (Exception ex) { Logger.Warn(ex, "Couldn't change startup state"); }
        finally { _updatingStartupSwitch = false; }
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        Settings.AppTheme = ThemeCombo.SelectedIndex;   // OnAppThemeChanged applies + saves
    }
}
