using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace Sacred.Engine.Rendering;

internal sealed class LoadingScreenRasterizer : IDisposable
{
    private const int BottomMargin = 20;
    private readonly RgbaImage _background;
    private readonly RgbaImage _emptyBar;
    private readonly RgbaImage _redBar;
    private readonly DebugOverlayFontSet _fonts;
    private ulong _revision;

    public LoadingScreenRasterizer(string backgroundPath, string gameDirectory)
    {
        _background = RgbaImageDecoder.LoadBitmap(backgroundPath);
        _emptyBar = LoadEmbeddedTga("ui_bar_empty.tga");
        _redBar = LoadEmbeddedTga("ui_bar_red.tga");
        if (_emptyBar.Width != _redBar.Width || _emptyBar.Height != _redBar.Height)
            throw new InvalidDataException("The embedded loading-bar textures have different dimensions.");

        _fonts = DebugOverlayFontSet.Load(gameDirectory);
    }

    public ScreenFrame Rasterize(double progress, string itemName)
    {
        var rgba = (byte[])_background.Rgba.Clone();
        var barX = Math.Max(0, (_background.Width - _emptyBar.Width) / 2);
        var barY = Math.Max(0, _background.Height - _emptyBar.Height - BottomMargin);
        Blend(rgba, _background.Width, _background.Height, _emptyBar, barX, barY, _emptyBar.Width);
        var progressWidth = (int)Math.Round(Math.Clamp(progress, 0.0, 1.0) * _redBar.Width);
        Blend(rgba, _background.Width, _background.Height, _redBar, barX, barY, progressWidth);
        DrawCenteredText(rgba, itemName, barX, barY, _emptyBar.Width, _emptyBar.Height);
        return new ScreenFrame(_background.Width, _background.Height, rgba, ++_revision);
    }

    private void DrawCenteredText(byte[] rgba, string text, int x, int y, int width, int height)
    {
        using var bitmap = new Bitmap(_background.Width, _background.Height, PixelFormat.Format32bppArgb);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.PageUnit = GraphicsUnit.Pixel;
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.EllipsisCharacter
        };
        var bounds = new RectangleF(x + 8, y, Math.Max(1, width - 16), height);
        var font = _fonts.GetFont(DebugTextFont.Default);
        using var shadow = new SolidBrush(Color.FromArgb(220, 0, 0, 0));
        using var foreground = new SolidBrush(Color.FromArgb(255, 242, 224, 192));
        var shadowBounds = bounds;
        shadowBounds.Offset(1, 1);
        graphics.DrawString(text, font, shadow, shadowBounds, format);
        graphics.DrawString(text, font, foreground, bounds, format);
        BlendBitmap(rgba, bitmap);
    }

    private static void BlendBitmap(byte[] destination, Bitmap bitmap)
    {
        var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var pixels = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            for (var y = 0; y < bitmap.Height; y++)
            {
                var sourceRow = data.Stride >= 0 ? y * stride : (bitmap.Height - 1 - y) * stride;
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var sourceOffset = sourceRow + x * 4;
                    var alpha = pixels[sourceOffset + 3];
                    if (alpha == 0)
                        continue;

                    BlendPixel(
                        destination,
                        (y * bitmap.Width + x) * 4,
                        pixels[sourceOffset + 2],
                        pixels[sourceOffset + 1],
                        pixels[sourceOffset],
                        alpha);
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static void Blend(
        byte[] destination,
        int destinationWidth,
        int destinationHeight,
        RgbaImage source,
        int destinationX,
        int destinationY,
        int sourceWidth)
    {
        sourceWidth = Math.Clamp(sourceWidth, 0, source.Width);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < sourceWidth; x++)
        {
            var targetX = destinationX + x;
            var targetY = destinationY + y;
            if ((uint)targetX >= destinationWidth || (uint)targetY >= destinationHeight)
                continue;

            var sourceOffset = (y * source.Width + x) * 4;
            BlendPixel(
                destination,
                (targetY * destinationWidth + targetX) * 4,
                source.Rgba[sourceOffset],
                source.Rgba[sourceOffset + 1],
                source.Rgba[sourceOffset + 2],
                source.Rgba[sourceOffset + 3]);
        }
    }

    private static void BlendPixel(byte[] destination, int offset, byte r, byte g, byte b, byte alpha)
    {
        if (alpha == 255)
        {
            destination[offset] = r;
            destination[offset + 1] = g;
            destination[offset + 2] = b;
            destination[offset + 3] = 255;
            return;
        }

        if (alpha == 0)
            return;

        var inverse = 255 - alpha;
        destination[offset] = (byte)((r * alpha + destination[offset] * inverse) / 255);
        destination[offset + 1] = (byte)((g * alpha + destination[offset + 1] * inverse) / 255);
        destination[offset + 2] = (byte)((b * alpha + destination[offset + 2] * inverse) / 255);
        destination[offset + 3] = 255;
    }

    private static RgbaImage LoadEmbeddedTga(string fileName)
    {
        var assembly = typeof(LoadingScreenRasterizer).Assembly;
        var suffix = ".Embeds." + fileName;
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Could not open embedded resource '{resourceName}'.");
            return RgbaImageDecoder.LoadTga(stream, resourceName);
        }

        throw new FileNotFoundException($"Embedded loading-bar texture '{fileName}' was not found.");
    }

    public void Dispose() => _fonts.Dispose();
}
