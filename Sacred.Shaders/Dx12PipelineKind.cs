namespace Sacred.Shaders;

/// <summary>Stable names for the graphics pipelines supplied by the Sacred shader catalog.</summary>
public enum Dx12PipelineKind
{
    Terrain,
    TerrainLiquidCover,
    StaticSpriteShadow,
    StaticSprite,
    TransparentStaticSprite,
    UnlitStaticSprite,
    TransparentUnlitStaticSprite,
    TransparentUnlitParticleRgb,
    TransparentUnlitParticleArgb,
    TransparentUnlitParticleAlphaMask,
    LiquidSprite,
    SurfaceLightMap,
    PlayerOcclusionMap,
    LightHalo,
    StaticModel,
    ModelShadow,
    GroundShadow,
    TransparentModel,
    AnimatedModel,
    EffectModel,
    TransparentEffectModel,
    TransparentItemParticle,
    DenseItemParticle,
    ItemGlow,
    ItemParticleRgb,
    ItemParticleArgb,
    ItemParticleAlphaMask,
    ItemGlowRgb,
    ItemGlowArgb,
    ItemGlowAlphaMask,
    InventoryUi,
    ImGui
}
