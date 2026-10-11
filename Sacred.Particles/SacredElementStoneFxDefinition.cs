using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Native model and direct-device surfaces; this family never creates standard particles.
/// ItemModelTypeId resolves the GRN name through Items.pak, as in Gold's TypeManager.</summary>
public sealed record SacredElementStoneFxDefinition(uint ItemModelTypeId, string ModelTexture,
    string BeamTexture, string FlickerTexture,
    IReadOnlyList<SacredFxSurfaceVertexLayout> BeamVertices,
    IReadOnlyList<SacredFxSurfaceVertexLayout> FlickerVertices,
    string GlowTexture, string CoreTexture)
{
    public float FullTurn { get; init; }
    public float ModelScale { get; init; }
    public float ModelHeight { get; init; }
    public float BeamHeight { get; init; }
    public float SecondBeamAngle { get; init; }
    public float ThirdBeamAngle { get; init; }
    public float RandomScale { get; init; }
    public float FlickerUWidth { get; init; }
    public float FlickerEdgeV { get; init; }
    public float GlowHalfSize { get; init; }
    public float GlowHeight { get; init; }
    public uint GlowColor { get; init; }
    public uint CoreColor { get; init; }
    public uint FlickerBottomColor { get; init; }
    public float CoreSizeAddend { get; init; }
    public float CoreHeight { get; init; }
    public bool GlowSourceColorOnly { get; init; }
    public bool CoreSourceColorOnly { get; init; }
    public bool ModelSourceColorOnly { get; init; }
    public bool SurfaceSourceColorOnly { get; init; }
}
