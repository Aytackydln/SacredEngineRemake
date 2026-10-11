namespace Sacred.Particles;

/// <summary>Native repeated count-based bursts with a randomized delay and centre.</summary>
public sealed record SacredParticleRandomBurst(float DelayMinimum, float DelayWidth,
    float Radius, float VerticalHalfRange, uint NativeAddress);
