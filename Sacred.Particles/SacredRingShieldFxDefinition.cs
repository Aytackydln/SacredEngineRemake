using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Native authored circular strips and the separate Holy ground flare or Magic marker quads.</summary>
public sealed record SacredRingShieldFxDefinition(IReadOnlyList<SacredFxSurfaceVertexLayout> RingVertices,
    string SurfaceTexture, string SecondaryTexture)
{
    public SacredHolyShieldFxParameters? Holy { get; init; }
    public SacredMagicShieldFxParameters? Magic { get; init; }
    public IReadOnlyList<SacredFxSurfaceVertexLayout> MarkerVertices { get; init; } = [];
    public string? MarkerTexture { get; init; }
    public float AngleStep { get; init; }
    public float Radius { get; init; }
    public float InitialLifetime { get; init; }
    public uint StartNotification { get; init; }
    public int LightStrength { get; init; }
}
