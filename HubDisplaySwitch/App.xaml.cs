using HubDisplaySwitch.Classes;
using HubDisplaySwitch.Classes.Settings;
using WinUIAppKit.Hosting;
using WinUIAppKit.Localization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System.IO;

namespace HubDisplaySwitch;

/// <summary>
/// Application entry point (WinUI 3). The app is resident: <see cref="MainWindow"/> is a hidden host
/// that owns the tray icon, the switch engine, the switcher flyout and the on-screen popup.
/// </summary>
public partial class App : Application
{
    public static DispatcherQueue MainDispatcherQueue { get; private set; } = null!;

    /// <summary>Path to the app icon (used for window/title-bar images).</summary>
    public static string IconPath => Path.Combine(AppContext.BaseDirectory, "Resources", "HubDisplaySwitch.ico");

    /// <summary>256 px PNG of the app icon for in-app images (scales down sharply; .ico frames get stretched).</summary>
    public static string IconPngPath => Path.Combine(AppContext.BaseDirectory, "Resources", "HubDisplaySwitch.png");

    public App()
    {
        // The shared NLog config writes to %AppData%\<data folder>; name it before anything logs.
        WinUIAppKit.Logging.AppLogging.UseDataFolder(SettingsManager.DataFolderName);
        InitializeComponent();
    }

    /// <summary>
    /// Starts the app through the kit's <see cref="AppBootstrap"/>: crash logging first (LOG-7), then
    /// <see cref="ClaimInstanceAndRestoreSettings"/>, then the hidden host window.
    /// </summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainDispatcherQueue = DispatcherQueue.GetForCurrentThread();
        m_window = AppBootstrap.Launch(this, ClaimInstanceAndRestoreSettings, () => new MainWindow());
    }

    /// <summary>
    /// The settings step of the launch. The single instance is claimed first, so a second launch hands
    /// "show settings" to the running copy and exits without touching the settings file (the store
    /// writes the file it loads).
    /// </summary>
    private static void ClaimInstanceAndRestoreSettings()
    {
        if (!MainWindow.ClaimSingleInstance())
        {
            Environment.Exit(0);
            return;
        }

        // Builds 1.0.0.10-11 set the package's PrimaryLanguageOverride; the kit resolves "Match Windows"
        // from the package language list, which that override would pin. Hub no longer sets it.
        try
        {
            if (WinUIAppKit.AppEnvironment.IsPackaged && global::Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride.Length > 0)
            {
                global::Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "";
            }
        }
        catch
        {
        }

        // Configure first: first-run defaults are named in the Windows language. Then apply the saved choice.
        UiText.Configure(typeof(App).Assembly, AppLanguages.CatalogPrefix, AppLanguages.Supported, null);
        SettingsManager.RestoreSettings();
        UiText.SetOverride(SettingsManager.Current.Language);
    }

    internal Window? m_window;
}
