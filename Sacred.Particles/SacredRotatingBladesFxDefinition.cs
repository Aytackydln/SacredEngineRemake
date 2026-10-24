using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Gold holywall: a ground ring, two rising helices and two orbiting spark tips.</summary>
public sealed record SacredRotatingBladesFxDefinition(IReadOnlyList<SacredFxSurfaceVertexLayout> TrailVertices,
    string GroundTexture, string TrailTexture, string TipTexture,
    float RotationRate, float RotationWrap, float RiseRate, float MaximumHeight,
    float RingAngleStep, float SampleStep, float HelixAngleScale, float TipAngle,
    float GroundMargin, float TipSizeBase, float TipSizeRandomScale)
{
    public bool GroundAdditive { get; init; }
    public bool GroundSourceColorOnly { get; init; }
    public bool TrailSourceColorOnly { get; init; }
    public float DefaultActorRadius { get; init; }
    public int RingSamples { get; init; }
    public int HelixSamples { get; init; }
    public uint GroundColor { get; init; }
}
