using HubDisplaySwitch.Classes.Settings;
using HubDisplaySwitch.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace HubDisplaySwitch.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        string version = typeof(App).Assembly.GetName().Version?.ToString() ?? "";
        if (MainWindow.IsPackaged)
        {
            try
            {
                var v = global::Windows.ApplicationModel.Package.Current.Id.Version;
                version = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
            }
            catch { }
        }
        _version = version;
        ShowVersion();
        UiText.Changed += OnLanguageChanged;
        Unloaded += (_, _) => UiText.Changed -= OnLanguageChanged;
        LogsPath.Text = SettingsManager.PhysicalSettingsDir;
        try { AppIcon.Source = new BitmapImage(new Uri(App.IconPngPath)); } catch { }
    }

    private readonly string _version;

    private void OnLanguageChanged() => DispatcherQueue.TryEnqueue(ShowVersion);

    private void ShowVersion() => VersionText.Text = UiText.Format(
        MainWindow.IsPackaged ? "Version {{version}}" : "Version {{version}} (unpackaged)", ("version", _version));

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => MainWindow.OpenLogsFolder();
}
