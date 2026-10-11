namespace Sacred.Particles;

/// <summary>Related regeneration and light controllers sharing layered stdLensflare ground surfaces.</summary>
public sealed record SacredLayeredFlareFxDefinition(IReadOnlyList<SacredFxFlareLayer> Layers)
{
    public float InitialLifetime { get; init; }
    public uint InitialAuxiliary { get; init; }
    public float DurationScale { get; init; }
    public uint StartNotification { get; init; }
    public bool CanExtendDuration { get; init; }
}
