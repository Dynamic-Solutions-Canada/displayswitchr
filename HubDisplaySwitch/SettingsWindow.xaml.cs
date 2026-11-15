using HubDisplaySwitch.Classes.Settings;
using HubDisplaySwitch.Models;
using HubDisplaySwitch.Pages;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.IO;
using WinRT.Interop;
using static HubDisplaySwitch.Classes.NativeMethods;

namespace HubDisplaySwitch;

/// <summary>
/// Settings window (Mica + NavigationView). Each setup is a navigation item; the built-in Settings item
/// is the General page. Edits apply and save immediately (Windows Settings style).
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private static SettingsWindow? instance;
    private IntPtr _hIconSmall, _hIconBig;
    private const string AddTag = "add";

    public static SettingsWindow? GetCurrent() => instance;

    public static void ShowInstance()
    {
        if (instance != null)
        {
            var hwnd = WindowNative.GetWindowHandle(instance);
            var appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd));
            if (appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } p) p.Restore();
            SetForegroundWindow(hwnd);
            return;
        }
        new SettingsWindow().Activate();
    }

    private SettingsWindow()
    {
        InitializeComponent();
        instance = this;
        Closed += SettingsWindow_Closed;

        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var hwnd = WindowNative.GetWindowHandle(this);
        var appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd));

        if (File.Exists(App.IconPath))
        {
            ApplyWindowIcon(hwnd, App.IconPath);
            SetAppWindowIcon(appWindow, App.IconPath);
            try { TitleBarIcon.Source = new BitmapImage(new Uri(App.IconPngPath)); } catch { }
        }

        double scale = GetDpiForWindow(hwnd) / 96.0;
        var settings = SettingsManager.Current;
        if (settings.SettingsWindowWidth >= 400 && settings.SettingsWindowHeight >= 300)
        {
            appWindow.Resize(new global::Windows.Graphics.SizeInt32(settings.SettingsWindowWidth, settings.SettingsWindowHeight));
            appWindow.Move(new global::Windows.Graphics.PointInt32(settings.SettingsWindowX, settings.SettingsWindowY));
        }
        else
        {
            int width = (int)(980 * scale), height = (int)(760 * scale);
            appWindow.Resize(new global::Windows.Graphics.SizeInt32(width, height));
            GetCursorPos(out var pt);
            var (_, work, _) = MonitorArea(MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST));
            appWindow.Move(new global::Windows.Graphics.PointInt32(
                work.Left + (work.Right - work.Left - width) / 2, work.Top + (work.Bottom - work.Top - height) / 2));
        }

        RebuildSetupItems(MainWindow.Engine?.Active?.Id ?? settings.Setups.FirstOrDefault()?.Id);
        Classes.ThemeManager.ApplySavedTheme(this);

        Activated += (s, e) =>
        {
            if (_hIconBig != IntPtr.Zero)
            {
                var h = WindowNative.GetWindowHandle(this);
                SendMessage(h, WM_SETICON, ICON_SMALL, _hIconSmall);
                SendMessage(h, WM_SETICON, ICON_BIG, _hIconBig);
            }
        };
    }

    // ── Navigation ───────────────────────────────────────────────────────
    /// <summary>Rebuild the setup items (after add/delete/rename) and select one.</summary>
    public void RebuildSetupItems(string? selectId)
    {
        RootNavigation.MenuItems.Clear();
        RootNavigation.MenuItems.Add(new NavigationViewItemHeader { Content = "Setups" });
        NavigationViewItem? select = null;
        foreach (var s in SettingsManager.Current.Setups)
        {
            var item = new NavigationViewItem
            {
                Content = s.Name,
                Tag = s.Id,
                Icon = new FontIcon { Glyph = s.Glyph },
            };
            RootNavigation.MenuItems.Add(item);
            if (s.Id == selectId) select = item;
        }
        RootNavigation.MenuItems.Add(new NavigationViewItem
        {
            Content = "Add setup",
            Tag = AddTag,
            SelectsOnInvoked = false,
            Icon = new FontIcon { Glyph = "" },
        });

        if (select != null) RootNavigation.SelectedItem = select;
        else if (SettingsManager.Current.Setups.Count == 0) RootNavigation.SelectedItem = RootNavigation.SettingsItem;
        else RootNavigation.SelectedItem = RootNavigation.MenuItems.OfType<NavigationViewItem>().First();
    }

    /// <summary>Update a setup's nav item text/icon in place (no reselect, keeps the page).</summary>
    public void UpdateSetupItem(Setup s)
    {
        foreach (var item in RootNavigation.MenuItems.OfType<NavigationViewItem>())
            if (item.Tag as string == s.Id)
            {
                item.Content = s.Name;
                item.Icon = new FontIcon { Glyph = s.Glyph };
            }
    }

    private void RootNavigation_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag as string != AddTag) return;
        var setup = new Setup { Name = "New setup", Glyph = Glyphs.Monitor };
        if (SettingsManager.Current.Setups.Count == 0) setup.IsDefault = true;
        SettingsManager.Current.Setups.Add(setup);
        SettingsManager.SaveSettings();
        MainWindow.Engine.SettingsChanged();
        RebuildSetupItems(setup.Id);
    }

    private void RootNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected) { Navigate(typeof(GeneralPage), null); return; }
        if ((args.SelectedItem as NavigationViewItem)?.Tag is not string tag || tag == AddTag) return;
        if (tag == "AboutPage") { Navigate(typeof(AboutPage), null); return; }
        if (tag == "PromoPage") { Navigate(typeof(TechnicallyReal.Promo.PromotedAppsPage), null); return; }
        Navigate(typeof(SetupPage), tag);
    }

    private void Navigate(Type page, string? parameter)
    {
        if (ContentFrame.Content is SetupPage current && page == typeof(SetupPage) && current.SetupId == parameter) return;
        if (ContentFrame.Content?.GetType() == page && page != typeof(SetupPage)) return;
        ContentFrame.Navigate(page, parameter);
    }

    // ── Icon / geometry ──────────────────────────────────────────────────
    private static void SetAppWindowIcon(AppWindow appWindow, string icoPath)
    {
        var hIcon = LoadImage(IntPtr.Zero, icoPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE);
        if (hIcon == IntPtr.Zero) return;
        try { appWindow.SetIcon(Microsoft.UI.Win32Interop.GetIconIdFromIcon(hIcon)); }
        finally { DestroyIcon(hIcon); }
    }

    /// <summary>Load the icon frames at this window's scaling (fixed 16/32 px get stretched and look blurry).</summary>
    private void ApplyWindowIcon(IntPtr hwnd, string icoPath)
    {
        uint dpi = GetDpiForWindow(hwnd);
        int small = GetSystemMetricsForDpi(SM_CXSMICON, dpi), big = GetSystemMetricsForDpi(SM_CXICON, dpi);
        _hIconSmall = LoadImage(IntPtr.Zero, icoPath, IMAGE_ICON, small, small, LR_LOADFROMFILE);
        _hIconBig = LoadImage(IntPtr.Zero, icoPath, IMAGE_ICON, big, big, LR_LOADFROMFILE);
        if (_hIconSmall != IntPtr.Zero) SendMessage(hwnd, WM_SETICON, ICON_SMALL, _hIconSmall);
        if (_hIconBig != IntPtr.Zero) SendMessage(hwnd, WM_SETICON, ICON_BIG, _hIconBig);
    }

    private void SettingsWindow_Closed(object sender, WindowEventArgs e)
    {
        instance = null;
        var hwnd = WindowNative.GetWindowHandle(this);
        var appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd));
        var settings = SettingsManager.Current;
        bool normal = appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored };
        if (normal)
        {
            settings.SettingsWindowX = appWindow.Position.X;
            settings.SettingsWindowY = appWindow.Position.Y;
            settings.SettingsWindowWidth = appWindow.Size.Width;
            settings.SettingsWindowHeight = appWindow.Size.Height;
        }
        SettingsManager.SaveSettings();
        if (_hIconSmall != IntPtr.Zero) DestroyIcon(_hIconSmall);
        if (_hIconBig != IntPtr.Zero) DestroyIcon(_hIconBig);
    }
}
