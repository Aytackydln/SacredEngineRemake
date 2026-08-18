namespace Sacred.Particles;

/// <summary>Maps authored particle modes and decoded texture encodings to shader families.</summary>
public static class ParticleShaderCatalog
{
    public static ParticleShaderKind ForMode(ParticleTextureMode mode) => mode switch
    {
        ParticleTextureMode.Luminance or
        ParticleTextureMode.Atlas4X4 or
        ParticleTextureMode.WeaponGlowFlare => ParticleShaderKind.ItemGlow,

        ParticleTextureMode.MagicOrb or
        ParticleTextureMode.FirePop or
        ParticleTextureMode.PoisonStatic => ParticleShaderKind.DenseItemParticle,

        ParticleTextureMode.Alpha or
        ParticleTextureMode.BouncyAlpha => ParticleShaderKind.ItemParticle,

        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown particle texture mode.")
    };

    /// <summary>
    /// Provides the safe default for a free-standing billboard when no authored
    /// effect mode is available. Callers with item/world metadata should prefer it.
    /// </summary>
    public static ParticleShaderKind ForTextureEncoding(ParticleTextureEncoding encoding) => encoding switch
    {
        ParticleTextureEncoding.AlphaColour => ParticleShaderKind.StaticAlphaSprite,
        ParticleTextureEncoding.AlphaMask => ParticleShaderKind.ItemGlow,
        ParticleTextureEncoding.BlackKeyColour => ParticleShaderKind.ItemParticle,
        ParticleTextureEncoding.OpaqueColour => ParticleShaderKind.StaticAlphaSprite,
        _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Unknown particle texture encoding.")
    };
}
