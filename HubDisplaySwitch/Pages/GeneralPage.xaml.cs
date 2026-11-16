using HubDisplaySwitch.Classes.Settings;
using WinUIAppKit.Localization;
using HubDisplaySwitch.Services;
using HubDisplaySwitch.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinUIAppKit.Startup;

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
            BuildLanguages();
            ThemeCombo.SelectedIndex = Math.Clamp((int)Settings.AppTheme, 0, 2);
            AutoSwitch.IsOn = !Settings.Paused;
            // The other controls are live from here; the startup switch has its own guard while it loads.
            _initializing = false;
            await LoadStartupAsync(() => MainWindow.Startup.GetStatusAsync());
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

    // ── Start with Windows ──────────────────────────────────────────────
    private async void StartupSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingStartupSwitch || _initializing)
        {
            return;
        }

        bool requested = StartupSwitch.IsOn;
        await LoadStartupAsync(() => MainWindow.Startup.SetEnabledAsync(requested));
    }

    /// <summary>
    /// Run a startup query or change and show its outcome (same pattern as WSL Monitor). Windows may
    /// refuse a change (the user turned the app off in Settings, or a policy decides); the switch then
    /// shows the real state, is disabled, and a note says where the decision lives. On an error the
    /// real state is re-read.
    /// </summary>
    private async Task LoadStartupAsync(Func<Task<StartupStatus>> operation)
    {
        _updatingStartupSwitch = true;
        try
        {
            ShowStartup(await operation());
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Couldn't read or change the startup state");
            try { ShowStartup(await MainWindow.Startup.GetStatusAsync()); } catch { }
        }
        finally
        {
            _updatingStartupSwitch = false;
        }
    }

    private void ShowStartup(StartupStatus status)
    {
        StartupSwitch.IsOn = status.IsOn();
        StartupSwitch.IsEnabled = status.CanChange();
        if (Settings.Startup != status.IsOn())
        {
            Settings.Startup = status.IsOn();
            SettingsManager.SaveSettings();
        }

        // Keys stay literal so scripts/check-localization.py can find them.
        switch (status)
        {
            case StartupStatus.DisabledByUser: Localize.SetText(StartupNote, "Turned off in Windows Settings (Apps > Startup). Turn it back on there."); break;
            case StartupStatus.DisabledByPolicy: Localize.SetText(StartupNote, "Turned off by your organization."); break;
            case StartupStatus.EnabledByPolicy: Localize.SetText(StartupNote, "Turned on by your organization."); break;
        }
        StartupNote.Visibility = status.CanChange() ? Visibility.Collapsed : Visibility.Visible;
    }

    private bool _buildingLanguages;

    /// <summary>"Match Windows", then each language in its own name (as in Persistent), so they read the same whatever is selected.</summary>
    private void BuildLanguages()
    {
        _buildingLanguages = true;
        try
        {
            LanguageCombo.Items.Clear();
            var match = new ComboBoxItem { Tag = "" };
            Localize.SetContent(match, "Match Windows");
            LanguageCombo.Items.Add(match);
            foreach (string identity in UiText.Languages.Identities)
            {
                LanguageCombo.Items.Add(new ComboBoxItem
                {
                    Content = UiText.Languages.NativeName(identity),
                    Tag = identity,
                    Language = identity,   // so the font and line breaking suit that language's script
                });
            }
            string current = UiText.Override ?? "";
            LanguageCombo.SelectedItem = LanguageCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == current) ?? match;
        }
        finally { _buildingLanguages = false; }
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_buildingLanguages || LanguageCombo.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            return;
        }

        Settings.Language = tag;   // OnLanguageChanged applies + saves; the UI re-labels itself
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        Settings.AppTheme = (WinUIAppKit.Theming.AppThemeChoice)ThemeCombo.SelectedIndex;   // OnAppThemeChanged applies + saves
    }
}
