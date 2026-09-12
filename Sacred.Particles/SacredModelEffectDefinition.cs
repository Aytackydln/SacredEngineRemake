using Sacred.Particles.Generated;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

public enum SacredModelEffectKind { Torch, Worms, Whip, Streak, Beam, MagicWeapon, ElementalFire, ElementalPoison }

/// <summary>The model-driven billboard drawn at every consecutive stdfx_bone helper.</summary>
public sealed record SacredStandardModelEffectDefinition(
    uint NativeAddress,
    string TextureName,
    uint Color,
    float HalfSize,
    int MaximumAnchorCount);

/// <summary>Model attachment rules and constants recovered from the verified executable.
/// Item IDs are generated from native predicates, never inferred from resource names.</summary>
public sealed record SacredModelEffectDefinition(
    SacredModelEffectKind Kind, IReadOnlyList<uint> ItemIds, uint PredicateAddress,
    string TextureName, SacredParticleEmissionLayout Emission, SacredParticleMotionLayout Motion,
    uint Color, float HalfSize, int PointCount, float LinkLength, float Inertia,
    float DirectionWeight, float DirectionWeightStep, float StepsPerSecond)
{
    public IReadOnlyList<uint> CornerColors { get; init; } = [];
    public IReadOnlyList<uint> HaloCornerColors { get; init; } = [];
    public uint ParticleColor { get; init; }
    /// <summary>stdRender arguments recovered from the model effect draw routine.</summary>
    public int ParticleAtlasSide { get; init; } = 1;
    public uint ParticleDrawFlags { get; init; }
    /// <summary>Texture selected by the native <c>stdLensflare</c> draw, when present.</summary>
    public string? LensFlareTextureName { get; init; }
    public float HaloHalfSize { get; init; }
    public uint HaloColor { get; init; }
    public float BeamDensity { get; init; }
    public float HaloDensity { get; init; }
    public bool UsesBaseItem { get; init; }
    public float Intensity { get; init; }
    public float EmissionSizeIntensityScale { get; init; }
    public float SizeChangeIntensityScale { get; init; }
    /// <summary>Optional native 256-entry energy colour table, indexed by the particle fade counter.</summary>
    public IReadOnlyList<uint> ParticleColors { get; init; } = [];
}

public static class SacredModelEffectCatalogue
{
    public static IReadOnlyList<SacredModelEffectDefinition> Definitions => EmbeddedModelEffects.Definitions;

    public static SacredStandardModelEffectDefinition StandardGlow =>
        EmbeddedModelEffects.StandardGlow;

    public static IReadOnlyList<SacredModelEffectDefinition> MagicWeaponDefinitions =>
        EmbeddedModelEffects.MagicWeaponDefinitions;

    public static SacredModelEffectDefinition? FindMagicWeapon(byte variant) =>
        variant is >= 1 and <= 5 ? MagicWeaponDefinitions[variant - 1] : null;

    public static SacredModelEffectDefinition? Find(uint itemId) =>
        Find(itemId, 0);

    public static SacredModelEffectDefinition? Find(uint itemId, uint baseItemId) =>
        Definitions.FirstOrDefault(definition => definition.ItemIds.Contains(itemId) ||
            (definition.UsesBaseItem && baseItemId != 0 && definition.ItemIds.Contains(baseItemId)));
}
