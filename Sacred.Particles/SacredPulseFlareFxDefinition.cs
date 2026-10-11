namespace Sacred.Particles;

public enum SacredPulseFlareKind { Transdim, PongBall, Zhadum }

/// <summary>Native ground/billboard passes and their decoded pulse, reset and hit controllers.</summary>
public sealed record SacredPulseFlareFxDefinition(SacredPulseFlareKind Kind, IReadOnlyList<SacredFxFlareLayer> Layers)
{
    public float Lifetime { get; init; }
    public float ResetInterval { get; init; }
    public float RandomScale { get; init; }
    public float PositionRange { get; init; }
    public float PositionBias { get; init; }
    public float FullAngle { get; init; }
    public int PlaneCount { get; init; }
    public float PlaneStep { get; init; }
    public float SpreadScale { get; init; }
    public double AlphaBias { get; init; }
    public double AlphaScale { get; init; }
    public uint FlashAlpha { get; init; }
    public float FadeRate { get; init; }
    public float InitialTimer { get; init; }
    public float ResetTimer { get; init; }
    public float HitTimer { get; init; }
    public uint InitialSpeed { get; init; }
    public uint SpeedIncrement { get; init; }
    public uint SpeedLimit { get; init; }
    public uint ResumeNotification { get; init; }
    public uint InitialNotification { get; init; }
    public ushort HitFlag { get; init; }
    public float RadiusFrequency { get; init; }
    public float RadiusAmplitude { get; init; }
    public float RadiusBias { get; init; }
}
