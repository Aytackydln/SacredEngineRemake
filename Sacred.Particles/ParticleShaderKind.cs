namespace Sacred.Particles;

/// <summary>
/// Renderer-independent shader families required by Sacred's billboard effects.
/// Texture.pak storage type is deliberately not represented here: it describes
/// compression and pixel layout, not how a decoded texture must be composed.
/// </summary>
public enum ParticleShaderKind
{
    StaticAlphaSprite,
    ItemGlow,
    ItemParticle,
    DenseItemParticle,
    ProceduralHalo,
    ProceduralSparkle
}
