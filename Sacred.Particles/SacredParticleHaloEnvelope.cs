namespace Sacred.Particles;

/// <summary>Rounded native flare factor, including its update-boundary rise and fall.</summary>
public sealed record SacredParticleHaloEnvelope(float InitialValue, float RiseRate,
    float MaximumValue, float? FadeStartSeconds, float FallRate, bool ScaleRgb)
{
    public float Advance(float value, float age, float step)
    {
        if (RiseRate != 0) value = value < MaximumValue
            ? (float)(value + step * (double)RiseRate) : MaximumValue;
        if (FadeStartSeconds is { } start && age > start)
            value = Math.Max(0, (float)(value - step * (double)FallRate));
        return value;
    }
}
