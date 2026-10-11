using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Native five-member Forge Element model, ground flares and cylindrical strip.</summary>
public sealed record SacredForgeElementFxDefinition(string ModelName, string ModelTexture,
    string RingTexture, string RotatingFlareTexture, string GroundFlareTexture,
    IReadOnlyList<SacredFxSurfaceVertexLayout> RingVertices, SacredForgeElementFxStateLayout InitialState)
{
    public float GrowthRate { get; init; }
    public float MaximumGrowthFactor { get; init; }
    public float CompressionRate { get; init; }
    public float MinimumCompressionFactor { get; init; }
    public float FadeRate { get; init; }
    public float ScrollRate { get; init; }
    public float ScrollWrap { get; init; }
    public float FlareHalfSize { get; init; }
    public float FlareHeight { get; init; }
    public float FullTurn { get; init; }
    public float HalfTurn { get; init; }
    public float FlareAngleBias { get; init; }
    public float ModelBobRate { get; init; }
    public float ModelBobAmplitude { get; init; }
    public float ModelHeight { get; init; }
    public float RingHeight { get; init; }
    public int TrailStartBrightness { get; init; }
    public int TrailBrightnessStep { get; init; }
    public float ChildDelay { get; init; }
    public float ChildFrameRate { get; init; }
    public float ChildLimit { get; init; }
    public uint ChildTypeId { get; init; }
    public bool ModelSourceColorOnly { get; init; }
}
