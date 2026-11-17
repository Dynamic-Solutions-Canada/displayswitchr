using HubDisplaySwitch.Classes;
using HubDisplaySwitch.Classes.Settings;
using HubDisplaySwitch.Models;
using HubDisplaySwitch.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUIAppKit.Localization;
using WinUIAppKit.Windowing;

namespace HubDisplaySwitch;

/// <summary>
/// Settings window (Mica + NavigationView). Each setup is a navigation item; the built-in Settings item
/// is the General page. Edits apply and save immediately (Windows Settings style). Icon, saved placement
/// and theme come from the app kit.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private static SettingsWindow? _instance;
    private const string AddTag = "add";

    public static SettingsWindow? GetCurrent() => _instance;

    public static void ShowInstance()
    {
        if (_instance != null)
        {
            WindowChrome.BringToFront(_instance);
            return;
        }
        new SettingsWindow().Activate();
    }

    /// <summary>
    /// Open the window on the Settings page. Used on the launch that found the settings file unreadable,
    /// so the page can show the kit's reset notice (CONF-2) even when the app started silently.
    /// </summary>
    public static void ShowSettings()
    {
        _openOnSettings = true;
        ShowInstance();
        _instance?.SelectSettingsPage();
    }

    /// <summary>Set by <see cref="ShowSettings"/> before the window exists: select Settings once it has loaded.</summary>
    private static bool _openOnSettings;

    private SettingsWindow()
    {
        InitializeComponent();
        _instance = this;
        Closed += SettingsWindow_Closed;

        // The kit's settings-window setup (UI-1): Mica, the custom title bar, the icon, the 500 x 500
        // minimum, the saved placement and the theme.
        WindowChrome.SetUpSettingsWindow(this, AppTitleBar, App.IconPath, SettingsManager.Current.SettingsWindow, 980, 760);
        try { TitleBarIcon.Source = new BitmapImage(new Uri(App.IconPngPath)); } catch { }

        RebuildSetupItems(MainWindow.Engine?.Active?.Id ?? SettingsManager.Current.Setups.FirstOrDefault()?.Id);
        RootNavigation.Loaded += (_, _) =>
        {
            LocalizeSettingsItem();
            if (_openOnSettings)
            {
                SelectSettingsPage();
            }
        };
        UiText.Changed += OnLanguageChanged;
    }

    // ── Navigation ───────────────────────────────────────────────────────
    /// <summary>Rebuild the setup items (after add/delete/rename) and select one.</summary>
    public void RebuildSetupItems(string? selectId)
    {
        RootNavigation.MenuItems.Clear();
        RootNavigation.MenuItems.Add(new NavigationViewItemHeader { Content = UiText.Get("Setups") });
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
            if (s.Id == selectId)
            {
                select = item;
            }
        }
        RootNavigation.MenuItems.Add(new NavigationViewItem
        {
            Content = UiText.Get("Add setup"),
            Tag = AddTag,
            SelectsOnInvoked = false,
            Icon = new FontIcon { Glyph = "" },
        });

        if (select != null)
        {
            RootNavigation.SelectedItem = select;
        }
        else if (SettingsManager.Current.Setups.Count == 0)
        {
            RootNavigation.SelectedItem = RootNavigation.SettingsItem;
        }
        else
        {
            RootNavigation.SelectedItem = RootNavigation.MenuItems.OfType<NavigationViewItem>().First();
        }
    }

    /// <summary>Update a setup's nav item text/icon in place (no reselect, keeps the page).</summary>
    public void UpdateSetupItem(Setup s)
    {
        foreach (var item in RootNavigation.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag as string == s.Id)
            {
                item.Content = s.Name;
                item.Icon = new FontIcon { Glyph = s.Glyph };
            }
        }
    }

    private void RootNavigation_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag as string != AddTag)
        {
            return;
        }

        var setup = new Setup { Name = UiText.Get("New setup"), Glyph = Glyphs.Monitor };
        if (SettingsManager.Current.Setups.Count == 0)
        {
            setup.IsDefault = true;
        }

        SettingsManager.Current.Setups.Add(setup);
        SettingsManager.SaveSettings();
        MainWindow.Engine.SettingsChanged();
        RebuildSetupItems(setup.Id);
    }

    private void RootNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_rebuildingQuietly)
        {
            return;
        }

        if (args.IsSettingsSelected) { Navigate(typeof(GeneralPage), null); return; }
        if ((args.SelectedItem as NavigationViewItem)?.Tag is not string tag || tag == AddTag)
        {
            return;
        }

        if (tag == "AboutPage") { Navigate(typeof(WinUIAppKit.Pages.AboutPage), AboutContent.Create()); return; }
        if (tag == "PromoPage") { Navigate(typeof(TechnicallyReal.Promo.PromotedAppsPage), null); return; }
        Navigate(typeof(SetupPage), tag);
    }

    /// <summary>Show a page, unless that exact page (and setup) is already showing or <paramref name="force"/> is set.</summary>
    /// <param name="parameter">The setup id for a setup page, the <see cref="WinUIAppKit.Pages.AboutInfo"/> for About, otherwise null.</param>
    private void Navigate(Type page, object? parameter, bool force = false)
    {
        if (!force && ContentFrame.Content is SetupPage current && page == typeof(SetupPage) && current.SetupId == parameter as string)
        {
            return;
        }

        if (!force && ContentFrame.Content?.GetType() == page && page != typeof(SetupPage))
        {
            return;
        }

        ContentFrame.Navigate(page, parameter);
    }

    /// <summary>Select the built-in Settings item once it exists (it appears when the navigation view loads).</summary>
    private void SelectSettingsPage()
    {
        if (RootNavigation.SettingsItem is not null)
        {
            _openOnSettings = false;
            RootNavigation.SelectedItem = RootNavigation.SettingsItem;
        }
    }

    /// <summary>
    /// WinUI's built-in Settings item takes its label, tooltip and screen-reader name from the Windows
    /// language; give all three the kit's shared label so they follow the app's language (UI-14).
    /// </summary>
    private void LocalizeSettingsItem()
    {
        if (RootNavigation.SettingsItem is NavigationViewItem item)
        {
            string label = UiText.Get("kit.settings.title");
            item.Content = label;
            ToolTipService.SetToolTip(item, label);
            AutomationProperties.SetName(item, label);
        }
    }

    /// <summary>Re-label the navigation and reload the current page (its code-built rows hold text too).</summary>
    private void OnLanguageChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        LocalizeSettingsItem();
        string? setupId = (ContentFrame.Content as SetupPage)?.SetupId;
        var selected = RootNavigation.SelectedItem;
        _rebuildingQuietly = true;
        try
        {
            RebuildSetupItems(setupId);
            if (setupId == null)
            {
                RootNavigation.SelectedItem = selected;   // Settings / About / promo stay selected
            }
        }
        finally { _rebuildingQuietly = false; }
        // XAML text refreshes itself (Localize); setup pages also build rows in code, so reload those.
        if (setupId != null)
        {
            Navigate(typeof(SetupPage), setupId, force: true);
        }
    });

    private bool _rebuildingQuietly;

    private void SettingsWindow_Closed(object sender, WindowEventArgs e)
    {
        _instance = null;
        UiText.Changed -= OnLanguageChanged;
        // Minimized or maximized: keep the last normal placement.
        if (WindowChrome.CapturePlacement(this) is { } placement)
        {
            SettingsManager.Current.SettingsWindow = placement;
        }

        SettingsManager.SaveSettings();
    }
}
