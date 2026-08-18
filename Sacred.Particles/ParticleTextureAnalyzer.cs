namespace Sacred.Particles;

/// <summary>
/// Identifies the channel convention of a decoded RGBA texture. This is intended
/// for catalogue building and diagnostics; authored render metadata remains the
/// stronger signal when it exists.
/// </summary>
public static class ParticleTextureAnalyzer
{
    private const byte BlackThreshold = 8;
    private const byte ChromaticThreshold = 8;

    public static ParticleTextureAnalysis Analyze(ReadOnlySpan<byte> rgba8, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var expectedLength = checked(width * height * 4);
        if (rgba8.Length != expectedLength)
            throw new ArgumentException(
                $"RGBA byte count {rgba8.Length} does not match {width}x{height} ({expectedLength} bytes).",
                nameof(rgba8));

        var transparentPixels = 0;
        var translucentPixels = 0;
        var opaquePixels = 0;
        var blackPixels = 0;
        var chromaticPixels = 0;
        var blackEdgePixels = 0;
        var edgePixels = 0;

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = (y * width + x) * 4;
            var red = rgba8[offset];
            var green = rgba8[offset + 1];
            var blue = rgba8[offset + 2];
            var alpha = rgba8[offset + 3];
            if (alpha == 0)
                transparentPixels++;
            else if (alpha == byte.MaxValue)
                opaquePixels++;
            else
                translucentPixels++;

            var maximum = Math.Max(red, Math.Max(green, blue));
            var minimum = Math.Min(red, Math.Min(green, blue));
            var isBlack = maximum <= BlackThreshold;
            if (isBlack)
                blackPixels++;
            if (maximum - minimum > ChromaticThreshold)
                chromaticPixels++;

            if (x != 0 && y != 0 && x != width - 1 && y != height - 1)
                continue;
            edgePixels++;
            if (isBlack)
                blackEdgePixels++;
        }

        var pixelCount = checked(width * height);
        var alphaVaries = translucentPixels > 0 ||
                          (transparentPixels > 0 && opaquePixels > 0);
        var encoding = alphaVaries
            ? chromaticPixels == 0
                ? ParticleTextureEncoding.AlphaMask
                : ParticleTextureEncoding.AlphaColour
            : HasBlackKeyBackground(
                pixelCount,
                blackPixels,
                blackEdgePixels,
                edgePixels,
                transparentPixels,
                chromaticPixels)
                ? ParticleTextureEncoding.BlackKeyColour
                : ParticleTextureEncoding.OpaqueColour;

        return new ParticleTextureAnalysis(
            encoding,
            pixelCount,
            transparentPixels,
            translucentPixels,
            opaquePixels,
            blackPixels,
            chromaticPixels,
            blackEdgePixels,
            edgePixels);
    }

    private static bool HasBlackKeyBackground(
        int pixelCount,
        int blackPixels,
        int blackEdgePixels,
        int edgePixels,
        int transparentPixels,
        int chromaticPixels)
    {
        var containsSignal = blackPixels < pixelCount || chromaticPixels > 0;
        if (!containsSignal)
            return false;

        // Some original textures carry useful RGB while their alpha channel is
        // entirely zero. Others are fully opaque but reserve black as zero energy.
        return transparentPixels == pixelCount ||
               blackPixels * 10 >= pixelCount ||
               blackEdgePixels * 4 >= edgePixels * 3;
    }
}

public readonly record struct ParticleTextureAnalysis(
    ParticleTextureEncoding Encoding,
    int PixelCount,
    int TransparentPixels,
    int TranslucentPixels,
    int OpaquePixels,
    int BlackPixels,
    int ChromaticPixels,
    int BlackEdgePixels,
    int EdgePixels);
