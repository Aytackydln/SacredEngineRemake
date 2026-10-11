using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Four native FVF vertices and angle constants shared by the single-texture and per-segment ring helpers.</summary>
public sealed record SacredFxRingDefinition(IReadOnlyList<SacredFxSurfaceVertexLayout> Vertices, float FullAngle, double InitialAngle);
