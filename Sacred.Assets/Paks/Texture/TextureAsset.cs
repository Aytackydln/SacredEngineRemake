namespace Sacred.Assets.Paks.Texture;

/// <summary>Channels that carry authored colour and coverage after archive decoding.</summary>
public enum SacredTextureChannelEncoding
{
    Rgb,
    Argb,
    AlphaMask
}

public static class SacredTextureChannelAnalyzer
{
    public static SacredTextureChannelEncoding Analyze(ReadOnlySpan<byte> rgba)
    {
        var hasTransparent = false;
        var hasOpaque = false;
        var hasFractionalAlpha = false;
        var hasColor = false;
        for (var offset = 0; offset < rgba.Length; offset += 4)
        {
            var alpha = rgba[offset + 3];
            hasTransparent |= alpha == 0;
            hasOpaque |= alpha == byte.MaxValue;
            hasFractionalAlpha |= alpha is not 0 and not byte.MaxValue;
            // Neutral RGB can contain authored rays, rings and brightness gradients.
            // Only an entirely black RGB plane leaves alpha as the sole usable channel.
            hasColor |= rgba[offset] != 0 || rgba[offset + 1] != 0 || rgba[offset + 2] != 0;
        }

        var alphaCarriesCoverage = hasFractionalAlpha || (hasTransparent && hasOpaque);
        if (!alphaCarriesCoverage)
            return SacredTextureChannelEncoding.Rgb;
        return hasColor
            ? SacredTextureChannelEncoding.Argb
            : SacredTextureChannelEncoding.AlphaMask;
    }
}

public sealed record TextureAsset(
    string Name,
    int Width,
    int Height,
    byte[] Rgba8,
    TextureAnimation Animation = default);

public sealed class StaticSpriteAsset
{
    private byte[] _rgba;

    public StaticSpriteAsset(
        uint groupId,
        int width,
        int height,
        int anchorX,
        int anchorY,
        byte[] rgba,
        int frameCount = 1,
        float frameDurationSeconds = 0.0f,
        int placementX = 0,
        int placementY = 0,
        bool retainPixelData = false)
    {
        GroupId = groupId;
        Width = width;
        Height = height;
        AnchorX = anchorX;
        AnchorY = anchorY;
        _rgba = rgba;
        FrameCount = frameCount;
        FrameDurationSeconds = frameDurationSeconds;
        PlacementX = placementX;
        PlacementY = placementY;
        RetainsPixelData = retainPixelData;
        HasTranslucentPixels = ContainsFractionalAlpha(rgba);
        ChannelEncoding = SacredTextureChannelAnalyzer.Analyze(rgba);
        Coverage = StaticSpriteCoverage.Measure(rgba, width, height, frameCount, AtlasColumns, AtlasRows);
    }

    public uint GroupId { get; }
    public int Width { get; }
    public int Height { get; }
    public int AnchorX { get; }
    public int AnchorY { get; }
    /// <summary>Mixed.pak placement point measured from the untrimmed group origin.</summary>
    public int PlacementX { get; }
    /// <summary>Mixed.pak placement point measured from the untrimmed group origin.</summary>
    public int PlacementY { get; }
    /// <summary>Whether another asynchronous renderer may still require the source pixels.</summary>
    public bool RetainsPixelData { get; }
    public byte[] Rgba => Volatile.Read(ref _rgba);
    public int FrameCount { get; }
    public float FrameDurationSeconds { get; }
    public int AtlasColumns => TextureFrameAtlasLayout.CalculateColumns(Width, Height, FrameCount);
    public int AtlasRows => TextureFrameAtlasLayout.CalculateRows(FrameCount, AtlasColumns);
    public int AtlasWidth => checked(Width * AtlasColumns);
    public int AtlasHeight => checked(Height * AtlasRows);
    public float AnimationPeriodSeconds => FrameDurationSeconds * FrameCount;
    /// <summary>Whether the authored sprite contains alpha values between fully transparent and opaque.</summary>
    public bool HasTranslucentPixels { get; }
    public SacredTextureChannelEncoding ChannelEncoding { get; }
    /// <summary>Retained after upload releases pixels; shared by all instances of this appearance.</summary>
    public StaticSpriteCoverage Coverage { get; }

    public void ReleasePixelData()
    {
        if (!RetainsPixelData)
            Interlocked.Exchange(ref _rgba, []);
    }

    private static bool ContainsFractionalAlpha(ReadOnlySpan<byte> rgba)
    {
        for (var index = 3; index < rgba.Length; index += 4)
        {
            if (rgba[index] is not 0 and not byte.MaxValue)
                return true;
        }

        return false;
    }
}

public sealed class TextureFrameSequenceAsset
{
    private byte[] _rgba8FrameAtlas;

    public TextureFrameSequenceAsset(
        string name,
        int frameWidth,
        int frameHeight,
        int frameCount,
        byte[] rgba8FrameAtlas)
    {
        Name = name;
        FrameWidth = frameWidth;
        FrameHeight = frameHeight;
        FrameCount = frameCount;
        _rgba8FrameAtlas = rgba8FrameAtlas;
    }

    public string Name { get; }
    public int FrameWidth { get; }
    public int FrameHeight { get; }
    public int FrameCount { get; }
    public byte[] Rgba8FrameAtlas => Volatile.Read(ref _rgba8FrameAtlas);
    public int AtlasColumns => TextureFrameAtlasLayout.CalculateColumns(FrameWidth, FrameHeight, FrameCount);
    public int AtlasRows => TextureFrameAtlasLayout.CalculateRows(FrameCount, AtlasColumns);
    public int AtlasWidth => checked(FrameWidth * AtlasColumns);
    public int AtlasHeight => checked(FrameHeight * AtlasRows);

    public void ReleasePixelData() => Interlocked.Exchange(ref _rgba8FrameAtlas, []);
}

public static class TextureFrameAtlasLayout
{
    public static int CalculateColumns(int frameWidth, int frameHeight, int frameCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameCount);

        var approximatelySquare = (int)Math.Ceiling(
            Math.Sqrt(frameCount * (double)frameHeight / frameWidth));
        return Math.Clamp(approximatelySquare, 1, frameCount);
    }

    public static int CalculateRows(int frameCount, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        return checked((frameCount + columns - 1) / columns);
    }
}
