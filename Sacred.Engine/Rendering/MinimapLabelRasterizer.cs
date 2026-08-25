using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Sacred.Engine.Rendering;

/// <summary>Creates the transparent difficulty and region labels drawn over the minimap.</summary>
internal sealed class MinimapLabelRasterizer : IDisposable
{
    public const int Width = 320;
    public const int Height = 72;

    private readonly DebugOverlayFontSet _fonts;
    private byte[] _bitmapPixels = new byte[Width * Height * 4];

    public MinimapLabelRasterizer(string gameDirectory) =>
        _fonts = DebugOverlayFontSet.Load(gameDirectory);

    public byte[] Rasterize(string difficultyDisplayName, string regionDisplayName)
    {
        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.PageUnit = GraphicsUnit.Pixel;

        var font = _fonts.GetFont(DebugTextFont.CarolingTitle);
        using var shadow = new SolidBrush(Color.FromArgb(220, 0, 0, 0));
        using var foreground = new SolidBrush(Color.FromArgb(255, 220, 215, 135));
        DrawLabel(graphics, difficultyDisplayName, font, shadow, foreground, 0.0f);
        DrawLabel(graphics, regionDisplayName, font, shadow, foreground, 24.0f);

        return ToRgba(bitmap);
    }

    private static void DrawLabel(
        System.Drawing.Graphics graphics,
        string text,
        Font font,
        Brush shadow,
        Brush foreground,
        float y)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        graphics.DrawString(text, font, shadow, 2.0f, y + 2.0f);
        graphics.DrawString(text, font, foreground, 0.0f, y);
    }

    private byte[] ToRgba(Bitmap bitmap)
    {
        var data = bitmap.LockBits(
            new Rectangle(0, 0, Width, Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var requiredLength = stride * Height;
            if (_bitmapPixels.Length < requiredLength)
                Array.Resize(ref _bitmapPixels, requiredLength);
            Marshal.Copy(data.Scan0, _bitmapPixels, 0, requiredLength);

            var rgba = new byte[Width * Height * 4];
            for (var y = 0; y < Height; y++)
            {
                var sourceRow = data.Stride >= 0 ? y * stride : (Height - 1 - y) * stride;
                var destinationRow = y * Width * 4;
                for (var x = 0; x < Width; x++)
                {
                    var source = sourceRow + x * 4;
                    var destination = destinationRow + x * 4;
                    rgba[destination] = _bitmapPixels[source + 2];
                    rgba[destination + 1] = _bitmapPixels[source + 1];
                    rgba[destination + 2] = _bitmapPixels[source];
                    rgba[destination + 3] = _bitmapPixels[source + 3];
                }
            }

            return rgba;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    public void Dispose() => _fonts.Dispose();
}
