using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Original six-vertex fins, swept sector selection and grow/hold/fade constants.</summary>
public sealed record SacredWarCrySignFxDefinition(string StripTexture,
    IReadOnlyList<SacredFxSurfaceVertexLayout> StripVertices, SacredFxFlareLayer GroundFlare)
{
    public float InitialLifetime { get; init; }
    public float MinimumReach { get; init; }
    public float MaximumReach { get; init; }
    public float WeightRate { get; init; }
    public float SweepRate { get; init; }
    public float SweepPeriod { get; init; }
    public int SectorCount { get; init; }
    public float SectorStep { get; init; }
    public float SweepWidth { get; init; }
    public float ReachFalloff { get; init; }
    public float FullAngle { get; init; }
    public float OppositeAngle { get; init; }
    public uint StripColor { get; init; }
    public float HighlightThreshold { get; init; }
    public float HighlightAlphaScale { get; init; }
    public int HighlightPassCount { get; init; }
    public uint InitialNotification { get; init; }
}
