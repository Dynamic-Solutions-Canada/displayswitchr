using HubDisplaySwitch.Classes;
using HubDisplaySwitch.Classes.Settings;
using HubDisplaySwitch.Models;
using HubDisplaySwitch.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinUIAppKit.Localization;
using WinUIAppKit.Theming;
using WinUIAppKit.Windowing;

namespace HubDisplaySwitch.Windows;

/// <summary>
/// Tray flyout: one tile per setup, click to switch. Window behaviour (context-menu presenter, acrylic,
/// rounded corners, placement by the taskbar, cloak-and-park instead of hiding, reveal after render,
/// dismissal on deactivate / Esc, reopen guard) comes from the kit's <see cref="FlyoutController"/>;
/// this class only builds content.
/// </summary>
public sealed partial class SwitcherWindow : Window
{
    private const double WidthDip = 380;

    private readonly FlyoutController _controller;
    private bool _updatingToggle;

    public SwitcherWindow()
    {
        InitializeComponent();
        AppTheme.Track(this);
        Refresh();
        _controller = new FlyoutController(this, WindowRoot, WidthDip);
        _controller.Opening += Refresh;
    }

    /// <summary>Tray single click: open, or close if open.</summary>
    public void Toggle(int anchorX, int anchorY) => _controller.Toggle(anchorX, anchorY);

    /// <summary>Tray double-click: make sure it is open.</summary>
    public void ShowAt(int anchorX, int anchorY) => _controller.Show(anchorX, anchorY);

    public void Dismiss() => _controller.Dismiss();

    /// <summary>The display layout changed: close and re-park off-screen (Windows pulls off-screen windows back).</summary>
    public void OnDisplayChanged()
    {
        _controller.Dismiss();
        _controller.Park();
    }

    // ── Content ──────────────────────────────────────────────────────────
    public void Refresh()
    {
        var engine = MainWindow.Engine;
        var s = SettingsManager.Current;

        _updatingToggle = true;
        AutoSwitch.IsOn = !s.Paused;
        _updatingToggle = false;

        TilesPanel.Children.Clear();
        foreach (var setup in s.Setups)
        {
            TilesPanel.Children.Add(BuildTile(engine, setup));
        }

        if (s.Setups.Count == 0)
        {
            var empty = EmptyState.Create(UiText.Get("No setups yet. Add one in Settings."));
            empty.Margin = new Thickness(10, 4, 10, 8);
            TilesPanel.Children.Add(empty);
        }

        StatusText.Text = engine == null ? ""
            : engine.IsSwitching ? UiText.Format("Switching to {{setup}}…", ("setup", engine.Active?.Name))
            : s.Paused ? UiText.Get("Auto-switching is paused")
            : engine.Active != null ? UiText.Format("Using {{setup}}", ("setup", engine.Active.Name)) : "";

        // Wording or setups changed while it's open (e.g. language switch): re-fit the height.
        if (_controller is { IsOpen: true })
        {
            _controller.UpdateSize();
        }
    }

    private Button BuildTile(SwitchEngine? engine, Setup setup)
    {
        bool active = engine?.Active?.Id == setup.Id;
        var res = Application.Current.Resources;

        var circle = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(20),
            Background = (Brush)res[active ? "AccentFillColorDefaultBrush" : "SubtleFillColorSecondaryBrush"],
            Child = new FontIcon
            {
                Glyph = setup.Glyph,
                FontSize = 18,
                Foreground = (Brush)res[active ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorPrimaryBrush"],
            },
        };

        var detail = new List<string> { setup.DescribeDisplays() };
        if (setup.AudioName.Length > 0)
        {
            detail.Add(SwitchEngine.ShortAudio(setup.AudioName));
        }

        var text = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = setup.Name, Style = (Style)res["BodyStrongTextBlockStyle"], TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock
        {
            Text = string.Join("  ·  ", detail),
            Style = (Style)res["CaptionTextBlockStyle"],
            Foreground = (Brush)res["TextFillColorSecondaryBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        // No badge for a setup with no hubs; never look up an empty key (the kit logs missing keys).
        string badge = active ? UiText.Get("Active")
                     : setup.IsDefault ? UiText.Get("Default")
                     : engine != null && engine.IsHubConnected(setup) ? UiText.Get("Hub connected")
                     : setup.Hubs.Count > 0 ? UiText.Get("Hub not connected") : "";
        var badgeText = new TextBlock
        {
            Text = badge,
            Style = (Style)res["CaptionTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)res[active ? "AccentTextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush"],
            FontWeight = active ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(circle);
        Grid.SetColumn(text, 1); grid.Children.Add(text);
        Grid.SetColumn(badgeText, 2); grid.Children.Add(badgeText);

        var button = new Button
        {
            Content = grid,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 8, 12, 8),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        if (active)
        {
            button.Background = (Brush)res["SubtleFillColorSecondaryBrush"];
        }

        // The content is a layout, not text, so give screen readers the name and state explicitly (UI-14).
        string spoken = badge.Length > 0 ? $"{setup.Name}, {badge}" : setup.Name;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, spoken);

        button.Click += (_, _) =>
        {
            Dismiss();
            MainWindow.Engine.Apply(setup, SwitchReason.Manual);
        };
        return button;
    }

    private void AutoSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingToggle)
        {
            return;
        }

        MainWindow.Engine.SetPaused(!AutoSwitch.IsOn);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        Dismiss();
        SettingsWindow.ShowInstance();
    }
}
