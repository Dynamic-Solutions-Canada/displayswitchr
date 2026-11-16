using HubDisplaySwitch.Classes;
using HubDisplaySwitch.Classes.Settings;
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

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainDispatcherQueue = DispatcherQueue.GetForCurrentThread();

        AppDomain.CurrentDomain.UnhandledException += (s, a) =>
        {
            NLog.LogManager.GetCurrentClassLogger().Error(a.ExceptionObject as Exception, "Unhandled exception");
            NLog.LogManager.Flush();
        };
        UnhandledException += (s, e) =>
        {
            NLog.LogManager.GetCurrentClassLogger().Error(e.Exception, "Unhandled UI exception");
            NLog.LogManager.Flush();
            e.Handled = true;
        };

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
        catch { }

        // Configure first: first-run defaults are named in the Windows language. Then apply the saved choice.
        UiText.Configure(typeof(App).Assembly, AppLanguages.CatalogPrefix, AppLanguages.Supported, null);
        SettingsManager.RestoreSettings();
        UiText.SetOverride(SettingsManager.Current.Language);

        m_window = new MainWindow();
        m_window.Activate();
    }

    internal Window? m_window;
}
