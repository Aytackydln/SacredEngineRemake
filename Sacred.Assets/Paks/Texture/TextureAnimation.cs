namespace Sacred.Assets.Paks.Texture;

public enum TextureAnimationMode
{
    None = 0,
    VerticalScrollBlackKey = 2,
    RadialSweepBlackKey = 3
}

public readonly record struct TextureAnimation(
    TextureAnimationMode Mode,
    float ScrollSpeed = 0.0f)
{
    public static readonly TextureAnimation None = new(TextureAnimationMode.None, 0.0f);

    public float TimeScale => ScrollSpeed;

    public bool IsAnimated =>
        Mode switch
        {
            TextureAnimationMode.VerticalScrollBlackKey or TextureAnimationMode.RadialSweepBlackKey => ScrollSpeed > 0.0f,
            _ => false
        };
}
