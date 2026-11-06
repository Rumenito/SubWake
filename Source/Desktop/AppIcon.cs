using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SubWake;

/// <summary>
/// Tray icons: full colour when active, a green dot while music holds the bursts, dimmed grey when off or
/// paused, and dimmed with an amber dot when a burst couldn't play.
/// </summary>
static class AppIcon
{
    public static readonly Icon Window = Load(32);
    public static readonly Icon Active = Load(SystemInformation.SmallIconSize.Width);
    public static readonly Icon Inactive = Make(Active, dim: true, dot: null);
    public static readonly Icon Holding = Make(Active, dim: false, dot: Color.FromArgb(167, 214, 167));
    public static readonly Icon Warning = Make(Active, dim: true, dot: Color.FromArgb(232, 184, 96));

    static Icon Load(int size)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "SubWake.ico");
        try { return File.Exists(path) ? new Icon(path, size, size) : SystemIcons.Application; }
        catch { return SystemIcons.Application; }
    }

    static Icon Make(Icon source, bool dim, Color? dot)
    {
        using var original = source.ToBitmap();
        var bitmap = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var target = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            if (dim)
            {
                var greyAndFaded = new ColorMatrix([
                    [0.3f, 0.3f, 0.3f, 0, 0],
                    [0.59f, 0.59f, 0.59f, 0, 0],
                    [0.11f, 0.11f, 0.11f, 0, 0],
                    [0, 0, 0, 0.45f, 0],
                    [0, 0, 0, 0, 1],
                ]);
                using var attributes = new ImageAttributes();
                attributes.SetColorMatrix(greyAndFaded);
                graphics.DrawImage(original, target, 0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
            }
            else graphics.DrawImage(original, target);

            if (dot is Color color)
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float size = bitmap.Width * 0.46f, x = bitmap.Width - size - 0.5f, y = bitmap.Height - size - 0.5f;
                using var ring = new SolidBrush(Color.FromArgb(13, 14, 16));
                using var fill = new SolidBrush(color);
                graphics.FillEllipse(ring, x - 1.2f, y - 1.2f, size + 2.4f, size + 2.4f);
                graphics.FillEllipse(fill, x, y, size, size);
            }
        }
        return Icon.FromHandle(bitmap.GetHicon());   // Created once for the app's lifetime.
    }
}
