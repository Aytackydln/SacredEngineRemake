using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Four native models sharing two texture passes, twenty ribbons and one glow.</summary>
public sealed record SacredUnderworldGlyphFxDefinition(string ModelName, string ModelTexture,
    string RibbonTexture, IReadOnlyList<SacredFxSurfaceVertexLayout> RibbonVertices, string GlowTexture)
{
    public uint Element { get; init; }
    public float FullTurn { get; init; }
    public float RandomScale { get; init; }
    public float WaveRateOrigin { get; init; }
    public float WaveRateWidth { get; init; }
    public float WaveRadiusOrigin { get; init; }
    public float WaveRadiusWidth { get; init; }
    public float PointPhaseStep { get; init; }
    public float HalfTurn { get; init; }
    public float WaveRadiusScale { get; init; }
    public int WaveHeightOrigin { get; init; }
    public float ModelScale { get; init; }
    public float ModelHeight { get; init; }
    public uint ModelColor { get; init; }
    public bool ModelSourceColorOnly { get; init; }
    public float FirstUScrollRate { get; init; }
    public float FirstVScrollRate { get; init; }
    public float SecondUScrollRate { get; init; }
    public float SecondVScrollRate { get; init; }
    public float RibbonHeight { get; init; }
    public float RibbonSideY { get; init; }
    public float RibbonSideZ { get; init; }
    public float RibbonNormalMinimum { get; init; }
    public float RibbonHalfWidth { get; init; }
    public float InitialThickness { get; init; }
    public bool RibbonSourceColorOnly { get; init; }
    public float GlowHalfSize { get; init; }
    public float GlowHeight { get; init; }
    public uint GlowColor { get; init; }
    public bool GlowSourceColorOnly { get; init; }
}
