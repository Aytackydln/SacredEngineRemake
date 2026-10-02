namespace Sacred.World.Rendering;

/// <summary>Exact area averages for the exporter's integer supersampling factors.</summary>
internal static class IntegerAreaDownsampler
{
    public static RgbaImage Resize(RgbaImage source, int width, int height, int factorX, int factorY)
    {
        var pixels = new byte[checked(width * height * 4)];
        var sampleCount = factorX * factorY;
        void Row(int y)
        {
            var firstSourceRow = y * factorY * source.Stride;
            var destination = y * width * 4;
            for (var x = 0; x < width; x++, destination += 4)
            {
                var firstSource = firstSourceRow + x * factorX * 4;
                int red = 0, green = 0, blue = 0, alpha = 0;
                for (var sy = 0; sy < factorY; sy++)
                {
                    var offset = firstSource + sy * source.Stride;
                    for (var sx = 0; sx < factorX; sx++, offset += 4)
                    {
                        var coverage = source.Pixels[offset + 3];
                        red += source.Pixels[offset] * coverage;
                        green += source.Pixels[offset + 1] * coverage;
                        blue += source.Pixels[offset + 2] * coverage;
                        alpha += coverage;
                    }
                }
                if (alpha == 0) continue;
                // Keep the general filter's round-to-even behavior and alpha weighting.
                pixels[destination] = (byte)Math.Round((double)red / alpha);
                pixels[destination + 1] = (byte)Math.Round((double)green / alpha);
                pixels[destination + 2] = (byte)Math.Round((double)blue / alpha);
                pixels[destination + 3] = (byte)Math.Round((double)alpha / sampleCount);
            }
        }

        if ((long)source.Width * source.Height >= 1024 * 1024 && height >= 64)
            Parallel.For(0, height, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8) }, Row);
        else
            for (var y = 0; y < height; y++) Row(y);
        return new RgbaImage(width, height, pixels);
    }
}
