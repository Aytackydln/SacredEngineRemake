namespace Sacred.Engine.Graphics.Swapchain;

internal readonly record struct Dx12DisplayProfile(
    float ScenePaperWhiteNits,
    float UiPaperWhiteNits,
    float SunDiffuseNits,
    float SunSpecularNits)
{
    public static Dx12DisplayProfile Sdr { get; } = new(
        ScenePaperWhiteNits: 1.0f,
        UiPaperWhiteNits: 1.0f,
        SunDiffuseNits: 1.0f,
        SunSpecularNits: 1.0f);

    public static Dx12DisplayProfile Hdr { get; } = new(
        ScenePaperWhiteNits: 203.0f,
        UiPaperWhiteNits: 180.0f,
        SunDiffuseNits: 360.0f,
        SunSpecularNits: 1000.0f);
}