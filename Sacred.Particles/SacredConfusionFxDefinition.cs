using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Two native lensflares and the complete crossed helical ribbon helper.</summary>
public sealed record SacredConfusionFxDefinition(IReadOnlyList<SacredFxFlareLayer> GroundLayers,
    string RibbonTexture, IReadOnlyList<SacredFxSurfaceVertexLayout> RibbonVertices,
    SacredConfusionFxStateLayout InitialState)
{
    public float PhaseRate { get; init; }
    public float PhaseWrap { get; init; }
    public float AlphaAngleScale { get; init; }
    public float AlphaScale { get; init; }
    public float RandomScale { get; init; }
    public float RandomAngleScale { get; init; }
    public float RibbonTimeScale { get; init; }
    public float MaximumGroundHeight { get; init; }
    public float ResetGroundHeight { get; init; }
    public uint StartNotification { get; init; }
}
