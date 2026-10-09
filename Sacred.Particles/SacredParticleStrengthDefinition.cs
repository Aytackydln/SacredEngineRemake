namespace Sacred.Particles;

/// <summary>Native event-strength expression; coefficients are recovered from the initializer.</summary>
public sealed record SacredParticleStrengthDefinition(float InputScale, float SizeAddend, float SizeMultiplier,
    float GrowthMultiplier, float GrowthAddend, uint NativeAddress);
