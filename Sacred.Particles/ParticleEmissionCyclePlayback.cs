namespace Sacred.Particles;

/// <summary>Independent reproducible CRT random stream for native timer choices.</summary>
public sealed class ParticleEmissionCyclePlayback
{
    private readonly SacredParticleEmissionCycle _definition;
    public ParticleEmissionCyclePlayback(SacredParticleEmissionCycle definition, uint seed)
    {
        _definition = definition; RandomState = seed;
        Countdown = Delay(definition.InitialDelayMinimum, definition.InitialDelayWidth);
    }
    public float Countdown { get; private set; }
    public uint RandomState { get; private set; }
    public bool Burst { get; private set; }
    public void Advance(float dt)
    {
        Countdown -= dt;
        if (Countdown < 0)
            Countdown = Delay(_definition.RepeatDelayMinimum, _definition.RepeatDelayWidth);
        else Burst = Countdown < _definition.BurstThreshold;
    }
    public void Restore(float countdown, uint randomState, bool burst)
    {
        if (!float.IsFinite(countdown)) throw new ArgumentOutOfRangeException(nameof(countdown));
        Countdown = countdown; RandomState = randomState; Burst = burst;
    }
    private float Delay(float minimum, float width)
    {
        RandomState = unchecked(RandomState * 214013u + 2531011u);
        // Native x87 retains precision until its single store to the countdown field.
        return (float)(((RandomState >> 16) & 32767) * (double)_definition.RandomScale * width + minimum);
    }
}
