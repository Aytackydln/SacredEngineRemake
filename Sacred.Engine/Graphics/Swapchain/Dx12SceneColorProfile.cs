namespace Sacred.Engine.Graphics.Swapchain;

/// <summary>Authored RGB scales, independent of final HDR frame white.</summary>
internal readonly record struct Dx12SceneColorProfile(
    float SceneWhiteScale,
    float UiWhiteScale,
    float SunDiffuseScale,
    float SunSpecularScale,
    float UnlitSpriteScale)
{
    public float ParticleColorMultiplier { get; init; } = 1.0f;
    public static Dx12SceneColorProfile Native { get; } = new(
        SceneWhiteScale: 1.0f,
        UiWhiteScale: 1.0f,
        SunDiffuseScale: 1.0f,
        SunSpecularScale: 1.0f,
        UnlitSpriteScale: 1.0f);
}
