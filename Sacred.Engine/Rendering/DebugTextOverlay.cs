using System;

namespace Sacred.Engine.Rendering;

public sealed class DebugTextOverlay
{
    public const int Width = 660;
    public const int Height = 176;

    private const int Padding = 8;
    private const int GlyphWidth = 5;
    private const int GlyphHeight = 7;
    private const int GlyphScale = 2;
    private const int CharacterSpacing = 1;
    private const int LineSpacing = 3;
    private const int GlyphAdvance = (GlyphWidth + CharacterSpacing) * GlyphScale;
    private const int LineAdvance = GlyphHeight * GlyphScale + LineSpacing;

    public byte[] Rgba { get; } = new byte[Width * Height * 4];

    public void SetLines(string[] lines)
    {
        Array.Clear(Rgba, 0, Rgba.Length);
        FillPanel();

        var y = Padding;
        foreach (var line in lines)
        {
            DrawString(line, Padding + 1, y + 1, 0, 0, 0, 190);
            DrawString(line, Padding, y, 238, 246, 255, 245);
            y += LineAdvance;
            if (y + GlyphHeight * GlyphScale > Height - Padding)
                break;
        }
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

    private void DrawString(string text, int x, int y, byte r, byte g, byte b, byte a)
    {
        var cursor = x;
        foreach (var c in text)
        {
            if (cursor >= Width - Padding)
                return;

            DrawGlyph(char.ToUpperInvariant(c), cursor, y, r, g, b, a);
            cursor += GlyphAdvance;
        }
    }

    private void DrawGlyph(char c, int x, int y, byte r, byte g, byte b, byte a)
    {
        if (c == ' ')
            return;

        var rows = GlyphRows(c);
        for (var gy = 0; gy < GlyphHeight; gy++)
        {
            var row = rows[gy];
            for (var gx = 0; gx < GlyphWidth; gx++)
            {
                if ((row & (1 << (GlyphWidth - 1 - gx))) == 0)
                    continue;

                var px = x + gx * GlyphScale;
                var py = y + gy * GlyphScale;
                for (var sy = 0; sy < GlyphScale; sy++)
                for (var sx = 0; sx < GlyphScale; sx++)
                    SetPixel(px + sx, py + sy, r, g, b, a);
            }
        }
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

    private static byte[] GlyphRows(char c) => c switch
    {
        'A' => A,
        'B' => B,
        'C' => C,
        'D' => D,
        'E' => E,
        'F' => F,
        'G' => G,
        'H' => H,
        'I' => I,
        'J' => J,
        'K' => K,
        'L' => L,
        'M' => M,
        'N' => N,
        'O' => O,
        'P' => P,
        'Q' => Q,
        'R' => R,
        'S' => S,
        'T' => T,
        'U' => U,
        'V' => V,
        'W' => W,
        'X' => X,
        'Y' => Y,
        'Z' => Z,
        '0' => Zero,
        '1' => One,
        '2' => Two,
        '3' => Three,
        '4' => Four,
        '5' => Five,
        '6' => Six,
        '7' => Seven,
        '8' => Eight,
        '9' => Nine,
        ':' => Colon,
        '.' => Dot,
        ',' => Comma,
        '/' => Slash,
        '-' => Dash,
        '+' => Plus,
        '(' => LeftParen,
        ')' => RightParen,
        _ => Unknown
    };

    private static readonly byte[] A = [0b01110, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001];
    private static readonly byte[] B = [0b11110, 0b10001, 0b10001, 0b11110, 0b10001, 0b10001, 0b11110];
    private static readonly byte[] C = [0b01111, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b01111];
    private static readonly byte[] D = [0b11110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b11110];
    private static readonly byte[] E = [0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b11111];
    private static readonly byte[] F = [0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b10000];
    private static readonly byte[] G = [0b01111, 0b10000, 0b10000, 0b10011, 0b10001, 0b10001, 0b01111];
    private static readonly byte[] H = [0b10001, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001];
    private static readonly byte[] I = [0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b11111];
    private static readonly byte[] J = [0b00111, 0b00010, 0b00010, 0b00010, 0b10010, 0b10010, 0b01100];
    private static readonly byte[] K = [0b10001, 0b10010, 0b10100, 0b11000, 0b10100, 0b10010, 0b10001];
    private static readonly byte[] L = [0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b11111];
    private static readonly byte[] M = [0b10001, 0b11011, 0b10101, 0b10101, 0b10001, 0b10001, 0b10001];
    private static readonly byte[] N = [0b10001, 0b11001, 0b10101, 0b10011, 0b10001, 0b10001, 0b10001];
    private static readonly byte[] O = [0b01110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110];
    private static readonly byte[] P = [0b11110, 0b10001, 0b10001, 0b11110, 0b10000, 0b10000, 0b10000];
    private static readonly byte[] Q = [0b01110, 0b10001, 0b10001, 0b10001, 0b10101, 0b10010, 0b01101];
    private static readonly byte[] R = [0b11110, 0b10001, 0b10001, 0b11110, 0b10100, 0b10010, 0b10001];
    private static readonly byte[] S = [0b01111, 0b10000, 0b10000, 0b01110, 0b00001, 0b00001, 0b11110];
    private static readonly byte[] T = [0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100];
    private static readonly byte[] U = [0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110];
    private static readonly byte[] V = [0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01010, 0b00100];
    private static readonly byte[] W = [0b10001, 0b10001, 0b10001, 0b10101, 0b10101, 0b10101, 0b01010];
    private static readonly byte[] X = [0b10001, 0b10001, 0b01010, 0b00100, 0b01010, 0b10001, 0b10001];
    private static readonly byte[] Y = [0b10001, 0b10001, 0b01010, 0b00100, 0b00100, 0b00100, 0b00100];
    private static readonly byte[] Z = [0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b10000, 0b11111];
    private static readonly byte[] Zero = [0b01110, 0b10001, 0b10011, 0b10101, 0b11001, 0b10001, 0b01110];
    private static readonly byte[] One = [0b00100, 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110];
    private static readonly byte[] Two = [0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b01000, 0b11111];
    private static readonly byte[] Three = [0b11110, 0b00001, 0b00001, 0b01110, 0b00001, 0b00001, 0b11110];
    private static readonly byte[] Four = [0b00010, 0b00110, 0b01010, 0b10010, 0b11111, 0b00010, 0b00010];
    private static readonly byte[] Five = [0b11111, 0b10000, 0b10000, 0b11110, 0b00001, 0b00001, 0b11110];
    private static readonly byte[] Six = [0b01110, 0b10000, 0b10000, 0b11110, 0b10001, 0b10001, 0b01110];
    private static readonly byte[] Seven = [0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b01000, 0b01000];
    private static readonly byte[] Eight = [0b01110, 0b10001, 0b10001, 0b01110, 0b10001, 0b10001, 0b01110];
    private static readonly byte[] Nine = [0b01110, 0b10001, 0b10001, 0b01111, 0b00001, 0b00001, 0b01110];
    private static readonly byte[] Colon = [0b00000, 0b00100, 0b00100, 0b00000, 0b00100, 0b00100, 0b00000];
    private static readonly byte[] Dot = [0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b01100, 0b01100];
    private static readonly byte[] Comma = [0b00000, 0b00000, 0b00000, 0b00000, 0b00110, 0b00100, 0b01000];
    private static readonly byte[] Slash = [0b00001, 0b00010, 0b00010, 0b00100, 0b01000, 0b01000, 0b10000];
    private static readonly byte[] Dash = [0b00000, 0b00000, 0b00000, 0b11111, 0b00000, 0b00000, 0b00000];
    private static readonly byte[] Plus = [0b00000, 0b00100, 0b00100, 0b11111, 0b00100, 0b00100, 0b00000];
    private static readonly byte[] LeftParen = [0b00010, 0b00100, 0b01000, 0b01000, 0b01000, 0b00100, 0b00010];
    private static readonly byte[] RightParen = [0b01000, 0b00100, 0b00010, 0b00010, 0b00010, 0b00100, 0b01000];
    private static readonly byte[] Unknown = [0b11111, 0b00001, 0b00010, 0b00100, 0b00100, 0b00000, 0b00100];
}
