namespace Sacred.World.Rendering;

/// <summary>Area filtering with premultiplied alpha for reducing offline render resolution.</summary>
public static class RgbaImageDownsampler
{
    public static RgbaImage Resize(RgbaImage source, int width, int height)
    {
        source.Validate();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (width > source.Width || height > source.Height)
            throw new ArgumentException("Area downsampling requires dimensions no larger than the source.");
        if (width == source.Width && height == source.Height) return source;
        if (source.Width % width == 0 && source.Height % height == 0 &&
            source.Width / width is 1 or 2 or 4 && source.Height / height is 1 or 2 or 4)
            return IntegerAreaDownsampler.Resize(source, width, height, source.Width / width, source.Height / height);
        var pixels = new byte[checked(width * height * 4)];
        var scaleX = (double)source.Width / width;
        var scaleY = (double)source.Height / height;
        for (var y = 0; y < height; y++)
        {
            var top = y * scaleY;
            var bottom = (y + 1) * scaleY;
            for (var x = 0; x < width; x++)
            {
                var left = x * scaleX;
                var right = (x + 1) * scaleX;
                double red = 0, green = 0, blue = 0, alpha = 0;
                for (var sy = (int)top; sy < Math.Min(source.Height, (int)Math.Ceiling(bottom)); sy++)
                {
                    var weightY = Math.Min(bottom, sy + 1) - Math.Max(top, sy);
                    for (var sx = (int)left; sx < Math.Min(source.Width, (int)Math.Ceiling(right)); sx++)
                    {
                        var weight = weightY * (Math.Min(right, sx + 1) - Math.Max(left, sx));
                        var offset = (sy * source.Width + sx) * 4;
                        var coverage = source.Pixels[offset + 3] * weight;
                        red += source.Pixels[offset] * coverage;
                        green += source.Pixels[offset + 1] * coverage;
                        blue += source.Pixels[offset + 2] * coverage;
                        alpha += coverage;
                    }
                }
                var target = (y * width + x) * 4;
                if (alpha > 0)
                {
                    pixels[target] = ToByte(red / alpha);
                    pixels[target + 1] = ToByte(green / alpha);
                    pixels[target + 2] = ToByte(blue / alpha);
                    pixels[target + 3] = ToByte(alpha / (scaleX * scaleY));
                }
            }
        }
        return new RgbaImage(width, height, pixels);
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
}
