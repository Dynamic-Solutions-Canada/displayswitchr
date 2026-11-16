using HubDisplaySwitch.Classes.Settings;
using HubDisplaySwitch.Models;
using HubDisplaySwitch.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUIAppKit.Localization;
using WinUIAppKit.Theming;
using WinUIAppKit.Windowing;

namespace HubDisplaySwitch;

/// <summary>
/// Settings window (Mica + NavigationView). Each setup is a navigation item; the built-in Settings item
/// is the General page. Edits apply and save immediately (Windows Settings style). Icon, saved placement
/// and theme come from the app kit.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private static SettingsWindow? instance;
    private const string AddTag = "add";

    public static SettingsWindow? GetCurrent() => instance;

    public static void ShowInstance()
    {
        if (instance != null)
        {
            WindowChrome.BringToFront(instance);
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

        WindowIcon.Apply(this, App.IconPath);
        try { TitleBarIcon.Source = new BitmapImage(new Uri(App.IconPngPath)); } catch { }
        WindowChrome.RestorePlacement(this, SettingsManager.Current.SettingsWindow, 980, 760);
        AppTheme.Track(this);

        RebuildSetupItems(MainWindow.Engine?.Active?.Id ?? SettingsManager.Current.Setups.FirstOrDefault()?.Id);
        RootNavigation.Loaded += (_, _) => LocalizeSettingsItem();
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
            if (s.Id == selectId) select = item;
        }
        RootNavigation.MenuItems.Add(new NavigationViewItem
        {
            Content = UiText.Get("Add setup"),
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
        var setup = new Setup { Name = UiText.Get("New setup"), Glyph = Glyphs.Monitor };
        if (SettingsManager.Current.Setups.Count == 0) setup.IsDefault = true;
        SettingsManager.Current.Setups.Add(setup);
        SettingsManager.SaveSettings();
        MainWindow.Engine.SettingsChanged();
        RebuildSetupItems(setup.Id);
    }

    private void RootNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_rebuildingQuietly) return;
        if (args.IsSettingsSelected) { Navigate(typeof(GeneralPage), null); return; }
        if ((args.SelectedItem as NavigationViewItem)?.Tag is not string tag || tag == AddTag) return;
        if (tag == "AboutPage") { Navigate(typeof(AboutPage), null); return; }
        if (tag == "PromoPage") { Navigate(typeof(TechnicallyReal.Promo.PromotedAppsPage), null); return; }
        Navigate(typeof(SetupPage), tag);
    }

    private void Navigate(Type page, string? parameter, bool force = false)
    {
        if (!force && ContentFrame.Content is SetupPage current && page == typeof(SetupPage) && current.SetupId == parameter) return;
        if (!force && ContentFrame.Content?.GetType() == page && page != typeof(SetupPage)) return;
        ContentFrame.Navigate(page, parameter);
    }

    /// <summary>WinUI's built-in Settings item follows the Windows language; label it ourselves.</summary>
    private void LocalizeSettingsItem()
    {
        if (RootNavigation.SettingsItem is NavigationViewItem item) Localize.SetContent(item, "Settings");
    }

    /// <summary>Re-label the navigation and reload the current page (its code-built rows hold text too).</summary>
    private void OnLanguageChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        string? setupId = (ContentFrame.Content as SetupPage)?.SetupId;
        var selected = RootNavigation.SelectedItem;
        _rebuildingQuietly = true;
        try
        {
            RebuildSetupItems(setupId);
            if (setupId == null) RootNavigation.SelectedItem = selected;   // Settings / About / promo stay selected
        }
        finally { _rebuildingQuietly = false; }
        // XAML text refreshes itself (Localize); setup pages also build rows in code, so reload those.
        if (setupId != null) Navigate(typeof(SetupPage), setupId, force: true);
    });

    private bool _rebuildingQuietly;

    private void SettingsWindow_Closed(object sender, WindowEventArgs e)
    {
        instance = null;
        UiText.Changed -= OnLanguageChanged;
        // Minimized or maximized: keep the last normal placement.
        if (WindowChrome.CapturePlacement(this) is { } placement) SettingsManager.Current.SettingsWindow = placement;
        SettingsManager.SaveSettings();
    }
}
