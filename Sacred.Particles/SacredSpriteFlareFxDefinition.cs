namespace Sacred.Particles;

/// <summary>Native billboard layers with either an asymmetric radius pulse or three grayscale oscillations.</summary>
public sealed record SacredSpriteFlareFxDefinition(IReadOnlyList<SacredFxFlareLayer> Layers)
{
    public SacredFxFlareLayer? GroundFlare { get; init; }
    public bool OscillatesColors { get; init; }
    public float Lifetime { get; init; }
    public float ColorFrequency { get; init; }
    public float ColorAmplitude { get; init; }
    public uint ColorBase { get; init; }
    public uint ComplementIntensity { get; init; }
    public float PulseThreshold { get; init; }
    public float PulseRiseScale { get; init; }
    public float PulseFallScale { get; init; }
    public float PulseAmplitude { get; init; }
    public ushort RemovalFlag { get; init; }
    public uint InitialNotification { get; init; }
    public uint InitialLightStrength { get; init; }
    public uint InitialAuxiliary { get; init; }
}
