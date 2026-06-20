using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace Sacred.Engine.Rendering;

public sealed class DebugTextOverlay(DebugOverlayFontSet fonts)
{
    public const int Width = 660;
    public const int Height = 176;

    private const int Padding = 8;
    private const int DefaultLineAdvance = 16;
    private const int TitleLineAdvance = 31;

    private static readonly StringFormat TextFormat = new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormatFlags.NoClip | StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces,
        Trimming = StringTrimming.None
    };

    public byte[] Rgba { get; } = new byte[Width * Height * 4];
    private byte[] _bitmapPixels = new byte[Width * Height * 4];

    public void SetLines(string[] lines)
    {
        var styledLines = new DebugTextLine[lines.Length];
        for (var i = 0; i < lines.Length; i++)
            styledLines[i] = DebugTextLine.Default(lines[i]);

        SetLines(styledLines);
    }

    public void SetLines(ReadOnlySpan<DebugTextLine> lines)
    {
        Array.Clear(Rgba, 0, Rgba.Length);
        FillPanel();

        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.PageUnit = GraphicsUnit.Pixel;

        var y = Padding;
        foreach (var line in lines)
        {
            var font = fonts.GetFont(line.Font);
            DrawString(graphics, line.Text, font, Padding + 1, y + 1, 0, 0, 0, 190);
            DrawString(graphics, line.Text, font, Padding, y, 238, 246, 255, 245);

            y += line.Font == DebugTextFont.CarolingTitle ? TitleLineAdvance : DefaultLineAdvance;
            if (y + DebugTextOverlayFontSizes.Default > Height - Padding)
                break;
        }

        BlendTextBitmap(bitmap);
    }

    private void DrawString(System.Drawing.Graphics graphics, string text, Font font, int x, int y, byte r, byte g, byte b, byte a)
    {
        using var brush = new SolidBrush(Color.FromArgb(a, r, g, b));
        graphics.DrawString(
            text,
            font,
            brush,
            new RectangleF(x, y, Width - Padding - x, Height - Padding - y),
            TextFormat);
    }

    private void FillPanel()
    {
        FillRect(0, 0, Width, Height, 8, 14, 18, 155);
        FillRect(0, 0, Width, 1, 125, 168, 190, 120);
        FillRect(0, Height - 1, Width, 1, 0, 0, 0, 120);
        FillRect(0, 0, 1, Height, 125, 168, 190, 120);
        FillRect(Width - 1, 0, 1, Height, 0, 0, 0, 120);
    }

    private void FillRect(int x, int y, int width, int height, byte r, byte g, byte b, byte a)
    {
        var x1 = Clamp(x, 0, Width);
        var y1 = Clamp(y, 0, Height);
        var x2 = Clamp(x + width, 0, Width);
        var y2 = Clamp(y + height, 0, Height);

        for (var py = y1; py < y2; py++)
        for (var px = x1; px < x2; px++)
            SetPixel(px, py, r, g, b, a);
    }

    private void BlendTextBitmap(Bitmap bitmap)
    {
        var bounds = new Rectangle(0, 0, Width, Height);
        var data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var requiredLength = stride * Height;
            if (_bitmapPixels.Length < requiredLength)
                Array.Resize(ref _bitmapPixels, requiredLength);
            Marshal.Copy(data.Scan0, _bitmapPixels, 0, requiredLength);

            for (var y = 0; y < Height; y++)
            {
                var sourceRow = data.Stride >= 0 ? y * stride : (Height - 1 - y) * stride;
                var destRow = y * Width * 4;

                for (var x = 0; x < Width; x++)
                {
                    var sourceOffset = sourceRow + x * 4;
                    var sourceAlpha = _bitmapPixels[sourceOffset + 3];
                    if (sourceAlpha == 0)
                        continue;

                    var destOffset = destRow + x * 4;
                    BlendPixel(
                        destOffset,
                        _bitmapPixels[sourceOffset + 2],
                        _bitmapPixels[sourceOffset + 1],
                        _bitmapPixels[sourceOffset + 0],
                        sourceAlpha);
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private void BlendPixel(int offset, byte sourceR, byte sourceG, byte sourceB, byte sourceA)
    {
        var destA = Rgba[offset + 3];
        if (destA == 0 || sourceA == 255)
        {
            Rgba[offset + 0] = sourceR;
            Rgba[offset + 1] = sourceG;
            Rgba[offset + 2] = sourceB;
            Rgba[offset + 3] = sourceA;
            return;
        }

        var destFactor = destA * (255 - sourceA) / 255;
        var outAlpha = sourceA + destFactor;
        Rgba[offset + 0] = (byte)((sourceR * sourceA + Rgba[offset + 0] * destFactor) / outAlpha);
        Rgba[offset + 1] = (byte)((sourceG * sourceA + Rgba[offset + 1] * destFactor) / outAlpha);
        Rgba[offset + 2] = (byte)((sourceB * sourceA + Rgba[offset + 2] * destFactor) / outAlpha);
        Rgba[offset + 3] = (byte)outAlpha;
    }

    private void SetPixel(int x, int y, byte r, byte g, byte b, byte a)
    {
        if ((uint)x >= Width || (uint)y >= Height)
            return;

        var offset = (y * Width + x) * 4;
        Rgba[offset + 0] = r;
        Rgba[offset + 1] = g;
        Rgba[offset + 2] = b;
        Rgba[offset + 3] = a;
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
            return min;
        return value > max ? max : value;
    }
}

public sealed class DebugOverlayFontSet : IDisposable
{
    private readonly PrivateFontCollection? _defaultCollection;
    private readonly PrivateFontCollection? _carolingCollection;
    private readonly Font _defaultFont;
    private readonly Font _carolingTitleFont;

    private DebugOverlayFontSet(
        PrivateFontCollection? defaultCollection,
        PrivateFontCollection? carolingCollection,
        Font defaultFont,
        Font carolingTitleFont)
    {
        _defaultCollection = defaultCollection;
        _carolingCollection = carolingCollection;
        _defaultFont = defaultFont;
        _carolingTitleFont = carolingTitleFont;
    }

    public static DebugOverlayFontSet Load(string gameDirectory)
    {
        var fontDirectory = Path.Combine(gameDirectory, "font");
        var defaultCollection = LoadCollection(Path.Combine(fontDirectory, "ANTQS__.TTF"));
        var carolingCollection = LoadCollection(Path.Combine(fontDirectory, "CAROLING.TTF"));

        return new DebugOverlayFontSet(
            defaultCollection,
            carolingCollection,
            CreateFont(defaultCollection, FontFamily.GenericSansSerif, DebugTextOverlayFontSizes.Default),
            CreateFont(carolingCollection, FontFamily.GenericSerif, DebugTextOverlayFontSizes.Title));
    }

    public Font GetFont(DebugTextFont font) =>
        font == DebugTextFont.CarolingTitle ? _carolingTitleFont : _defaultFont;

    public void Dispose()
    {
        _defaultFont.Dispose();
        _carolingTitleFont.Dispose();
        _defaultCollection?.Dispose();
        _carolingCollection?.Dispose();
    }

    private static PrivateFontCollection? LoadCollection(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"Debug overlay font not found: {path}");
            return null;
        }

        var collection = new PrivateFontCollection();
        collection.AddFontFile(path);
        return collection;
    }

    private static Font CreateFont(PrivateFontCollection? collection, FontFamily fallback, float size)
    {
        var family = collection is { Families.Length: > 0 } ? collection.Families[0] : fallback;
        return new Font(family, size, FontStyle.Regular, GraphicsUnit.Pixel);
    }
}

public readonly record struct DebugTextLine(string Text, DebugTextFont Font)
{
    public static DebugTextLine Default(string text) => new(text, DebugTextFont.Default);

    public static DebugTextLine CarolingTitle(string text) => new(text, DebugTextFont.CarolingTitle);
}

public enum DebugTextFont
{
    Default,
    CarolingTitle
}

internal static class DebugTextOverlayFontSizes
{
    public const int Default = 20;
    public const int Title = 27;
}
