using System.Drawing;
using WinUIAppKit.Tray;

namespace HubDisplaySwitch.Classes;

/// <summary>
/// The tray icon's artwork, a monitor: the frame follows the taskbar theme, and the screen is blue when
/// auto-switching is on and grey when paused. The kit's <see cref="TrayIconRasterizer"/> turns it into a
/// PNG at exactly the pixel size the tray uses at the current scaling, so Windows doesn't blur it.
/// </summary>
internal static class TrayIconRenderer
{
    private static readonly Color ActiveScreen = Color.FromArgb(59, 130, 246);
    private static readonly Color PausedScreen = Color.FromArgb(128, 128, 128);

    /// <summary>Renders the icon as PNG bytes.</summary>
    /// <param name="paused">Auto-switching is paused (grey screen).</param>
    /// <param name="darkTaskbar">From <see cref="TrayIcon.TaskbarIsDark"/>; picks the frame colour.</param>
    /// <param name="size">The icon's pixel size, normally <see cref="TrayIcon.IconPixelSize"/>.</param>
    public static byte[] Render(bool paused, bool darkTaskbar, int size)
    {
        var frame = TrayIconRasterizer.Ink(darkTaskbar);
        var screen = paused ? PausedScreen : ActiveScreen;
        return TrayIconRasterizer.Render(size, (g, side) => DrawMonitor(g, side, frame, screen));
    }

    /// <summary>Draws the monitor on a canvas <paramref name="side"/> pixels square.</summary>
    private static void DrawMonitor(Graphics g, float side, Color frame, Color screen)
    {
        // The bezel, then the screen inset inside it.
        var body = new RectangleF(side * 0.04f, side * 0.14f, side * 0.92f, side * 0.60f);
        using (var bezel = new SolidBrush(frame))
        using (var bezelPath = TrayIconRasterizer.RoundedRectangle(body, side * 0.08f))
        {
            g.FillPath(bezel, bezelPath);
        }

        var inner = RectangleF.Inflate(body, -side * 0.075f, -side * 0.075f);
        using (var display = new SolidBrush(screen))
        using (var displayPath = TrayIconRasterizer.RoundedRectangle(inner, side * 0.03f))
        {
            g.FillPath(display, displayPath);
        }

        // The neck and the foot of the stand.
        using var stand = new SolidBrush(frame);
        g.FillRectangle(stand, side * 0.43f, body.Bottom - 1, side * 0.14f, side * 0.12f);
        using var foot = TrayIconRasterizer.RoundedRectangle(new RectangleF(side * 0.24f, body.Bottom + side * 0.10f, side * 0.52f, side * 0.09f), side * 0.045f);
        g.FillPath(stand, foot);
    }
}
