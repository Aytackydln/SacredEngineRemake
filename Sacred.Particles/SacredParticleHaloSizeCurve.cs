namespace Sacred.Particles;

/// <summary>Native cast-flare size over its event-clock window.</summary>
public sealed record SacredParticleHaloSizeCurve(float Start, float End, float Amplitude,
    SacredParticleHaloSizeCurveKind Kind = SacredParticleHaloSizeCurveKind.Sine)
{
    public float At(float age)
    {
        var phase = (age - (double)Start) / (End - (double)Start);
        return (float)(Amplitude * (Kind == SacredParticleHaloSizeCurveKind.Sine
            ? Math.Sin(phase * Math.PI) : 1 - phase * phase));
    }
}

public enum SacredParticleHaloSizeCurveKind { Sine, QuadraticDecay }
