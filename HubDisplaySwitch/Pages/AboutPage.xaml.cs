using HubDisplaySwitch.Classes.Settings;
using WinUIAppKit.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUIAppKit;

namespace HubDisplaySwitch.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        ShowVersion();
        UiText.Changed += OnLanguageChanged;
        Unloaded += (_, _) => UiText.Changed -= OnLanguageChanged;
        LogsPath.Text = SettingsManager.PhysicalSettingsDir;
        try { AppIcon.Source = new BitmapImage(new Uri(App.IconPngPath)); } catch { }
    }

    private void OnLanguageChanged() => ShowVersion();

    private void ShowVersion() => VersionText.Text = UiText.Format(
        AppEnvironment.IsPackaged ? "Version {{version}}" : "Version {{version}} (unpackaged)", ("version", AppEnvironment.Version));

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => MainWindow.OpenLogsFolder();
}
