namespace Sacred.Assets.Paks.Texture;

public enum TextureAnimationMode
{
    None = 0,
    FrameStrip = 1,
    VerticalScrollBlackKey = 2
}

public readonly record struct TextureAnimation(
    int FrameCount,
    float FramesPerSecond,
    TextureAnimationMode Mode = TextureAnimationMode.FrameStrip)
{
    public static readonly TextureAnimation None = new(1, 0.0f, TextureAnimationMode.None);

    public bool IsAnimated =>
        FramesPerSecond > 0.0f &&
        Mode switch
        {
            TextureAnimationMode.FrameStrip => FrameCount > 1,
            TextureAnimationMode.VerticalScrollBlackKey => true,
            _ => false
        };
}
