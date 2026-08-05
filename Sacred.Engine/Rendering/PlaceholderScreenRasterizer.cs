using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Sacred.Engine.Rendering;

internal sealed class PlaceholderScreenRasterizer : IDisposable
{
    private const int Width = 1024;
    private const int Height = 768;
    private readonly DebugOverlayFontSet _fonts;
    private ulong _revision;

    public PlaceholderScreenRasterizer(string gameDirectory) =>
        _fonts = DebugOverlayFontSet.Load(gameDirectory);

    public ScreenFrame Rasterize(string title, string instructions)
    {
        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(255, 3, 8, 12));
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.PageUnit = GraphicsUnit.Pixel;
        using var centered = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        using var titleBrush = new SolidBrush(Color.FromArgb(255, 218, 189, 126));
        using var textBrush = new SolidBrush(Color.FromArgb(255, 226, 234, 238));
        graphics.DrawString(
            title,
            _fonts.GetFont(DebugTextFont.CarolingTitle),
            titleBrush,
            new RectangleF(0, Height * 0.35f, Width, 80),
            centered);
        graphics.DrawString(
            instructions,
            _fonts.GetFont(DebugTextFont.Default),
            textBrush,
            new RectangleF(0, Height * 0.52f, Width, 80),
            centered);

        var rgba = ToRgba(bitmap);
        return new ScreenFrame(Width, Height, rgba, ++_revision);
    }

    private static byte[] ToRgba(Bitmap bitmap)
    {
        var rgba = new byte[Width * Height * 4];
        var bounds = new Rectangle(0, 0, Width, Height);
        var data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var bgra = new byte[stride * Height];
            Marshal.Copy(data.Scan0, bgra, 0, bgra.Length);
            for (var y = 0; y < Height; y++)
            {
                var sourceRow = data.Stride >= 0 ? y * stride : (Height - 1 - y) * stride;
                for (var x = 0; x < Width; x++)
                {
                    var source = sourceRow + x * 4;
                    var destination = (y * Width + x) * 4;
                    rgba[destination] = bgra[source + 2];
                    rgba[destination + 1] = bgra[source + 1];
                    rgba[destination + 2] = bgra[source];
                    rgba[destination + 3] = bgra[source + 3];
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return rgba;
    }

    public void Dispose() => _fonts.Dispose();
}
