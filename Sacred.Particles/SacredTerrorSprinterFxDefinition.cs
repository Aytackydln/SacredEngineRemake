using Sacred.Particles.Particles;

namespace Sacred.Particles;

public sealed record SacredTerrorSprinterFxDefinition(SacredFxFlareLayer GroundFlare,
    IReadOnlyList<string> SegmentTextureVariants, SacredFxRingDefinition Ring)
{
    public SacredTerrorSprinterFxStateLayout InitialState { get; init; }
    public float WeightRate { get; init; }
    public float RedBlueScale { get; init; }
    public float GreenScale { get; init; }
    public float RingRadius { get; init; }
    public float RingHalfWidth { get; init; }
    public int SegmentCount { get; init; }
    public uint RingColor { get; init; }
    public bool RingSourceColorOnly { get; init; }
    public bool RingAdditive { get; init; }
    public uint InitialNotification { get; init; }
    public uint PendingCode { get; init; }
    public uint TextureCommand { get; init; }
}
