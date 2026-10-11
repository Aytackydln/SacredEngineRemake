using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Native Door Spike model, saved geometry, orientation and release control.</summary>
public sealed record SacredDoorSpikeFxDefinition(string ModelName, string SurfaceTexture)
{
    public SacredDoorSpikeFxStateLayout InitialState { get; init; }
    public int DrawCount { get; init; }
    public float EvenHeading { get; init; }
    public float OddHeading { get; init; }
    public float Tilt { get; init; }
    public ushort ReleaseFlag { get; init; }
    public bool HasShadowPass { get; init; }
}
