namespace Sacred.Assets.Paks.Texture;

public sealed record TextureAsset(
    string Name,
    int Width,
    int Height,
    byte[] Rgba8,
    TextureAnimation Animation = default);

public sealed record StaticSpriteAsset(
    uint GroupId,
    int Width,
    int Height,
    int AnchorX,
    int AnchorY,
    byte[] Rgba,
    int FrameCount = 1,
    float FrameDurationSeconds = 0.0f)
{
    public int AtlasColumns => TextureFrameAtlasLayout.CalculateColumns(Width, Height, FrameCount);
    public int AtlasRows => TextureFrameAtlasLayout.CalculateRows(FrameCount, AtlasColumns);
    public int AtlasWidth => checked(Width * AtlasColumns);
    public int AtlasHeight => checked(Height * AtlasRows);
    public float AnimationPeriodSeconds => FrameDurationSeconds * FrameCount;
}

public sealed record TextureFrameSequenceAsset(
    string Name,
    int FrameWidth,
    int FrameHeight,
    int FrameCount,
    byte[] Rgba8FrameAtlas)
{
    public int AtlasColumns => TextureFrameAtlasLayout.CalculateColumns(FrameWidth, FrameHeight, FrameCount);
    public int AtlasRows => TextureFrameAtlasLayout.CalculateRows(FrameCount, AtlasColumns);
    public int AtlasWidth => checked(FrameWidth * AtlasColumns);
    public int AtlasHeight => checked(FrameHeight * AtlasRows);
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
