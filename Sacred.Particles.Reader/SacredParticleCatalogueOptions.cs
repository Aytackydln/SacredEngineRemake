namespace Sacred.Particles.Reader;

public sealed record SacredParticleCatalogueOptions
{
    public SacredParticleQuality Quality { get; init; } = SacredParticleQuality.High;
    /// <summary>Optional progress/diagnostic sink. The library otherwise performs no console or file writes.</summary>
    public Action<string>? Log { get; init; }
}
