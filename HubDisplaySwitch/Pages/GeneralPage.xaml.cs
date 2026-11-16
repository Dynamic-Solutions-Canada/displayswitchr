using HubDisplaySwitch.Classes.Settings;
using HubDisplaySwitch.Localization;
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
            BuildLanguages();
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

    /// <summary>Language names are in their own language (as in Persistent), so they read the same whatever is selected.</summary>
    private static readonly (string Tag, string Name)[] Languages =
    [
        ("en-US", "English (United States)"), ("en-CA", "English (Canada)"), ("es-ES", "Español (España)"),
        ("fr-FR", "Français (France)"), ("fr-CA", "Français (Canada)"), ("de-DE", "Deutsch (Deutschland)"),
        ("zh-CN", "简体中文（中国）"), ("pt-BR", "Português (Brasil)"), ("ja-JP", "日本語（日本）"),
    ];

    private bool _buildingLanguages;

    private void BuildLanguages()
    {
        _buildingLanguages = true;
        try
        {
            LanguageCombo.Items.Clear();
            var match = new ComboBoxItem { Tag = "" };
            Classes.Localize.SetContent(match, "Match Windows");
            LanguageCombo.Items.Add(match);
            foreach (var (tag, name) in Languages) LanguageCombo.Items.Add(new ComboBoxItem { Content = name, Tag = tag });
            string current = SupportedLanguages.NormalizeOverride(Settings.Language) ?? "";
            LanguageCombo.SelectedItem = LanguageCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == current) ?? match;
        }
        finally { _buildingLanguages = false; }
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_buildingLanguages || LanguageCombo.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        Settings.Language = tag;   // OnLanguageChanged applies + saves; the UI re-labels itself
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        Settings.AppTheme = ThemeCombo.SelectedIndex;   // OnAppThemeChanged applies + saves
    }
}
