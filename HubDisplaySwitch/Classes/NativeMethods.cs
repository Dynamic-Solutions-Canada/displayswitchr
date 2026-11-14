using System.Runtime.InteropServices;

namespace HubDisplaySwitch.Classes;

/// <summary>
/// All UI-side P/Invoke (windows, tray icon, monitors, DWM). Consumed via
/// <c>using static HubDisplaySwitch.Classes.NativeMethods;</c>. Display/audio/USB interop lives with
/// its service in <c>Services/</c>.
/// </summary>
internal static class NativeMethods
{
    // ── Structs ─────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MARGINS { public int Left, Right, Top, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFYICONIDENTIFIER
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    public delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData);

    // ── Constants ───────────────────────────────────────────────────────
    public const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_GUID = 0x20;
    public const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    public const int WM_LBUTTONUP = 0x202, WM_RBUTTONUP = 0x205, WM_CONTEXTMENU = 0x7B;
    public const uint WM_KEYDOWN = 0x100, WM_SETICON = 0x80, WM_DISPLAYCHANGE = 0x7E;
    public const int VK_ESCAPE = 0x1B;
    public const IntPtr ICON_SMALL = 0, ICON_BIG = 1;

    public const uint MONITOR_DEFAULTTOPRIMARY = 1, MONITOR_DEFAULTTONEAREST = 2;
    public const int MDT_EFFECTIVE_DPI = 0;

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
                     WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
    public const uint LWA_ALPHA = 0x2;

    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10,
                      SWP_SHOWWINDOW = 0x40, SWP_ASYNCWINDOWPOS = 0x4000;
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    public const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77;

    public const int DWMWA_CLOAK = 13, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2, DWMWCP_ROUNDSMALL = 3;

    public const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10;

    // ── user32 ──────────────────────────────────────────────────────────
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessage(string lpString);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] public static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);
    public const int SM_CXICON = 11, SM_CXSMICON = 49;
    public const uint WM_DPICHANGED = 0x02E0, WM_SETTINGCHANGE = 0x001A;
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);

    // ── shcore / dwmapi / comctl32 / shell32 / kernel32 ─────────────────
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
    [DllImport("comctl32.dll")] public static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);
    [DllImport("comctl32.dll")] public static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);
    [DllImport("shell32.dll")] public static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterApplicationRestart(string? pwzCommandline, int dwFlags);

    // ── helpers ─────────────────────────────────────────────────────────
    public static void AddExStyle(IntPtr hwnd, int style) =>
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)((long)GetWindowLongPtr(hwnd, GWL_EXSTYLE) | (uint)style));

    public static void RemoveExStyle(IntPtr hwnd, int style) =>
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)((long)GetWindowLongPtr(hwnd, GWL_EXSTYLE) & ~(long)(uint)style));

    /// <summary>Monitor work/bounds rectangles and DPI scale for a monitor handle.</summary>
    public static (RECT bounds, RECT work, double scale) MonitorArea(IntPtr monitor)
    {
        var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        GetMonitorInfo(monitor, ref mi);
        double scale = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpi, out _) == 0 ? dpi / 96.0 : 1.0;
        return (mi.rcMonitor, mi.rcWork, scale);
    }

    /// <summary>The main display (the one containing 0,0).</summary>
    public static (RECT bounds, RECT work, double scale) PrimaryMonitorArea() =>
        MonitorArea(MonitorFromPoint(new POINT(), MONITOR_DEFAULTTOPRIMARY));

    /// <summary>
    /// DWM-cloak a window: invisible but still "shown", so WinUI keeps its composition surfaces. Parked
    /// windows are cloaked too, because a display switch shrinks the virtual screen and Windows then moves
    /// off-screen windows back into view.
    /// </summary>
    public static void Cloak(IntPtr hwnd, bool cloaked)
    {
        int value = cloaked ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref value, sizeof(int));
    }

    /// <summary>Off-screen position for "parked" windows (WinUI keeps composition surfaces when not hidden).</summary>
    public static (int x, int y) ParkingSpot(int width, int height) =>
        (GetSystemMetrics(SM_XVIRTUALSCREEN) - width - 64, GetSystemMetrics(SM_YVIRTUALSCREEN) - height - 64);
}
