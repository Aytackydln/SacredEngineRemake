using System;

namespace Sacred.Engine.Graphics;

/// <summary>HDR frame white and authored RGB gains. Legacy nits fields preserve saved settings.</summary>
public sealed record HdrBrightnessSettings
{
    // Fixed reset/default. OS SDR white is queried per display when Reset OS is selected.
    public const float DefaultSceneBrightnessNits = 160.0f;
    public const float DefaultUiBrightnessNits = 203.0f;
    public const float DefaultSunDiffuseNits = 203.0f;
    public const float DefaultSunSpecularNits = 600.0f;
    public const float DefaultUnlitSpriteNits = 380.0f;
    public const float DefaultUnlitColorMultiplier = 2.0f;
    public const float DefaultParticleColorMultiplier = 2.5f;

    private const float MinimumNits = 1.0f;
    private const float MaximumNits = 10_000.0f;

    public static HdrBrightnessSettings Default { get; } = new();

    public float SceneBrightnessNits { get; init; } = DefaultSceneBrightnessNits;
    public float UnlitColorMultiplier { get; init; } = DefaultUnlitColorMultiplier;
    public float ParticleColorMultiplier { get; init; } = DefaultParticleColorMultiplier;
    // Legacy per-material boost values no longer affect rendering. In particular,
    // the new RGB gains are independent of the obsolete material nits settings.
    public float UiBrightnessNits { get; init; } = DefaultUiBrightnessNits;
    public float SunDiffuseNits { get; init; } = DefaultSunDiffuseNits;
    public float SunSpecularNits { get; init; } = DefaultSunSpecularNits;
    public float UnlitSpriteNits { get; init; } = DefaultUnlitSpriteNits;

    public HdrBrightnessSettings Normalized() => this with
    {
        SceneBrightnessNits = Normalize(SceneBrightnessNits, DefaultSceneBrightnessNits),
        UnlitColorMultiplier = NormalizeMultiplier(UnlitColorMultiplier, DefaultUnlitColorMultiplier),
        ParticleColorMultiplier = NormalizeMultiplier(ParticleColorMultiplier, DefaultParticleColorMultiplier),
        UiBrightnessNits = Normalize(UiBrightnessNits, DefaultUiBrightnessNits),
        SunDiffuseNits = Normalize(SunDiffuseNits, DefaultSunDiffuseNits),
        SunSpecularNits = Normalize(SunSpecularNits, DefaultSunSpecularNits),
        UnlitSpriteNits = Normalize(UnlitSpriteNits, DefaultUnlitSpriteNits)
    };

    private static float Normalize(float value, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, MinimumNits, MaximumNits) : fallback;

    private static float NormalizeMultiplier(float value, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, 0.0f, 4.0f) : fallback;
}
