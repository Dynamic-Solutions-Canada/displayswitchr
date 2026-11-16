using HubDisplaySwitch.Classes;
using HubDisplaySwitch.Classes.Settings;
using HubDisplaySwitch.Localization;
using HubDisplaySwitch.Models;
using HubDisplaySwitch.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using static HubDisplaySwitch.Classes.NativeMethods;

namespace HubDisplaySwitch.Windows;

/// <summary>
/// Tray flyout: one tile per setup, click to switch. Same window pattern as Little Launcher's flyout:
/// context-menu presenter, desktop acrylic, rounded DWM corners + shadow, WS_EX_TOOLWINDOW, hidden by
/// parking off-screen (never SW_HIDE, so the next show is instant), dismissed on deactivate / Esc.
/// </summary>
public sealed partial class SwitcherWindow : Window
{
    private const double WidthDip = 380;

    private readonly IntPtr _hwnd;
    private readonly SUBCLASSPROC _wndProcDelegate;
    private bool _isOpen, _isShowing, _updatingToggle;
    private DateTime _dismissedAt = DateTime.MinValue;

    public SwitcherWindow()
    {
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        GetAppWindow().SetPresenter(presenter);
        GetAppWindow().IsShownInSwitchers = false;

        SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
        int corner = DWMWCP_ROUND;
        DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        var margins = new MARGINS { Left = 1, Right = 1, Top = 1, Bottom = 1 };
        DwmExtendFrameIntoClientArea(_hwnd, ref margins);
        AddExStyle(_hwnd, WS_EX_TOOLWINDOW);

        _wndProcDelegate = WndProc;
        SetWindowSubclass(_hwnd, _wndProcDelegate, 2, 0);
        Activated += (s, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated && !_isShowing) Dismiss();
        };
        ThemeManager.ThemeChanged += () => WindowRoot.RequestedTheme = ThemeManager.Requested(SettingsManager.Current.AppTheme);
        WindowRoot.RequestedTheme = ThemeManager.Requested(SettingsManager.Current.AppTheme);

