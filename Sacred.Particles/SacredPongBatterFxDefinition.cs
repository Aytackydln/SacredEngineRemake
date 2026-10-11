using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Both Pong paddle variants share the same box mesh and twelve glow lines.</summary>
public sealed record SacredPongBatterFxDefinition(string SurfaceTexture, string EdgeTexture,
    IReadOnlyList<SacredFxSurfaceVertexLayout> SurfaceVertices, IReadOnlyList<SacredFxBeamSegment> Edges)
{
    public bool ComputerControlled { get; init; }
    public float TextureScale { get; init; }
    public float UScrollRate { get; init; }
    public float VScrollRate { get; init; }
    public float EdgeSizeOrigin { get; init; }
    public float EdgeSizeWave { get; init; }
    public float EdgeSizeHit { get; init; }
    public float EdgeDensityOrigin { get; init; }
    public float EdgeDensityHit { get; init; }
    public float SurfaceColorOrigin { get; init; }
    public float SurfaceColorWave { get; init; }
    public float HitFlashReset { get; init; }
    public uint EdgeColor { get; init; }
}
