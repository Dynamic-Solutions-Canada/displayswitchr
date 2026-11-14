using HubDisplaySwitch.Classes.Settings;
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

        SettingsManager.RestoreSettings();

        m_window = new MainWindow();
        m_window.Activate();
    }

    internal Window? m_window;
}
