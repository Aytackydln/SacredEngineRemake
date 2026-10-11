namespace Sacred.Particles;

public enum SacredControlledFlareKind { Revival, DeathCall, ToggleTrap }

/// <summary>Decoded controllers using the complete stdLensflare surface path.</summary>
public sealed record SacredControlledFlareFxDefinition(SacredControlledFlareKind Kind,
    IReadOnlyList<SacredFxFlareLayer> Layers)
{
    public float RiseRate { get; init; }
    public float FallRate { get; init; }
    public float RotationRate { get; init; }
    public float FullAngle { get; init; }
    public float AlphaScale { get; init; }
    public float InitialWeight { get; init; }
    public float InitialRadius { get; init; }
    public float RadiusScale { get; init; }
    public float LayerTimeIncrement { get; init; }
    public float RotationNumerator { get; init; }
    public float RadiusAmplitude { get; init; }
    public int AtlasSide { get; init; } = 1;
    public int InitialAtlasCell { get; init; }
    public ushort ToggleFlag { get; init; }
    public uint InitialNotification { get; init; }
    public uint ToggleNotification { get; init; }
}
