using Sacred.Particles.Generated;

namespace Sacred.Particles;

/// <summary>Original cGranny ground-symbol bindings and special-ring draw parameters.</summary>
public sealed record SacredCreatureMarkerDefinition(string HealthTexture, string BorderTexture,
    string ChampionTexture, string EliteTexture, float SpecialRadiusScale, float RotationPerMillisecond)
{
    public static SacredCreatureMarkerDefinition Original => EmbeddedCreatureMarkers.Definition;
}
