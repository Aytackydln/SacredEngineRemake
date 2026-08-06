using Sacred.Assets.Paks.Texture;

namespace Sacred.World.Rendering;

internal sealed class RgbaCanvas
{
    public RgbaCanvas(int width, int height, byte red, byte green, byte blue)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        Pixels = new byte[checked(width * height * 4)];
        for (var offset = 0; offset < Pixels.Length; offset += 4)
        {
            Pixels[offset] = red;
            Pixels[offset + 1] = green;
            Pixels[offset + 2] = blue;
            Pixels[offset + 3] = 255;
        }
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public void DrawTexture(TextureAsset texture, float left, float top, float width, float height, float brightness = 1.0f)
    {
        var firstX = Math.Max(0, (int)MathF.Floor(left));
        var firstY = Math.Max(0, (int)MathF.Floor(top));
        var lastX = Math.Min(Width, (int)MathF.Ceiling(left + width));
        var lastY = Math.Min(Height, (int)MathF.Ceiling(top + height));
        for (var y = firstY; y < lastY; y++)
        for (var x = firstX; x < lastX; x++)
        {
            var sourceX = Math.Clamp((int)((x - left) / width * texture.Width), 0, texture.Width - 1);
            var sourceY = Math.Clamp((int)((y - top) / height * texture.Height), 0, texture.Height - 1);
            BlendTexturePixel(texture, sourceX, sourceY, x, y, brightness);
        }
    }

    public void DrawTerrainDiamond(
        TextureAsset primary,
        int primaryX,
        int primaryY,
        TextureAsset? alphaMask,
        int alphaMaskX,
        int alphaMaskY,
        float left,
        float top,
        float width,
        float height)
    {
        var firstX = Math.Max(0, (int)MathF.Floor(left));
        var firstY = Math.Max(0, (int)MathF.Floor(top));
        var lastX = Math.Min(Width, (int)MathF.Ceiling(left + width));
        var lastY = Math.Min(Height, (int)MathF.Ceiling(top + height));
        for (var y = firstY; y < lastY; y++)
        for (var x = firstX; x < lastX; x++)
        {
            var u = (x + 0.5f - left) / width;
            var v = (y + 0.5f - top) / height;
            if (MathF.Abs(u * 2.0f - 1.0f) + MathF.Abs(v * 2.0f - 1.0f) > 1.0f)
                continue;

            var sourceX = Math.Clamp(primaryX + (int)(u * 100.0f), 0, primary.Width - 1);
            var sourceY = Math.Clamp(primaryY + (int)(v * 50.0f), 0, primary.Height - 1);
            var sourceOffset = (sourceY * primary.Width + sourceX) * 4;
            var alpha = primary.Rgba8[sourceOffset + 3];
            if (alphaMask is not null)
            {
                var maskX = Math.Clamp(alphaMaskX + (int)(u * 100.0f), 0, alphaMask.Width - 1);
                var maskY = Math.Clamp(alphaMaskY + (int)(v * 50.0f), 0, alphaMask.Height - 1);
                alpha = alphaMask.Rgba8[(maskY * alphaMask.Width + maskX) * 4 + 3];
            }

            BlendPixel(x, y, primary.Rgba8[sourceOffset], primary.Rgba8[sourceOffset + 1], primary.Rgba8[sourceOffset + 2], alpha);
        }
    }

    public void DrawLiquidDiamond(
        TextureAsset texture,
        byte variant,
        byte alphaLeft,
        byte alphaTop,
        byte alphaRight,
        byte alphaBottom,
        float left,
        float top,
        float width,
        float height)
    {
        var firstX = Math.Max(0, (int)MathF.Floor(left));
        var firstY = Math.Max(0, (int)MathF.Floor(top));
        var lastX = Math.Min(Width, (int)MathF.Ceiling(left + width));
        var lastY = Math.Min(Height, (int)MathF.Ceiling(top + height));
        for (var y = firstY; y < lastY; y++)
        for (var x = firstX; x < lastX; x++)
        {
            var u = (x + 0.5f - left) / width;
            var v = (y + 0.5f - top) / height;
            var positionX = u * 2.0f - 1.0f;
            var positionY = v * 2.0f - 1.0f;
            if (MathF.Abs(positionX) + MathF.Abs(positionY) > 1.0f)
                continue;

            var cellU = u + v - 0.5f;
            var cellV = v - u + 0.5f;
            var blockX = variant & 3;
            var blockY = (variant >> 2) & 3;
            var sourceX = Math.Clamp((int)(((blockX + cellU) * 0.25f) * texture.Width), 0, texture.Width - 1);
            var sourceY = Math.Clamp((int)(((blockY + cellV) * 0.25f) * texture.Height), 0, texture.Height - 1);
            var source = (sourceY * texture.Width + sourceX) * 4;
            var cornerAlpha = LiquidCornerAlpha(
                positionX, positionY, alphaLeft, alphaTop, alphaRight, alphaBottom);
            var alpha = (byte)(texture.Rgba8[source + 3] * cornerAlpha / 255);
            BlendPixel(x, y, texture.Rgba8[source], texture.Rgba8[source + 1], texture.Rgba8[source + 2], alpha);
        }
    }

    public void DrawCross(int centerX, int centerY, int radius, byte red, byte green, byte blue)
    {
        for (var delta = -radius; delta <= radius; delta++)
        {
            BlendPixel(centerX + delta, centerY, red, green, blue, 255);
            BlendPixel(centerX, centerY + delta, red, green, blue, 255);
        }
    }

    public RgbaImage ToImage() => new(Width, Height, Pixels);

    private void BlendTexturePixel(TextureAsset texture, int sourceX, int sourceY, int x, int y, float brightness)
    {
        var source = (sourceY * texture.Width + sourceX) * 4;
        BlendPixel(
            x,
            y,
            Scale(texture.Rgba8[source], brightness),
            Scale(texture.Rgba8[source + 1], brightness),
            Scale(texture.Rgba8[source + 2], brightness),
            texture.Rgba8[source + 3]);
    }

    private void BlendPixel(int x, int y, byte red, byte green, byte blue, byte alpha)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height || alpha == 0)
            return;

        var destination = (y * Width + x) * 4;
        var inverse = 255 - alpha;
        Pixels[destination] = (byte)((red * alpha + Pixels[destination] * inverse) / 255);
        Pixels[destination + 1] = (byte)((green * alpha + Pixels[destination + 1] * inverse) / 255);
        Pixels[destination + 2] = (byte)((blue * alpha + Pixels[destination + 2] * inverse) / 255);
        Pixels[destination + 3] = 255;
    }

    private static byte Scale(byte value, float amount) =>
        (byte)Math.Clamp((int)MathF.Round(value * amount), 0, 255);

    private static int LiquidCornerAlpha(
        float x,
        float y,
        byte left,
        byte top,
        byte right,
        byte bottom)
    {
        var center = (left + top + right + bottom) * 0.25f;
        float alpha;
        if (y < 0.0f)
            alpha = x < 0.0f
                ? center * (1.0f + x + y) - left * x - top * y
                : center * (1.0f - x + y) + right * x - top * y;
        else
            alpha = x < 0.0f
                ? center * (1.0f + x - y) - left * x + bottom * y
                : center * (1.0f - x - y) + right * x + bottom * y;
        return Math.Clamp((int)MathF.Round(alpha), 0, 255);
    }
}
