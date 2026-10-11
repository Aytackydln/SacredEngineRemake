using System.Numerics;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Native eight-state strike strips, saved color/rate variants and camera-facing flares.</summary>
public sealed record SacredStrikeSymbolFxDefinition(IReadOnlyList<string> Textures,
    IReadOnlyList<SacredFxSurfaceVertexLayout> SurfaceVertices, string FlareTexture, string? FinishTexture)
{
    public bool UsesElapsedRate { get; init; }
    public float InitialElapsedRate { get; init; } = 1;
    public float Lifetime { get; init; }
    public float FrameInterval { get; init; }
    public float FlareHalfSize { get; init; }
    public Vector3 FlareOffset { get; init; }
    public uint FlareColor { get; init; }
    public bool FlareSourceColorOnly { get; init; }
    public uint StartNotification { get; init; }
    public int LightStrength { get; init; }
    public uint FinishNotification { get; init; }
    public uint FinishArgument { get; init; }
    public bool UsesSavedColor { get; init; }
    public uint InitialColor { get; init; }
}
