namespace Sacred.Assets.Paks.Texture;

public enum TextureAnimationMode
{
    None = 0,
    /// <summary>Gold complex-material UV matrix selected by Items.pak offset 0x6E.</summary>
    NativeItemTransform = 4,
    VerticalScrollBlackKey = 2,
    RadialSweepBlackKey = 3
}

public readonly record struct TextureAnimation(
    TextureAnimationMode Mode,
    float ScrollSpeed = 0.0f,
    byte NativeEffectSelector = 0)
{
    public static readonly TextureAnimation None = new(TextureAnimationMode.None, 0.0f);

    // Gold cGranny constructor 0x401239 resolves this fixed resource for selector 1's second pass.
    public const string MultitextureGlowName = "FX_MULTITEXGLOW01.TGA";
    public bool HasSecondaryGlow => Mode == TextureAnimationMode.NativeItemTransform && NativeEffectSelector == 1;
    public float TimeScale => ScrollSpeed;

    public bool IsAnimated =>
        Mode switch
        {
            TextureAnimationMode.NativeItemTransform => true,
            TextureAnimationMode.VerticalScrollBlackKey or TextureAnimationMode.RadialSweepBlackKey => ScrollSpeed > 0.0f,
            _ => false
        };
}
