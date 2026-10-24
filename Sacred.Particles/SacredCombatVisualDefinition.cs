using System.Numerics;
using Sacred.Core.CombatArts;
using Sacred.Particles.Generated;

namespace Sacred.Particles;

/// <summary>Original CA factory output and attachment, extracted from the version-checked executable.
/// An empty Bone means a fixed ground target. Particle geometry and textures remain
/// in the particle catalogue and Items.pak.</summary>
public sealed record SacredCombatVisualDefinition(SacredCombatArtKind Kind, uint Code, uint Resource,
    uint FxSubtype, string Bone, bool TargetIsCaster, float ReleaseProgress,
    ushort DurationBase, ushort DurationPerLevel, ushort DurationCap, uint NativeAddress)
{
    /// <summary>Native factory substitutes the caster when object lookup for the destination fails.</summary>
    public bool FallbackToCaster { get; init; }
    public float? FixedDuration { get; init; }
    public string? Diagnostic { get; init; }
    public Vector3 Direction { get; init; }
    public float Duration(int rank) => FixedDuration ??
        (DurationCap == 0 ? unchecked((ushort)(DurationBase + DurationPerLevel * rank))
            : Math.Min(DurationCap, unchecked((ushort)(DurationBase + DurationPerLevel * rank))));
}

public static class SacredCombatVisualCatalogue
{
    public static IReadOnlyList<SacredCombatVisualDefinition> Definitions => EmbeddedCombatVisuals.Definitions;
    public static IEnumerable<SacredCombatVisualDefinition> Find(SacredCombatArtKind kind, uint code) =>
        Definitions.Where(definition => definition.Kind == kind && definition.Code == code);
}
