using HubDisplaySwitch.Classes;
using HubDisplaySwitch.Classes.Settings;
using HubDisplaySwitch.Models;
using HubDisplaySwitch.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIAppKit.Localization;
using WinUIAppKit.Pages;
using WinUIAppKit.Windowing;

namespace HubDisplaySwitch;

/// <summary>
/// The settings window, hosted in the kit's <see cref="SettingsShell"/> (UI-1, UI-2): one page per setup,
/// an "Add setup" action, Settings (the General page), Our other apps and About. Edits apply and save
/// immediately (Windows Settings style). One instance at a time.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    // The "Add setup" action item's tag. Setup items are tagged with the setup's id (a GUID), so neither
    // can collide with each other or with the shell's own tags.
    private const string AddTag = "add";

    private static SettingsWindow? _instance;

    /// <summary>Set while the menu is rebuilt for a language change, so re-selecting an item doesn't navigate.</summary>
    private bool _rebuildingQuietly;

    /// <summary>The open window, or null when it is closed. Pages use it to ask questions on it.</summary>
    public static SettingsWindow? GetCurrent() => _instance;

    // ── Opening ─────────────────────────────────────────────────────────
    /// <summary>Show the window (creating it if needed) on its current page (the active setup for a new window).</summary>
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
    /// so the page can show the kit's reset notice (CONF-2) even when the app started silently. The shell
    /// applies the selection once it has loaded.
    /// </summary>
    public static void ShowSettings()
    {
        ShowInstance();
        _instance?.Shell.Select(SettingsShell.SettingsTag);
    }

    private SettingsWindow()
    {
        InitializeComponent();
        _instance = this;
        Closed += SettingsWindow_Closed;

        Shell.Caption = "DisplaySwitchr";
        Shell.IconPngPath = App.IconPngPath;
        WindowChrome.SetUpSettingsWindow(this, Shell.TitleBar, App.IconPath, SettingsManager.Current.SettingsWindow, 980, 760);
        Shell.PageSelected += OnPageSelected;
        Shell.ActionInvoked += OnActionInvoked;

        RebuildSetupItems(MainWindow.Engine?.Active?.Id ?? SettingsManager.Current.Setups.FirstOrDefault()?.Id);
        UiText.Changed += OnLanguageChanged;
    }

    // ── Menu ────────────────────────────────────────────────────────────
    /// <summary>
    /// Rebuild the setup items (after add, delete, rename or reset) and select one: the setup with
    /// <paramref name="selectId"/>, else the first setup, else Settings when there are no setups.
    /// </summary>
    /// <param name="selectId">The id of the setup to show, or null for the first.</param>
    public void RebuildSetupItems(string? selectId)
    {
        BuildMenu();

        var setups = SettingsManager.Current.Setups;
        if (setups.Any(s => s.Id == selectId))
        {
            Shell.Select(selectId!);
        }
        else if (setups.Count == 0)
        {
            Shell.Select(SettingsShell.SettingsTag);
        }
        else
        {
            Shell.Select(setups[0].Id);
        }
    }

    /// <summary>Update a setup's nav item text and icon in place (no reselect, keeps the page).</summary>
    public void UpdateSetupItem(Setup s)
    {
        foreach (var item in Shell.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag as string == s.Id)
            {
                item.Content = s.Name;
                item.Icon = new FontIcon { Glyph = s.Glyph };
            }
        }
    }

    /// <summary>A "Setups" header, one item per setup, then the "Add setup" action item.</summary>
    private void BuildMenu()
    {
        Shell.MenuItems.Clear();
        Shell.MenuItems.Add(new NavigationViewItemHeader { Content = UiText.Get("Setups") });
        foreach (var s in SettingsManager.Current.Setups)
        {
            Shell.MenuItems.Add(new NavigationViewItem
            {
                Content = s.Name,
                Tag = s.Id,
                Icon = new FontIcon { Glyph = s.Glyph },
            });
        }

        // An action item: the shell reports it through ActionInvoked and never selects it.
        Shell.MenuItems.Add(new NavigationViewItem
        {
            Content = UiText.Get("Add setup"),
            Tag = AddTag,
            SelectsOnInvoked = false,
            Icon = new FontIcon { Glyph = "\uE710" },
        });
    }

    /// <summary>"Add setup": a new setup (the default one when it is the first), saved and shown.</summary>
    private void OnActionInvoked(string tag)
    {
        if (tag != AddTag)
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

    // ── Navigation ──────────────────────────────────────────────────────
    private void OnPageSelected(string tag)
    {
        if (_rebuildingQuietly)
        {
            return;
        }

        if (tag == SettingsShell.SettingsTag)
        {
            Navigate(typeof(GeneralPage), null);
        }
        else if (tag == SettingsShell.AboutTag)
        {
            Navigate(typeof(WinUIAppKit.Pages.AboutPage), AboutContent.Create());
        }
        else if (tag == SettingsShell.OtherAppsTag)
        {
            Navigate(typeof(TechnicallyReal.Promo.PromotedAppsPage), null);
        }
        else
        {
            Navigate(typeof(SetupPage), tag);
        }
    }

    /// <summary>Show a page, unless that exact page (and setup) is already showing or <paramref name="force"/> is set.</summary>
    /// <param name="parameter">The setup id for a setup page, the <see cref="WinUIAppKit.Pages.AboutInfo"/> for About, otherwise null.</param>
    private void Navigate(Type page, object? parameter, bool force = false)
    {
        var frame = Shell.ContentFrame;
        if (!force && frame.Content is SetupPage current && page == typeof(SetupPage) && current.SetupId == parameter as string)
        {
            return;
        }

        if (!force && frame.Content?.GetType() == page && page != typeof(SetupPage))
        {
            return;
        }

        frame.Navigate(page, parameter);
    }

    /// <summary>
    /// The interface language changed. The shell relabels its own items and XAML text refreshes itself;
    /// the setup menu and the setup page's code-built rows hold text too, so rebuild the menu with the
    /// same item selected and reload a setup page.
    /// </summary>
    private void OnLanguageChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        string? tag = Shell.SelectedTag;
        _rebuildingQuietly = true;
        try
        {
            BuildMenu();
            if (tag != null)
            {
                Shell.Select(tag);
            }
        }
        finally
        {
            _rebuildingQuietly = false;
        }

        if (Shell.ContentFrame.Content is SetupPage page)
        {
            Navigate(typeof(SetupPage), page.SetupId, force: true);
        }
    });

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
