using Sacred.Particles.Particles;

namespace Sacred.Particles;

public sealed record SacredBubbleTrapExplosionFxDefinition(string TextureName, SacredFxRingDefinition Ring)
{
    public SacredBubbleTrapExplosionFxStateLayout InitialState { get; init; }
    public float Lifetime { get; init; }
    public float RadiusRate { get; init; }
    public float RingWidth { get; init; }
    public float WeightRate { get; init; }
    public float RedBlueScale { get; init; }
    public float GreenScale { get; init; }
    public int SegmentCount { get; init; }
    public bool SourceColorOnly { get; init; }
    public bool Additive { get; init; }
    public uint InitialNotification { get; init; }
    public uint InitialLightStrength { get; init; }
}
