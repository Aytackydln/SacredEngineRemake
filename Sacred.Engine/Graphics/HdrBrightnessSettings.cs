using System;

namespace Sacred.Engine.Graphics;

/// <summary>HDR luminance targets expressed in display nits.</summary>
public sealed record HdrBrightnessSettings
{
    public const float DefaultSceneBrightnessNits = 160.0f;
    public const float DefaultUiBrightnessNits = 203.0f;
    public const float DefaultSunDiffuseNits = 203.0f;
    public const float DefaultSunSpecularNits = 600.0f;
    public const float DefaultUnlitSpriteNits = 380.0f;

    private const float MinimumNits = 1.0f;
    private const float MaximumNits = 10_000.0f;

    public static HdrBrightnessSettings Default { get; } = new();

    public float SceneBrightnessNits { get; init; } = DefaultSceneBrightnessNits;
    public float UiBrightnessNits { get; init; } = DefaultUiBrightnessNits;
    public float SunDiffuseNits { get; init; } = DefaultSunDiffuseNits;
    public float SunSpecularNits { get; init; } = DefaultSunSpecularNits;
    public float UnlitSpriteNits { get; init; } = DefaultUnlitSpriteNits;

    public HdrBrightnessSettings Normalized() => this with
    {
        SceneBrightnessNits = Normalize(SceneBrightnessNits, DefaultSceneBrightnessNits),
        UiBrightnessNits = Normalize(UiBrightnessNits, DefaultUiBrightnessNits),
        SunDiffuseNits = Normalize(SunDiffuseNits, DefaultSunDiffuseNits),
        SunSpecularNits = Normalize(SunSpecularNits, DefaultSunSpecularNits),
        UnlitSpriteNits = Normalize(UnlitSpriteNits, DefaultUnlitSpriteNits)
    };

    private static float Normalize(float value, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, MinimumNits, MaximumNits) : fallback;
}
