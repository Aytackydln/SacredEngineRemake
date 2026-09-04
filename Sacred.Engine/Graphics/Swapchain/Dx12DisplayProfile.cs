namespace Sacred.Engine.Graphics.Swapchain;

internal readonly record struct Dx12DisplayProfile(
    float ScenePaperWhiteNits,
    float UiPaperWhiteNits,
    float SunDiffuseNits,
    float SunSpecularNits,
    float UnlitSpriteNits)
{
    public static Dx12DisplayProfile Sdr { get; } = new(
        ScenePaperWhiteNits: 1.0f,
        UiPaperWhiteNits: 1.0f,
        SunDiffuseNits: 1.0f,
        SunSpecularNits: 1.0f,
        UnlitSpriteNits: 1.0f);

    public static Dx12DisplayProfile CreateHdr(HdrBrightnessSettings settings) => new(
        ScenePaperWhiteNits: settings.SceneBrightnessNits,
        UiPaperWhiteNits: settings.UiBrightnessNits,
        SunDiffuseNits: settings.SunDiffuseNits,
        SunSpecularNits: settings.SunSpecularNits,
        UnlitSpriteNits: settings.UnlitSpriteNits);
}
