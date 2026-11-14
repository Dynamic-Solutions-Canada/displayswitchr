using HubDisplaySwitch.Classes;
using HubDisplaySwitch.Classes.Settings;
using HubDisplaySwitch.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using static HubDisplaySwitch.Classes.NativeMethods;

namespace HubDisplaySwitch.Windows;

/// <summary>
/// Volume/brightness-style popup at the bottom centre of the main display. While Working it shows a
/// spinner and stays up; Done/Error hold for a moment, then fade (layered alpha) and park off-screen.
/// Never activates, is click-through, and is re-centred on WM_DISPLAYCHANGE so it follows the desktop
/// onto whichever display comes up.
/// </summary>
public sealed partial class OsdWindow : Window
{
    private const double WidthDip = 340, MinWidthDip = 320, MaxWidthDip = 640, HeightDip = 68;
    private static readonly TimeSpan DoneHold = TimeSpan.FromSeconds(2.5), MaxWorking = TimeSpan.FromSeconds(90);
    private const int FadeSteps = 12;

    private readonly IntPtr _hwnd;
    private readonly DispatcherQueueTimer _hold, _fade;
    private int _fadeStep;
    private bool _visible;

    public OsdWindow()
    {
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        var appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd));
        appWindow.SetPresenter(presenter);
        appWindow.IsShownInSwitchers = false;

        SystemBackdrop = new DesktopAcrylicBackdrop();
        int corner = DWMWCP_ROUND;
        DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        var margins = new MARGINS { Left = 1, Right = 1, Top = 1, Bottom = 1 };
        DwmExtendFrameIntoClientArea(_hwnd, ref margins);
        AddExStyle(_hwnd, WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        SetLayeredWindowAttributes(_hwnd, 0, 255, LWA_ALPHA);

        WindowRoot.RequestedTheme = ThemeManager.Requested(SettingsManager.Current.AppTheme);
        ThemeManager.ThemeChanged += () => WindowRoot.RequestedTheme = ThemeManager.Requested(SettingsManager.Current.AppTheme);

        _hold = DispatcherQueue.CreateTimer();
        _hold.IsRepeating = false;
        _hold.Tick += (_, _) => StartFade();
        _fade = DispatcherQueue.CreateTimer();
        _fade.Interval = TimeSpan.FromMilliseconds(30);
        _fade.Tick += (_, _) => FadeTick();

        // Warm up cloaked and parked off-screen so the first popup is composed instantly.
        Cloak(_hwnd, true);
        appWindow.Show(false);
        Park();
    }

    public void Present(string title, string subtitle, OsdState state)
    {
        TitleText.Text = title;
        SubText.Text = subtitle;

        bool working = state == OsdState.Working;
        Spinner.IsActive = working;
        Spinner.Visibility = working ? Visibility.Visible : Visibility.Collapsed;
        StateIcon.Visibility = working ? Visibility.Collapsed : Visibility.Visible;
        StateIcon.Glyph = state == OsdState.Error ? "" : "";
        StateIcon.Foreground = (Brush)Application.Current.Resources[
            state == OsdState.Error ? "SystemFillColorCriticalBrush" : "AccentTextFillColorPrimaryBrush"];

        _fade.Stop();
        SetLayeredWindowAttributes(_hwnd, 0, 255, LWA_ALPHA);
        _visible = true;
        Reposition();

        _hold.Stop();
        _hold.Interval = working ? MaxWorking : DoneHold;
        _hold.Start();
    }

    /// <summary>Bottom centre of the main display, above everything, without activating.</summary>
    public void Reposition()
    {
        // Hidden: re-park (a display switch makes Windows pull off-screen windows back into view).
        if (!_visible) { Park(); return; }
        var (bounds, _, scale) = PrimaryMonitorArea();
        // Size to the text (single lines), within limits; longer text is trimmed with an ellipsis.
        WindowRoot.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, HeightDip));
        double widthDip = Math.Clamp(WindowRoot.DesiredSize.Width + 8, MinWidthDip, MaxWidthDip);
        int w = (int)Math.Ceiling(widthDip * scale), h = (int)Math.Ceiling(HeightDip * scale);
        int x = bounds.Left + (bounds.Right - bounds.Left - w) / 2;
        int y = bounds.Bottom - h - (int)(64 * scale);
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        Cloak(_hwnd, false);
    }

    private void StartFade()
    {
        _fadeStep = 0;
        _fade.Start();
    }

    private void FadeTick()
    {
        _fadeStep++;
        byte alpha = (byte)Math.Max(0, 255 - 255 * _fadeStep / FadeSteps);
        SetLayeredWindowAttributes(_hwnd, 0, alpha, LWA_ALPHA);
        if (_fadeStep < FadeSteps) return;
        _fade.Stop();
        _visible = false;
        Spinner.IsActive = false;
        Park();
        SetLayeredWindowAttributes(_hwnd, 0, 255, LWA_ALPHA);
    }

    private void Park()
    {
        Cloak(_hwnd, true);
        var (x, y) = ParkingSpot((int)WidthDip, (int)HeightDip);
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, (int)WidthDip, (int)HeightDip, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }
}
