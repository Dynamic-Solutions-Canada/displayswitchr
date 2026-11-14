using HubDisplaySwitch.Classes;
using HubDisplaySwitch.Classes.Settings;
using HubDisplaySwitch.Services;
using HubDisplaySwitch.Windows;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WinRT.Interop;
using static HubDisplaySwitch.Classes.NativeMethods;

namespace HubDisplaySwitch;

/// <summary>
/// Hidden, resident host window (1×1, off-screen, never closes). Owns the tray icon (raw Shell_NotifyIcon
/// on this HWND), the <see cref="SwitchEngine"/>, the switcher flyout and the on-screen popup. Exit happens
/// only from the tray menu.
/// </summary>
public sealed partial class MainWindow : Window
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public const string HostTitle = "Hub Display Switch Host";
    private static readonly Mutex Singleton = new(true, "HubDisplaySwitch");

    /// <summary>Stable tray GUID so Windows remembers the icon's show/hide preference across updates.</summary>
    private static readonly Guid TrayGuid = new("5b0f1c7e-3c1a-4e8e-9a35-6f1d2a7c4b11");
    private const uint TrayUid = 1;
    private const string StartupTaskId = "HubDisplaySwitchStartup";
    private const string RunValueName = "Hub Display Switch";

    public static SwitchEngine Engine { get; private set; } = null!;
    public static MainWindow Instance { get; private set; } = null!;

    private readonly IntPtr _hwnd;
    private readonly SUBCLASSPROC _wndProcDelegate;   // kept in a field so it isn't garbage-collected
    private readonly int _wmTrayCallback, _wmShowSettings, _wmTaskbarCreated;
    private readonly SwitcherWindow _switcher;
    private readonly OsdWindow _osd;
    private System.Drawing.Icon? _trayIcon;
    private bool _trayAdded;
    private readonly global::Windows.UI.ViewManagement.UISettings _uiSettings = new();

    public MainWindow()
    {
        InitializeComponent();

        // Single instance: hand "show settings" to the running copy and exit.
        if (!Singleton.WaitOne(TimeSpan.Zero, true))
        {
            IntPtr existing = FindWindow(null, HostTitle);
            if (existing != IntPtr.Zero && !IsSilentLaunch())
                PostMessage(existing, (uint)RegisterWindowMessage("HubDisplaySwitch_ShowSettings"), 0, 0);
            Environment.Exit(0);
        }

        Instance = this;
        _hwnd = WindowNative.GetWindowHandle(this);
        AddExStyle(_hwnd, WS_EX_TOOLWINDOW);
        var appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd));
        appWindow.IsShownInSwitchers = false;
        appWindow.Move(new global::Windows.Graphics.PointInt32(-9999, -9999));
        appWindow.Resize(new global::Windows.Graphics.SizeInt32(1, 1));
        appWindow.Closing += (s, e) => e.Cancel = true;

        _wmTrayCallback = RegisterWindowMessage("HubDisplaySwitch_TrayCallback");
        _wmShowSettings = RegisterWindowMessage("HubDisplaySwitch_ShowSettings");
        _wmTaskbarCreated = RegisterWindowMessage("TaskbarCreated");
        _wndProcDelegate = WndProc;
        SetWindowSubclass(_hwnd, _wndProcDelegate, 0, 0);
        RegisterApplicationRestart("--silent", 0);

        Engine = new SwitchEngine(DispatcherQueue);
        _osd = new OsdWindow();
        _switcher = new SwitcherWindow();
        Engine.Popup += (title, subtitle, state) => _osd.Present(title, subtitle, state);
        Engine.StateChanged += () => { UpdateTrayIcon(); _switcher.Refresh(); };

        AddTrayIcon();
        ThemeManager.ThemeChanged += UpdateTrayIcon;
        _uiSettings.ColorValuesChanged += (s, e) => DispatcherQueue.TryEnqueue(UpdateTrayIcon);

        _ = ApplyFirstRunStartupAsync();
        MigrateStartupRegistryEntry();

        Logger.Info("Started v{Version}, {Count} setup(s), packaged={Packaged}",
            typeof(App).Assembly.GetName().Version?.ToString(3), SettingsManager.Current.Setups.Count, IsPackaged);
        Engine.Start();

        if (!IsSilentLaunch() || SettingsManager.IsFirstRun) SettingsWindow.ShowInstance();
    }

    // ── Packaging / launch ──────────────────────────────────────────────
    public static bool IsPackaged
    {
        get
        {
            try { return global::Windows.ApplicationModel.Package.Current.Id != null; }
            catch { return false; }
        }
    }

    private static bool IsSilentLaunch()
    {
        if (Environment.GetCommandLineArgs().Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase))) return true;
        try
        {
            return Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs().Kind
                   == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.StartupTask;
        }
        catch { return false; }
    }

    // ── Start with Windows ──────────────────────────────────────────────
    /// <summary>Register login startup once on first run. Never overrides DisabledByUser / policy.</summary>
    private static async Task ApplyFirstRunStartupAsync()
    {
        if (!SettingsManager.IsFirstRun || !SettingsManager.Current.Startup) return;
        try
        {
            if (IsPackaged)
            {
                var task = await global::Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId);
                if (task.State == global::Windows.ApplicationModel.StartupTaskState.Disabled)
                    await task.RequestEnableAsync();
            }
            else SetUnpackagedStartup(true);
        }
        catch (Exception ex) { Logger.Warn(ex, "First-run startup registration failed"); }
    }

    public static async Task<bool> IsStartupEnabledAsync()
    {
        if (!IsPackaged)
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
            return key?.GetValue(RunValueName) != null;
        }
        var task = await global::Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId);
        return task.State is global::Windows.ApplicationModel.StartupTaskState.Enabled
                          or global::Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
    }

    /// <summary>Returns the resulting enabled state (the user or policy can refuse).</summary>
    public static async Task<bool> SetStartupAsync(bool enabled)
    {
        if (!IsPackaged) { SetUnpackagedStartup(enabled); return enabled; }
        var task = await global::Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId);
        if (!enabled) { task.Disable(); return false; }
        var state = await task.RequestEnableAsync();
        return state is global::Windows.ApplicationModel.StartupTaskState.Enabled
                     or global::Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
    }

    private static void SetUnpackagedStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\" --silent");
        else if (key.GetValue(RunValueName) != null) key.DeleteValue(RunValueName);
    }

    /// <summary>When packaged, remove Run-key entries left by unpackaged builds (including the original "HubDisplaySwitch" value).</summary>
    private static void MigrateStartupRegistryEntry()
    {
        if (!IsPackaged) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            foreach (var name in new[] { RunValueName, "HubDisplaySwitch" })
                if (key?.GetValue(name) != null) { key.DeleteValue(name); Logger.Info("Removed legacy Run entry {Name}", name); }
        }
        catch (Exception ex) { Logger.Warn(ex, "Couldn't clean legacy Run entry"); }
    }

    // ── Tray icon ───────────────────────────────────────────────────────
    private static bool TaskbarIsDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("SystemUsesLightTheme") is not int v || v == 0;
        }
        catch { return true; }
    }

    private string TrayTooltip()
    {
        var s = SettingsManager.Current;
        string tip = $"Hub Display Switch – {Engine.Active?.Name ?? "no setup"}{(s.Paused ? " (paused)" : "")}";
        return tip.Length > 127 ? tip[..127] : tip;
    }

    private NOTIFYICONDATA TrayData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = TrayUid,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_GUID,
        uCallbackMessage = (uint)_wmTrayCallback,
        hIcon = _trayIcon?.Handle ?? IntPtr.Zero,
        szTip = TrayTooltip(),
        szInfo = "",
        szInfoTitle = "",
        guidItem = TrayGuid,
    };

    /// <summary>Tray icons use the small-icon size at the main display's scaling (e.g. 24 px at 150%, 40 px at 250%).</summary>
    private static int TrayIconSize()
    {
        var (_, _, scale) = PrimaryMonitorArea();
        return GetSystemMetricsForDpi(SM_CXSMICON, (uint)Math.Round(96 * scale));
    }

    private void AddTrayIcon()
    {
        _trayIcon?.Dispose();
        _trayIcon = TrayIconRenderer.Create(SettingsManager.Current.Paused, TaskbarIsDark(), TrayIconSize());
        var data = TrayData();
        // A stale registration (crash, previous run) makes NIM_ADD fail for the same GUID.
        var del = new NOTIFYICONDATA { cbSize = data.cbSize, uFlags = NIF_GUID, guidItem = TrayGuid, szTip = "", szInfo = "", szInfoTitle = "" };
        Shell_NotifyIcon(NIM_DELETE, ref del);
        _trayAdded = Shell_NotifyIcon(NIM_ADD, ref data);
        if (!_trayAdded) Logger.Warn("Shell_NotifyIcon(NIM_ADD) failed");
    }

    public void UpdateTrayIcon()
    {
        if (!_trayAdded) { AddTrayIcon(); return; }
        var old = _trayIcon;
        _trayIcon = TrayIconRenderer.Create(SettingsManager.Current.Paused, TaskbarIsDark(), TrayIconSize());
        var data = TrayData();
        Shell_NotifyIcon(NIM_MODIFY, ref data);
        old?.Dispose();
    }

    private void RemoveTrayIcon()
    {
        var data = new NOTIFYICONDATA { cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(), uFlags = NIF_GUID, guidItem = TrayGuid, szTip = "", szInfo = "", szInfoTitle = "" };
        Shell_NotifyIcon(NIM_DELETE, ref data);
        _trayAdded = false;
    }

    /// <summary>Centre of the tray icon's rectangle, or the cursor if Windows can't say.</summary>
    private (int x, int y) TrayAnchor()
    {
        GetCursorPos(out var pt);
        var id = new NOTIFYICONIDENTIFIER { cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(), hWnd = _hwnd, uID = TrayUid, guidItem = TrayGuid };
        if (Shell_NotifyIconGetRect(ref id, out var r) == 0 && r.Right > r.Left && r.Bottom > r.Top)
            return ((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
        return (pt.X, pt.Y);
    }

    private void ShowContextMenu(int x, int y)
    {
        var s = SettingsManager.Current;
        var popup = new H.NotifyIcon.Core.PopupMenu();
        foreach (var setup in s.Setups.ToList())
        {
            var item = new H.NotifyIcon.Core.PopupMenuItem
            {
                Text = (Engine.Active?.Id == setup.Id ? "● " : "    ") + setup.Name,
            };
            item.Click += (_, _) => DispatcherQueue.TryEnqueue(() => Engine.Apply(setup, "Switched manually"));
            popup.Items.Add(item);
        }
        popup.Items.Add(new H.NotifyIcon.Core.PopupMenuSeparator());

        var pause = new H.NotifyIcon.Core.PopupMenuItem { Text = s.Paused ? "Resume auto-switching" : "Pause auto-switching" };
        pause.Click += (_, _) => DispatcherQueue.TryEnqueue(() => Engine.SetPaused(!SettingsManager.Current.Paused));
        popup.Items.Add(pause);

        var settings = new H.NotifyIcon.Core.PopupMenuItem { Text = "Settings" };
        settings.Click += (_, _) => DispatcherQueue.TryEnqueue(SettingsWindow.ShowInstance);
        popup.Items.Add(settings);

        var logs = new H.NotifyIcon.Core.PopupMenuItem { Text = "Open logs folder" };
        logs.Click += (_, _) => OpenLogsFolder();
        popup.Items.Add(logs);

        popup.Items.Add(new H.NotifyIcon.Core.PopupMenuSeparator());
        var exit = new H.NotifyIcon.Core.PopupMenuItem { Text = "Exit" };
        exit.Click += (_, _) => DispatcherQueue.TryEnqueue(ExitApp);
        popup.Items.Add(exit);

        popup.Show(_hwnd, x, y);
    }

    public static void OpenLogsFolder()
    {
        try
        {
            var dir = SettingsManager.PhysicalSettingsDir;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Logger.Warn(ex, "Couldn't open logs folder"); }
    }

    public void ExitApp()
    {
        Logger.Info("Exit requested");
        SettingsManager.SaveSettings();
        Engine.Dispose();
        RemoveTrayIcon();
        NLog.LogManager.Flush();
        Environment.Exit(0);
    }

    // ── Window procedure ────────────────────────────────────────────────
    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr refData)
    {
        if (_wmTrayCallback != 0 && (int)msg == _wmTrayCallback)
        {
            int notification = (int)(lParam.ToInt64() & 0xFFFF);
            if (notification == WM_LBUTTONUP)
            {
                var (ax, ay) = TrayAnchor();
                DispatcherQueue.TryEnqueue(() => _switcher.Toggle(ax, ay));
            }
            else if (notification == WM_RBUTTONUP || notification == WM_CONTEXTMENU)
            {
                GetCursorPos(out var pt);
                DispatcherQueue.TryEnqueue(() => ShowContextMenu(pt.X, pt.Y));
            }
            return IntPtr.Zero;
        }
        if (_wmShowSettings != 0 && (int)msg == _wmShowSettings)
        {
            DispatcherQueue.TryEnqueue(SettingsWindow.ShowInstance);
            return IntPtr.Zero;
        }
        if (_wmTaskbarCreated != 0 && (int)msg == _wmTaskbarCreated)
        {
            // Explorer restarted: the tray icon is gone and must be re-added.
            DispatcherQueue.TryEnqueue(AddTrayIcon);
        }
        else if (msg == WM_DISPLAYCHANGE)
        {
            // Follow the new layout: re-centre a visible popup, re-park hidden windows, and
            // re-render the tray icon for the (possibly different) main display's scaling.
            DispatcherQueue.TryEnqueue(() => { _osd.Reposition(); _switcher.Dismiss(); _switcher.Park(); UpdateTrayIcon(); });
        }
        else if (msg == WM_DPICHANGED || msg == WM_SETTINGCHANGE)
        {
            DispatcherQueue.TryEnqueue(UpdateTrayIcon);
        }
        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }
}