        // Warm up: create the composition surfaces now, cloaked and parked off-screen.
        Refresh();
        Cloak(_hwnd, true);
        GetAppWindow().Show(false);
        Park();
    }

    private AppWindow GetAppWindow() =>
        AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd));

    // ── Show / hide ──────────────────────────────────────────────────────
    public void Toggle(int anchorX, int anchorY)
    {
        if (_isOpen) { Dismiss(); return; }
        // The tray click that deactivated (dismissed) us shouldn't immediately reopen.
        if ((DateTime.Now - _dismissedAt).TotalMilliseconds < 300) return;
        Show(anchorX, anchorY);
    }

    private void Show(int anchorX, int anchorY)
    {
        _isShowing = true;
        Refresh();

        var monitor = MonitorFromPoint(new POINT { X = anchorX, Y = anchorY }, MONITOR_DEFAULTTONEAREST);
        var (bounds, work, scale) = MonitorArea(monitor);
        WindowRoot.Measure(new global::Windows.Foundation.Size(WidthDip, double.PositiveInfinity));
        int w = (int)Math.Ceiling(WidthDip * scale);
        int h = (int)Math.Ceiling(WindowRoot.DesiredSize.Height * scale);
        var (left, top) = Placement(anchorX, anchorY, w, h, bounds, work, scale);

        // Move/resize while still cloaked, then reveal only once XAML has drawn at the new size.
        // Uncloaking straight away shows a frame of bare acrylic with the old layout in the corner.
        SetWindowPos(_hwnd, IntPtr.Zero, left, top, w, h, SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        _isOpen = true;
        RevealAfterRender();
    }

    private void RevealAfterRender()
    {
        int frames = 0;
        bool revealed = false;
        var fallback = DispatcherQueue.CreateTimer();

        void Reveal()
        {
            if (revealed) return;
            revealed = true;
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering;
            fallback.Stop();
            if (!_isOpen) { _isShowing = false; return; }   // dismissed before it appeared
            Cloak(_hwnd, false);
            SetForegroundWindow(_hwnd);
            SetFocus(_hwnd);
            DispatcherQueue.TryEnqueue(() => _isShowing = false);
        }

        // Two rendered frames: one for layout at the new size, one for it to reach the screen.
        void OnRendering(object? sender, object e) { if (++frames >= 2) Reveal(); }

        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnRendering;
        fallback.Interval = TimeSpan.FromMilliseconds(150);   // never leave it invisible
        fallback.IsRepeating = false;
        fallback.Tick += (_, _) => Reveal();
        fallback.Start();
    }

    public void Dismiss()
    {
        if (!_isOpen) return;
        _isOpen = false;
        _dismissedAt = DateTime.Now;
        Park();
    }

    /// <summary>Cloak and move off-screen. Also called after display changes (Windows pulls off-screen windows back).</summary>
    public void Park()
    {
        if (_isOpen) return;
        Cloak(_hwnd, true);
        // Keep the current (last shown) size so the next open doesn't have to re-layout from scratch.
        var size = GetAppWindow().Size;
        var (x, y) = ParkingSpot(Math.Max(size.Width, 1), Math.Max(size.Height, 1));
        SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSIZE | SWP_SHOWWINDOW);
    }

    /// <summary>Little Launcher's placement rules: beside a side taskbar, above a bottom one, below a top one.</summary>
    private static (int left, int top) Placement(int ax, int ay, int w, int h, RECT mon, RECT work, double scale)
    {
        int gap = (int)Math.Max(4, 8 * scale), edge = (int)(16 * scale);
        int left = ax - w / 2, top;
        bool taskbarLeft = work.Left > mon.Left && ax - work.Left < edge;
        bool taskbarRight = work.Right < mon.Right && work.Right - ax < edge;
        if (taskbarLeft) { left = work.Left + gap; top = ay - h / 2; }
        else if (taskbarRight) { left = work.Right - w - gap; top = ay - h / 2; }
        else if (work.Bottom - ay < edge * 3 || ay >= work.Bottom) top = work.Bottom - h - gap;
        else if (ay - work.Top < edge * 3 || ay <= work.Top) top = work.Top + gap;
        else top = ay - h - gap;   // e.g. tray overflow popup
        left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - w));
        top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - h));
        return (left, top);
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
            TilesPanel.Children.Add(BuildTile(engine, setup));

        StatusText.Text = engine == null ? ""
            : engine.IsSwitching ? UiText.Format("Switching to {{setup}}…", ("setup", engine.Active?.Name))
            : s.Paused ? UiText.Get("Auto-switching is paused")
            : engine.Active != null ? UiText.Format("Using {{setup}}", ("setup", engine.Active.Name)) : "";
    }

    private Button BuildTile(SwitchEngine? engine, Setup setup)
    {
        bool active = engine?.Active?.Id == setup.Id;
        var res = Application.Current.Resources;

        var circle = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(20),
            Background = (Brush)res[active ? "AccentFillColorDefaultBrush" : "SubtleFillColorSecondaryBrush"],
            Child = new FontIcon
            {
                Glyph = setup.Glyph, FontSize = 18,
                Foreground = (Brush)res[active ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorPrimaryBrush"],
            },
        };

        var detail = new List<string> { setup.DescribeDisplays() };
        if (setup.AudioName.Length > 0) detail.Add(SwitchEngine.ShortAudio(setup.AudioName));
        var text = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = setup.Name, Style = (Style)res["BodyStrongTextBlockStyle"], TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock
        {
            Text = string.Join("  ·  ", detail), Style = (Style)res["CaptionTextBlockStyle"],
            Foreground = (Brush)res["TextFillColorSecondaryBrush"], TextTrimming = TextTrimming.CharacterEllipsis,
        });

        string badge = UiText.Get(active ? "Active"
                     : setup.IsDefault ? "Default"
                     : engine != null && engine.IsHubConnected(setup) ? "Hub connected"
                     : setup.Hubs.Count > 0 ? "Hub not connected" : "");
        var badgeText = new TextBlock
        {
            Text = badge, Style = (Style)res["CaptionTextBlockStyle"], VerticalAlignment = VerticalAlignment.Center,
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
        if (active) button.Background = (Brush)res["SubtleFillColorSecondaryBrush"];
        button.Click += (_, _) =>
        {
            Dismiss();
            MainWindow.Engine.Apply(setup, SwitchReason.Manual);
        };
        return button;
    }

    private void AutoSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingToggle) return;
        MainWindow.Engine.SetPaused(!AutoSwitch.IsOn);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        Dismiss();
        SettingsWindow.ShowInstance();
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr refData)
    {
        if (msg == WM_KEYDOWN && (int)wParam == VK_ESCAPE)
        {
            DispatcherQueue.TryEnqueue(Dismiss);
            return IntPtr.Zero;
        }
        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }
}
