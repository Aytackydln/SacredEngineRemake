using System;

namespace Sacred.Engine.Graphics.Sprites;

/// <summary>
/// Extends visible sprite colours into transparent texels so linear texture filtering does not
/// mix sprite edges with the transparent black used by Sacred's source atlases.
/// </summary>
internal static class SpriteTransparentEdgePadding
{
    public static void Apply(
        byte[] rgba,
        int atlasWidth,
        int atlasHeight,
        int frameWidth,
        int frameHeight)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        if (atlasWidth <= 0 || atlasHeight <= 0 || frameWidth <= 0 || frameHeight <= 0 ||
            rgba.Length != checked(atlasWidth * atlasHeight * 4))
        {
            throw new ArgumentException("Sprite atlas dimensions do not match its RGBA data.", nameof(rgba));
        }

        for (var frameY = 0; frameY < atlasHeight; frameY += frameHeight)
        for (var frameX = 0; frameX < atlasWidth; frameX += frameWidth)
            PadFrame(rgba, atlasWidth, frameX, frameY, frameWidth, frameHeight);
    }

    private static void PadFrame(
        byte[] rgba,
        int atlasWidth,
        int frameX,
        int frameY,
        int frameWidth,
        int frameHeight)
    {
        var frameRight = frameX + frameWidth;
        var frameBottom = frameY + frameHeight;
        for (var y = frameY; y < frameBottom; y++)
        for (var x = frameX; x < frameRight; x++)
        {
            var offset = (y * atlasWidth + x) * 4;
            if (rgba[offset + 3] != 0)
                continue;

            var red = 0;
            var green = 0;
            var blue = 0;
            var weight = 0;
            for (var neighborY = Math.Max(frameY, y - 1); neighborY <= Math.Min(frameBottom - 1, y + 1); neighborY++)
            for (var neighborX = Math.Max(frameX, x - 1); neighborX <= Math.Min(frameRight - 1, x + 1); neighborX++)
            {
                var neighborOffset = (neighborY * atlasWidth + neighborX) * 4;
                var alpha = rgba[neighborOffset + 3];
                if (alpha == 0)
                    continue;

                red += rgba[neighborOffset] * alpha;
                green += rgba[neighborOffset + 1] * alpha;
                blue += rgba[neighborOffset + 2] * alpha;
                weight += alpha;
            }

            if (weight == 0)
                continue;

            rgba[offset] = (byte)(red / weight);
            rgba[offset + 1] = (byte)(green / weight);
            rgba[offset + 2] = (byte)(blue / weight);
        }
    }
}
