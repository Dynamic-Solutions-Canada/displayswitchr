using HubDisplaySwitch.Classes.Settings;
using HubDisplaySwitch.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIAppKit.Localization;
using WinUIAppKit.Settings;

namespace HubDisplaySwitch.Pages;

/// <summary>
/// App-wide settings (the NavigationView's built-in Settings item). Start with Windows, language and
/// theme are winui-app-kit's shared rows (UI-4); this page saves what they report. Every edit saves at
/// once, with no Save button.
/// </summary>
public sealed partial class GeneralPage : Page
{
    public UserSettings Settings => SettingsManager.Current;
    private bool _initializing = true;

    public GeneralPage()
    {
        InitializeComponent();

        // The kit's rows apply a change themselves and report it; the settings object saves it.
        StartupRow.Registration = MainWindow.Startup;
        StartupRow.StatusChanged += OnStartupStatus;
        LanguageRow.ChoiceChanged += identity => Settings.Language = identity ?? "";
        ThemeRow.ChoiceChanged += choice => Settings.AppTheme = choice;

        // After an unreadable settings file, say once that the defaults are in use (CONF-2).
        if (SettingsManager.TakeResetNotice() is { } notice)
        {
            NoticeHost.Content = SettingsResetNotice.Create(notice.BackupPath);
        }

        Loaded += (_, _) =>
        {
            AutoSwitch.IsOn = !Settings.Paused;
            _initializing = false;
        };
    }

    private void Persist(object sender, RoutedEventArgs e)
    {
        if (!_initializing)
        {
            SettingsManager.SaveSettings();
        }
    }

    private void AutoSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        MainWindow.Engine.SetPaused(!AutoSwitch.IsOn);
    }

    /// <summary>
    /// Keep the saved "Start with Windows" in step with Windows' real state, which the row reports when it
    /// loads and after every change (the user or a policy may have decided in Windows Settings).
    /// </summary>
    private void OnStartupStatus(bool isOn)
    {
        if (Settings.Startup != isOn)
        {
            Settings.Startup = isOn;
            SettingsManager.SaveSettings();
        }
    }

    /// <summary>
    /// "Reset settings" (CONF-4): asks first (the kit's dialog defaults to Cancel), then resets and
    /// lets the engine and the settings window pick up the new setups. The settings window rebuilds its
    /// menu and opens the new default setup, which also leaves this page and its bindings to the old
    /// settings object.
    /// </summary>
    private async void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsWindow.GetCurrent() is not { } window)
        {
            return;
        }

        bool confirmed = await WinUIAppKit.Windowing.ConfirmDialog.ConfirmAsync(
            window,
            UiText.Get("Reset all settings?"),
            UiText.Get("Your setups are replaced by one default setup for the displays and audio in use now, and the theme and language go back to the defaults. Start with Windows stays as it is."),
            UiText.Get("Reset"));
        if (!confirmed)
        {
            return;
        }

        SettingsManager.Reset();
        MainWindow.Engine.SettingsChanged();
        window.RebuildSetupItems(null);
    }
}
