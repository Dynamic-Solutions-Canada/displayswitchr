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

        // The kit's "Reset settings" row asks and reports; the reset itself, and what it says about the
        // setups being replaced, are the app's (CONF-4).
        ResetRow.ResetAsync = ResetSettingsAsync;
        ResetRow.KeptNote = () => UiText.Get("Your setups are replaced by one default setup for the displays and audio in use now.");
        ResetRow.ResetCompleted += ShowResetSetups;

        Loaded += (_, _) =>
        {
            ResetRow.Window = SettingsWindow.GetCurrent();
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
    /// The reset the kit's row runs after the user confirms: new defaults (one default setup for the
    /// displays and audio in use now), then the engine picks up the new setups. Start with Windows is
    /// left as it is.
    /// </summary>
    private static Task ResetSettingsAsync()
    {
        SettingsManager.Reset();
        MainWindow.Engine.SettingsChanged();
        return Task.CompletedTask;
    }

    /// <summary>
    /// After a reset, the settings window rebuilds its menu and opens the new default setup, which also
    /// leaves this page and its bindings to the old settings object.
    /// </summary>
    private static void ShowResetSetups()
    {
        SettingsWindow.GetCurrent()?.RebuildSetupItems(null);
    }
}
