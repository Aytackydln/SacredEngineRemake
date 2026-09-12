using Sacred.Particles.Generated;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

public enum SacredElementalWeaponEffectKind { Fire, Magic, Poison }

public sealed record SacredElementalWeaponSelectionDefinition(
    float PhysicalThresholdScale, float LowDamageReference, float HighDamageReference);

/// <summary>Damage selection, glow-line, and particle constants recovered from
/// cWeapon3D::toggleVisuals and cItemBase::renderItemEffects in Sacred.exe.</summary>
public sealed record SacredElementalWeaponEffectDefinition(
    SacredElementalWeaponEffectKind Kind,
    uint NativeAddress,
    uint ParticleTypeId,
    string TextureName,
    SacredParticleEmissionLayout Emission,
    SacredParticleMotionLayout Motion,
    uint ParticleColor,
    uint ParticleDrawFlags,
    int ParticleAtlasSide,
    uint GlowColor,
    float GlowHalfSizeScale,
    float GlowHalfSizeOffset,
    float GlowDensity,
    float IntensityLowScale,
    float IntensityHighScale,
    float EmissionSizeIntensityScale,
    float SizeChangeIntensityScale)
{
    public SacredModelEffectDefinition ParticleDefinition(float intensity) =>
        new(Kind == SacredElementalWeaponEffectKind.Fire
                ? SacredModelEffectKind.ElementalFire : SacredModelEffectKind.ElementalPoison,
            [], NativeAddress, TextureName, Emission, Motion, 0, 0, 0, 0, 0, 0, 0, 0)
        {
            ParticleColor = ParticleColor,
            ParticleDrawFlags = ParticleDrawFlags,
            ParticleAtlasSide = ParticleAtlasSide,
            Intensity = intensity,
            EmissionSizeIntensityScale = EmissionSizeIntensityScale,
            SizeChangeIntensityScale = SizeChangeIntensityScale
        };
}

public static class SacredElementalWeaponEffectCatalogue
{
    public static SacredElementalWeaponSelectionDefinition Selection =>
        EmbeddedModelEffects.ElementalSelection;

    public static IReadOnlyList<SacredElementalWeaponEffectDefinition> Definitions =>
        EmbeddedModelEffects.ElementalDefinitions;
}
