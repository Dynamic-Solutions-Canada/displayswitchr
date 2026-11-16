using HubDisplaySwitch.Classes.Settings;
using HubDisplaySwitch.Localization;
using HubDisplaySwitch.Models;
using HubDisplaySwitch.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace HubDisplaySwitch.Pages;

/// <summary>Editor for one setup. Every change is saved immediately and the engine re-evaluates hubs.</summary>
public sealed partial class SetupPage : Page
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public string? SetupId { get; private set; }
    private Setup _setup = new();
    private bool _initializing;

    public SetupPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        SetupId = e.Parameter as string;
        var setup = SettingsManager.Current.Setups.FirstOrDefault(s => s.Id == SetupId);
        if (setup == null) return;
        _setup = setup;
        Load();
    }

    private void Load()
    {
        _initializing = true;
        try
        {
            GlyphIcon.Glyph = _setup.Glyph;
            GlyphGrid.ItemsSource = Glyphs.SetupChoices;
            NameBox.Text = _setup.Name;
            DefaultRadio.IsChecked = _setup.IsDefault;
            HubRadio.IsChecked = !_setup.IsDefault;
            HubSection.Visibility = _setup.IsDefault ? Visibility.Collapsed : Visibility.Visible;
            BuildHubs();
            BuildDisplays();
            MirrorSwitch.IsOn = _setup.Mirror;
            BuildAudio();
        }
        finally { _initializing = false; }
    }

    private void Save(bool navChanged = false)
    {
        if (_initializing) return;
        SettingsManager.SaveSettings();
        MainWindow.Engine.SettingsChanged();
        if (navChanged) SettingsWindow.GetCurrent()?.UpdateSetupItem(_setup);
    }

    // ── Name / icon / delete ─────────────────────────────────────────────
    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initializing) return;
        _setup.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? UiText.Get("Setup") : NameBox.Text.Trim();
        Save(navChanged: true);
    }

    private void GlyphGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not string glyph) return;
        _setup.Glyph = glyph;
        GlyphIcon.Glyph = glyph;
        GlyphFlyout.Hide();
        Save(navChanged: true);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = UiText.Format("Delete \"{{name}}\"?", ("name", _setup.Name)),
            Content = UiText.Get("This removes the setup. Your displays and audio aren't changed."),
            PrimaryButtonText = UiText.Get("Delete"),
            CloseButtonText = UiText.Get("Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var setups = SettingsManager.Current.Setups;
        setups.Remove(_setup);
        if (_setup.IsDefault && setups.Count > 0 && !setups.Any(s => s.IsDefault)) setups[0].IsDefault = true;
        SettingsManager.SaveSettings();
        MainWindow.Engine.SettingsChanged();
        Logger.Info("Deleted setup {Name}", _setup.Name);
        SettingsWindow.GetCurrent()?.RebuildSetupItems(setups.FirstOrDefault()?.Id);
    }

    // ── Trigger / hubs ───────────────────────────────────────────────────
    private void Trigger_Checked(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        bool isDefault = DefaultRadio.IsChecked == true;
        HubSection.Visibility = isDefault ? Visibility.Collapsed : Visibility.Visible;
        if (isDefault == _setup.IsDefault) return;
        if (isDefault)
            foreach (var other in SettingsManager.Current.Setups) other.IsDefault = false;
        _setup.IsDefault = isDefault;
        Save();
    }

    private void BuildHubs()
    {
        HubsPanel.Children.Clear();
        List<UsbDevice> present;
        try { present = UsbDevices.List(); } catch { present = new(); }

        foreach (var hub in _setup.Hubs.ToList())
        {
            bool connected = UsbDevices.IsPresent(present, hub.DeviceId, hub.ExactPort);
            var model = UsbDevices.ModelPrefix(hub.DeviceId).Replace("USB\\", "").Replace("VID_", "VID ").Replace("&PID_", " · PID ");

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Text = hub.Name, TextTrimming = TextTrimming.CharacterEllipsis });
            info.Children.Add(new TextBlock
            {
                Text = $"{UiText.Get(connected ? "Connected" : "Not connected")}  ·  {model}",
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources[connected ? "SystemFillColorSuccessBrush" : "TextFillColorSecondaryBrush"],
            });

            var exact = new CheckBox { Content = UiText.Get("This port only"), IsChecked = hub.ExactPort, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(exact, UiText.Get("Only match when plugged into this exact USB port. Needed if two setups use the same hub model."));
            exact.Click += (_, _) => { hub.ExactPort = exact.IsChecked == true; Save(); BuildHubs(); };

            var remove = new Button { Content = new FontIcon { Glyph = "", FontSize = 12 }, Padding = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(remove, UiText.Get("Remove"));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, UiText.Get("Remove"));
            remove.Click += (_, _) => { _setup.Hubs.Remove(hub); Save(); BuildHubs(); };

            HubsPanel.Children.Add(Row(Glyphs.Usb, info, exact, remove));
        }
        if (_setup.Hubs.Count == 0)
            HubsPanel.Children.Add(new TextBlock
            {
                Text = UiText.Get("No hubs yet. Add one from the list, or detect it by unplugging it."),
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
    }

    /// <summary>A rounded row: icon, stretching content, then trailing controls.</summary>
    private static Border Row(string glyph, FrameworkElement content, params FrameworkElement[] trailing)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = new FontIcon { Glyph = glyph, FontSize = 18, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(icon);
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        for (int i = 0; i < trailing.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(trailing[i], 2 + i);
            grid.Children.Add(trailing[i]);
        }
        return new Border
        {
            Child = grid,
            Padding = new Thickness(12, 8, 8, 8),
            CornerRadius = new CornerRadius(6),
            Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
        };
    }

    private void AddHubMenu_Opening(object sender, object e)
    {
        AddHubMenu.Items.Clear();
        List<UsbDevice> devices;
        try { devices = UsbDevices.List(); } catch { devices = new(); }
        var hubs = devices.Where(d => d.IsHub).OrderBy(d => d.Name).ToList();
        var others = devices.Where(d => !d.IsHub).OrderBy(d => d.Name).ToList();

        foreach (var d in hubs) AddHubMenu.Items.Add(HubMenuItem(d));
        if (hubs.Count == 0) AddHubMenu.Items.Add(new MenuFlyoutItem { Text = UiText.Get("No USB hubs found"), IsEnabled = false });
        if (others.Count > 0)
        {
            var sub = new MenuFlyoutSubItem { Text = UiText.Get("Other USB devices") };
            foreach (var d in others) sub.Items.Add(HubMenuItem(d));
            AddHubMenu.Items.Add(new MenuFlyoutSeparator());
            AddHubMenu.Items.Add(sub);
        }
    }

    private MenuFlyoutItem HubMenuItem(UsbDevice d)
    {
        var item = new MenuFlyoutItem { Text = d.ToString() };
        item.Click += (_, _) => AddHub(d);
        return item;
    }

    private void AddHub(UsbDevice d)
    {
        if (_setup.Hubs.Any(h => h.DeviceId.Equals(d.DeviceId, StringComparison.OrdinalIgnoreCase))) return;
        // If another setup already uses this hub model, match by exact port so they can be told apart.
        bool sameModelElsewhere = SettingsManager.Current.Setups.SelectMany(s => s.Hubs)
            .Any(h => UsbDevices.ModelPrefix(h.DeviceId).Equals(d.ModelPrefix, StringComparison.OrdinalIgnoreCase));
        _setup.Hubs.Add(new HubRef { DeviceId = d.DeviceId, Name = d.Name, ExactPort = sameModelElsewhere });
        Save();
        BuildHubs();
    }

    private async void DetectHub_Click(object sender, RoutedEventArgs e)
    {
        var device = await DetectHubDialog.ShowAsync(XamlRoot);
        if (device != null) AddHub(device);
    }

    // ── Displays ─────────────────────────────────────────────────────────
    private void BuildDisplays()
    {
        DisplaysPanel.Children.Clear();
        List<MonitorInfo> monitors;
        try { monitors = DisplayConfig.GetMonitors(); } catch { monitors = new(); }

        // Every display Windows can see now, plus any this setup uses that isn't connected right now.
        var known = monitors.Select(m => new DisplayRef { Id = m.Id, Name = m.Name }).ToList();
        foreach (var d in _setup.Displays)
            if (!known.Any(k => Same(k, d))) known.Add(d);

        var main = _setup.Main;
        string group = "main-" + _setup.Id;
        foreach (var d in known)
        {
            var mon = monitors.FirstOrDefault(m => m.Id == d.Id) ?? monitors.FirstOrDefault(m => m.Name == d.Name);
            bool selected = _setup.Displays.Any(x => Same(x, d));
            string status = UiText.Get(mon == null ? "Not connected or switched off" : mon.Active ? "On" : "Connected, currently off");
            if (mon?.Active == true)
            {
                try { int hz = DisplayConfig.GetCurrentRefreshRate(mon.Id, mon.Name); if (hz > 0) status += $"  ·  {hz} Hz"; } catch { }
            }

            var info = new StackPanel();
            info.Children.Add(new TextBlock { Text = d.Name });
            info.Children.Add(new TextBlock
            {
                Text = status, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
            var check = new CheckBox { Content = info, IsChecked = selected, VerticalAlignment = VerticalAlignment.Center };
            var mainRadio = new RadioButton
            {
                Content = UiText.Get("Main display"), GroupName = group, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center,
                IsChecked = selected && main != null && Same(main, d),
            };

            var display = d;
            check.Click += (_, _) => ToggleDisplay(display, check.IsChecked == true);
            mainRadio.Checked += (_, _) =>
            {
                if (_initializing) return;
                if (!_setup.Displays.Any(x => Same(x, display))) _setup.Displays.Add(display);
                _setup.MainDisplayId = display.Id.Length > 0 ? display.Id : display.Name;
                Save(); Rebuild();
            };

            var grid = new Grid { ColumnSpacing = 16 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(check);
            if (_setup.Displays.FirstOrDefault(x => Same(x, d)) is { } own)
            {
                var refresh = RefreshPicker(own, mon);
                Grid.SetColumn(refresh, 1);
                grid.Children.Add(refresh);
            }
            Grid.SetColumn(mainRadio, 2);
            grid.Children.Add(mainRadio);
            DisplaysPanel.Children.Add(new Border
            {
                Child = grid, Padding = new Thickness(12, 6, 12, 6), CornerRadius = new CornerRadius(6),
                Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
            });
        }
    }

    /// <summary>Editable picker: highest (default) / don't change / the display's rates (when it's on) / a typed number.</summary>
    private ComboBox RefreshPicker(DisplayRef display, MonitorInfo? mon)
    {
        string HighestRate = UiText.Get("Highest refresh rate"), KeepRate = UiText.Get("Don't change refresh rate");
        var combo = new ComboBox { IsEditable = true, Width = 230, VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(combo, UiText.Get("Refresh rate to use in this setup. Re-applied for about 45 seconds after switching, because TVs waking from standby often fall back to 60 Hz."));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(combo, UiText.Get("Refresh rate"));
        combo.Items.Add(HighestRate);
        combo.Items.Add(KeepRate);
        if (mon?.Active == true)
        {
            try { foreach (var r in DisplayConfig.GetRefreshRates(mon.Id, mon.Name)) combo.Items.Add($"{r} Hz"); } catch { }
        }
        // Select an item rather than setting Text: an editable ComboBox shows blank for Text set before it's loaded.
        string current = display.RefreshRate == 0 ? HighestRate : display.RefreshRate < 0 ? KeepRate : $"{display.RefreshRate} Hz";
        if (!combo.Items.Contains(current)) combo.Items.Add(current);
        combo.SelectedItem = current;

        void Commit(string text)
        {
            if (_initializing) return;
            text = text.Trim();
            display.RefreshRate =
                text.Equals(HighestRate, StringComparison.CurrentCultureIgnoreCase) ? 0 :
                text.Equals(KeepRate, StringComparison.CurrentCultureIgnoreCase) ? -1 :
                int.TryParse(new string(text.TakeWhile(char.IsDigit).ToArray()), out var hz) && hz > 0 ? hz : 0;
            Save();
        }
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string s) Commit(s); };
        combo.TextSubmitted += (_, args) => Commit(args.Text);
        return combo;
    }

    private void ToggleDisplay(DisplayRef d, bool on)
    {
        var existing = _setup.Displays.FirstOrDefault(x => Same(x, d));
        if (on && existing == null) _setup.Displays.Add(d);
        if (!on && existing != null) _setup.Displays.Remove(existing);
        // Keep exactly one main display among the selected ones.
        if (_setup.Displays.Count > 0 && !_setup.Displays.Any(x => x.Id == _setup.MainDisplayId || x.Name == _setup.MainDisplayId))
            _setup.MainDisplayId = _setup.Displays[0].Id.Length > 0 ? _setup.Displays[0].Id : _setup.Displays[0].Name;
        Save();
        Rebuild();
    }

    private void Rebuild()
    {
        _initializing = true;
        try { BuildDisplays(); } finally { _initializing = false; }
    }

    private static bool Same(DisplayRef a, DisplayRef b) =>
        (a.Id.Length > 0 && a.Id.Equals(b.Id, StringComparison.OrdinalIgnoreCase)) || a.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase);

    private void MirrorSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _setup.Mirror = MirrorSwitch.IsOn;
        Save();
    }

    // ── Audio ────────────────────────────────────────────────────────────
    private void BuildAudio()
    {
        _outputs = FillAudioCombo(OutputCombo, AudioFlow.Output, _setup.AudioId, _setup.AudioName);
        _inputs = FillAudioCombo(InputCombo, AudioFlow.Input, _setup.MicId, _setup.MicName);
    }

    /// <summary>"Don't change", then a Connected group, then a Not connected group (unplugged devices).
    /// A saved device that Windows no longer lists is kept at the end so the setting isn't lost.</summary>
    private static List<AudioDevice> FillAudioCombo(ComboBox combo, AudioFlow flow, string id, string name)
    {
        List<AudioDevice> devices;
        try { devices = AudioDevices.List(flow); } catch (Exception ex) { Logger.Warn(ex, "{Flow} list failed", flow); devices = new(); }
        combo.Items.Clear();
        combo.Items.Add(new ComboBoxItem { Content = UiText.Get("Don't change"), Tag = "" });
        ComboBoxItem? selected = null;

        void Group(string header, IEnumerable<AudioDevice> items)
        {
            var list = items.ToList();
            if (list.Count == 0) return;
            combo.Items.Add(new ComboBoxItem
            {
                IsEnabled = false, IsHitTestVisible = false, Padding = new Thickness(11, 10, 11, 2),
                Content = new TextBlock
                {
                    Text = UiText.Get(header), Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                },
            });
            foreach (var a in list)
            {
                var item = new ComboBoxItem { Content = a.Name, Tag = a.Id };
                combo.Items.Add(item);
                if (name.Length > 0 && selected == null && (a.Id == id || a.Name == name)) selected = item;
            }
        }
        Group("Connected", devices.Where(d => d.Active));
        Group("Not connected", devices.Where(d => !d.Active));

        if (name.Length > 0 && selected == null)
        {
            selected = new ComboBoxItem { Content = name, Tag = id };
            combo.Items.Add(new ComboBoxItem
            {
                IsEnabled = false, IsHitTestVisible = false, Padding = new Thickness(11, 10, 11, 2),
                Content = new TextBlock
                {
                    Text = UiText.Get("Saved, not available now"), Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                },
            });
            combo.Items.Add(selected);
        }
        combo.SelectedItem = selected ?? combo.Items[0];
        return devices;
    }

    private List<AudioDevice> _outputs = new(), _inputs = new();

    private void OutputCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || OutputCombo.SelectedItem is not ComboBoxItem item) return;
        (_setup.AudioId, _setup.AudioName) = Pick(item, _outputs, _setup.AudioId, _setup.AudioName);
        Save();
    }

    private void InputCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || InputCombo.SelectedItem is not ComboBoxItem item) return;
        (_setup.MicId, _setup.MicName) = Pick(item, _inputs, _setup.MicId, _setup.MicName);
        Save();
    }

    private static (string id, string name) Pick(ComboBoxItem item, List<AudioDevice> devices, string oldId, string oldName)
    {
        string id = item.Tag as string ?? "";
        if (id.Length == 0) return ("", "");
        var device = devices.FirstOrDefault(a => a.Id == id);
        return device != null ? (device.Id, device.Name) : (oldId, oldName);   // "(not found)" entry keeps the saved one
    }

    private void SwitchNow_Click(object sender, RoutedEventArgs e) => MainWindow.Engine.Apply(_setup, SwitchReason.Manual);
}
