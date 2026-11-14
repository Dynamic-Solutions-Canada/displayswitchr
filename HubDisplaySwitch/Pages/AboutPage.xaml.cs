using HubDisplaySwitch.Classes.Settings;
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
        VersionText.Text = $"Version {version}{(MainWindow.IsPackaged ? "" : " (unpackaged)")}";
        LogsPath.Text = SettingsManager.PhysicalSettingsDir;
        try { AppIcon.Source = new BitmapImage(new Uri(App.IconPngPath)); } catch { }
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => MainWindow.OpenLogsFolder();
}
