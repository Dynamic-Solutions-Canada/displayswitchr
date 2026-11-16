using System.Runtime.InteropServices;

namespace HubDisplaySwitch.Classes;

/// <summary>
/// Win32 declarations Hub needs that the app kit doesn't have (the click-through, fading popup).
/// Everything else comes from <c>WinUIAppKit.Interop.NativeMethods</c>; files import both with
/// <c>using static</c>, so never add a name the kit already declares. Display/audio/USB interop lives
/// with its service in <c>Services/</c>.
/// </summary>
internal static class NativeMethods
{
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint LWA_ALPHA = 0x2;

    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
}
