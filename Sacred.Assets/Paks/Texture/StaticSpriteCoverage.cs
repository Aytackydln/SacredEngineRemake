namespace Sacred.Assets.Paks.Texture;

/// <summary>Union of nonzero coverage in frame-local texels, including a filter gutter.</summary>
public readonly record struct StaticSpriteCoverage(int X, int Y, int Width, int Height)
{
    public static StaticSpriteCoverage Measure(
        ReadOnlySpan<byte> rgba, int width, int height, int frames, int columns, int rows)
    {
        var atlasWidth = checked(width * columns);
        if (rgba.Length < checked(atlasWidth * height * rows * 4))
            return new(0, 0, width, height);

        var left = width;
        var top = height;
        var right = -1;
        var bottom = -1;
        for (var frame = 0; frame < frames; frame++)
        {
            var frameX = frame % columns * width;
            var frameY = frame / columns * height;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if (rgba[((frameY + y) * atlasWidth + frameX + x) * 4 + 3] == 0)
                    continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        // An empty sprite keeps its existing geometry and sampling behavior.
        if (right < left) return new(0, 0, width, height);
        left = Math.Max(0, left - 1);
        top = Math.Max(0, top - 1);
        right = Math.Min(width - 1, right + 1);
        bottom = Math.Min(height - 1, bottom + 1);
        return new(left, top, right - left + 1, bottom - top + 1);
    }
}
