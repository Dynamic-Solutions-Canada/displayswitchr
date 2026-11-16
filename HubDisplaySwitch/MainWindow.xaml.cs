using HubDisplaySwitch.Classes;
using HubDisplaySwitch.Classes.Settings;
using HubDisplaySwitch.Services;
using HubDisplaySwitch.Windows;
using Microsoft.UI.Xaml;
using WinUIAppKit;
using WinUIAppKit.Hosting;
using WinUIAppKit.Logging;
using WinUIAppKit.Localization;
using WinUIAppKit.Startup;
using WinUIAppKit.Theming;
using WinUIAppKit.Tray;
using static WinUIAppKit.Interop.NativeMethods;

namespace HubDisplaySwitch;

/// <summary>
/// Hidden, resident host window (1×1, off-screen, never closes). Owns the tray icon, the
/// <see cref="SwitchEngine"/>, the switcher flyout and the on-screen popup. Exit happens only from the
/// tray menu. Tray, single-instance and startup plumbing come from the WinUI App Kit.
/// </summary>
public sealed partial class MainWindow : Window
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    // App identity. Never reuse a sibling app's values: a shared tray GUID or mutex makes the apps
    // collide. These are unchanged from before the kit so Windows keeps the tray icon's show/hide choice.
    public const string HostTitle = "Hub Display Switch Host";
    private const string MutexName = "HubDisplaySwitch";
    private const string ShowSettingsMessageName = "HubDisplaySwitch_ShowSettings";
    private static readonly Guid TrayGuid = new("5b0f1c7e-3c1a-4e8e-9a35-6f1d2a7c4b11");

    /// <summary>"Start with Windows" (manifest task id / unpackaged Run value).</summary>
    public static StartupRegistration Startup { get; } = new("HubDisplaySwitchStartup", "Hub Display Switch");

    public static SwitchEngine Engine { get; private set; } = null!;
    public static MainWindow Instance { get; private set; } = null!;

    private readonly HostWindow _host;
    private readonly TrayIcon _tray;
    private readonly SwitcherWindow _switcher;
    private readonly OsdWindow _osd;
    private readonly global::Windows.UI.ViewManagement.UISettings _uiSettings = new();

    /// <summary>
    /// Single instance: if another copy is running, hand it "show settings" (unless this is a sign-in
    /// launch) and return false. Called by <see cref="App"/> before settings load, so a second launch
    /// never touches the settings file.
    /// </summary>
    public static bool ClaimSingleInstance()
    {
        if (SingleInstance.TryClaim(MutexName))
        {
            return true;
        }

        if (!Startup.IsSilentLaunch())
        {
            SingleInstance.SignalRunningInstance(HostTitle, ShowSettingsMessageName);
        }

        return false;
    }

    public MainWindow()
    {
        InitializeComponent();
        Instance = this;

        // The kit's hidden host (TRAY-7): 1x1 off-screen tool window that answers a second launch.
        _host = new HostWindow(this, HostTitle, ShowSettingsMessageName);
        _host.ShowRequested += SettingsWindow.ShowInstance;
        RegisterApplicationRestart("--silent", 0);

        Engine = new SwitchEngine(DispatcherQueue);
        _osd = new OsdWindow();
        _switcher = new SwitcherWindow();
        Engine.Popup += (title, subtitle, state) => _osd.Present(title, subtitle, state);
        Engine.StateChanged += () => { UpdateTrayIcon(); _switcher.Refresh(); };

        _tray = new TrayIcon(_host.Hwnd, TrayGuid, "HubDisplaySwitch_TrayCallback");
        _tray.Clicked += a => _switcher.Toggle(a.X, a.Y);
        _tray.DoubleClicked += a => _switcher.ShowAt(a.X, a.Y);   // the first click already opened it; keep it open
        _tray.ContextMenuRequested += ShowContextMenu;
        _tray.RenderNeeded += UpdateTrayIcon;
        // Follow the new layout: re-centre a visible popup and re-park the hidden windows.
        _tray.DisplayChanged += () => { _osd.Reposition(); _switcher.OnDisplayChanged(); };

        AppTheme.Changed += UpdateTrayIcon;
        UiText.Changed += () => { UpdateTrayIcon(); _switcher.Refresh(); };
        // The shared "Our other apps" page has its own strings; point it at our language choice.
        TechnicallyReal.Promo.PromoLocalization.LocaleProvider = () => UiText.Locale;
        UiText.Changed += TechnicallyReal.Promo.PromoLocalization.Refresh;
        _uiSettings.ColorValuesChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateTrayIcon);
        UpdateTrayIcon();

        // Exit steps run in this order when the user picks Exit (HostWindow.Exit).
        _host.AddExitStep("save settings", SettingsManager.SaveSettings);
        _host.AddExitStep("stop the switch engine", Engine.Dispose);
        _host.AddExitStep("remove the tray icon", _tray.Dispose);

        if (SettingsManager.IsFirstRun && SettingsManager.Current.Startup)
        {
            _ = Startup.RegisterOnFirstRunAsync();
        }

        Startup.RemoveUnpackagedEntries("HubDisplaySwitch");   // the original watcher's Run value

        Logger.Info("Started v{Version}, {Count} setup(s), packaged={Packaged}",
            AppEnvironment.Version, SettingsManager.Current.Setups.Count, AppEnvironment.IsPackaged);
        Engine.Start();

        // Also after the settings file had to be reset, so the user sees their setups were recreated.
        if (!Startup.IsSilentLaunch() || SettingsManager.IsFirstRun || SettingsManager.WasReset)
        {
            SettingsWindow.ShowInstance();
        }
    }

    // ── Tray icon ───────────────────────────────────────────────────────
    public void UpdateTrayIcon()
    {
        var png = TrayIconRenderer.Render(SettingsManager.Current.Paused, TrayIcon.TaskbarIsDark(), TrayIcon.IconPixelSize);
        _tray.Update(png, TrayTooltip());
    }

    private static string TrayTooltip()
    {
        string setup = Engine.Active?.Name ?? UiText.Get("no setup");
        return "Hub Display Switch – " + (SettingsManager.Current.Paused ? UiText.Format("{{setup}} (paused)", ("setup", setup)) : setup);
    }

    private void ShowContextMenu(int x, int y)
    {
        var s = SettingsManager.Current;
        var menu = new TrayMenu();
        foreach (var setup in s.Setups.ToList())
        {
            menu.Add(setup.Name, () => Engine.Apply(setup, SwitchReason.Manual), isChecked: Engine.Active?.Id == setup.Id);
        }

        menu.AddSeparator()
            .Add(UiText.Get(s.Paused ? "Resume auto-switching" : "Pause auto-switching"), () => Engine.SetPaused(!SettingsManager.Current.Paused))
            .Add(UiText.Get("Settings"), SettingsWindow.ShowInstance)
            .Add(UiText.Get("Open logs folder"), OpenLogsFolder)
            .AddSeparator()
            .Add(UiText.Get("Exit"), ExitApp)
            .Show(_host.Hwnd, x, y);
    }

    public static void OpenLogsFolder() => AppLogging.OpenLogsFolder();

    /// <summary>Exits through the host: the exit steps run in order, the log is flushed, then the process ends.</summary>
    public void ExitApp() => _host.Exit();
}
