using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace HubDisplaySwitch.Classes;

/// <summary>
/// Renders the tray icon (a monitor) in code: frame follows the taskbar theme, screen is blue when
/// auto-switching is on and grey when paused. Rendered at exactly the pixel size the tray uses at the
/// current scaling (Windows would otherwise pick the nearest size and stretch it, which looks blurry),
/// packed as a PNG-compressed ICO in memory (never Icon.Save, which drops the PNG data).
/// </summary>
internal static class TrayIconRenderer
{
    public static Icon Create(bool paused, bool darkTaskbar, int size)
    {
        size = Math.Clamp(size, 16, 256);
        var frame = darkTaskbar ? Color.White : Color.FromArgb(32, 32, 32);
        var screen = paused ? Color.FromArgb(128, 128, 128) : Color.FromArgb(59, 130, 246);

        var blob = Render(size, frame, screen);
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((ushort)0); bw.Write((ushort)1); bw.Write((ushort)1);
        bw.Write((byte)(size >= 256 ? 0 : size)); bw.Write((byte)(size >= 256 ? 0 : size)); bw.Write((byte)0); bw.Write((byte)0);
        bw.Write((ushort)1); bw.Write((ushort)32);
        bw.Write((uint)blob.Length); bw.Write((uint)22);
        bw.Write(blob);
        bw.Flush();
        ms.Position = 0;
        return new Icon(ms, size, size);
    }

    static byte[] Render(int size, Color frame, Color screen)
    {
        // Supersample 4x then shrink once, so small sizes stay crisp.
        int m = size * 4;
        using var big = new Bitmap(m, m, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(big))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float w = m, h = m;
            var body = new RectangleF(w * 0.04f, h * 0.14f, w * 0.92f, h * 0.60f);
            using (var b = new SolidBrush(frame)) g.FillPath(b, Rounded(body, w * 0.08f));
            var inner = RectangleF.Inflate(body, -w * 0.075f, -h * 0.075f);
            using (var b = new SolidBrush(screen)) g.FillPath(b, Rounded(inner, w * 0.03f));
            using var stand = new SolidBrush(frame);
            g.FillRectangle(stand, w * 0.43f, body.Bottom - 1, w * 0.14f, h * 0.12f);
            g.FillPath(stand, Rounded(new RectangleF(w * 0.24f, body.Bottom + h * 0.10f, w * 0.52f, h * 0.09f), h * 0.045f));
        }
        using var small = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawImage(big, 0, 0, size, size);
        }
        using var png = new MemoryStream();
        small.Save(png, ImageFormat.Png);
        return png.ToArray();
    }

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Max(0.1f, radius * 2);
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
