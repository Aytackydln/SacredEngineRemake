using Sacred.Particles.Generated;

namespace Sacred.Particles;

/// <summary>Gold 305's untextured five-vertex gold streaks (76C150–76C730).</summary>
public sealed record SacredDeflectionFxDefinition(int StreakCount, int TrailPoints, float TrailSpacing,
    float Speed, float Damping, float InitialLife, float LifeLoss, float AngularRange, float VerticalDirection,
    float DirectionSpread, float DirectionBias, float RandomScale, IReadOnlyList<uint> Colors)
{
    /// <summary>Creation event selects the native neck/root wearer attachment.</summary>
    public uint AttachmentSubtype { get; init; }
    public static SacredDeflectionFxDefinition Original => EmbeddedDeflectionFx.Definition;
}
