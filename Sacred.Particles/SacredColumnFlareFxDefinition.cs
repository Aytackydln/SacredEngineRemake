namespace Sacred.Particles;

/// <summary>Native grow/hold/fade controller with a rotating ground plane and repeated height slices.</summary>
public sealed record SacredColumnFlareFxDefinition(SacredFxFlareLayer Layer)
{
    public int SliceCount { get; init; }
    public float InitialLifetime { get; init; }
    public float RiseRate { get; init; }
    public float FallRate { get; init; }
    public float FadeLeadTime { get; init; }
    public float ColorFrequency { get; init; }
    public float ColorBias { get; init; }
    public float ColorAmplitude { get; init; }
    public float ColorOffset { get; init; }
    public float ColorScale { get; init; }
    public uint BlueChannel { get; init; }
    public float LightBlue { get; init; }
    public float GroundAlphaScale { get; init; }
    public float SliceAlphaScale { get; init; }
    public float SliceAlphaFalloff { get; init; }
    public float HeightSineBias { get; init; }
    public float HeightScale { get; init; }
    public uint InitialNotification { get; init; }
    public uint InitialPendingCode { get; init; }
}
