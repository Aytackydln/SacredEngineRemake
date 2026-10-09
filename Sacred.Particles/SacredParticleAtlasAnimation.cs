namespace Sacred.Particles;

/// <summary>Native lensflare frame selection: reverse introduction, held frame, masked outro.</summary>
public sealed record SacredParticleAtlasAnimation(float IntroductionEnd, float HoldEnd, float End,
    float OutroOrigin, float FramesPerSecond, int FrameMask)
{
    public int? FrameAt(float age)
    {
        if (age > End) return null;
        if (age <= IntroductionEnd) return (int)((IntroductionEnd - (double)age) * FramesPerSecond) & FrameMask;
        if (age <= HoldEnd) return 0;
        return (int)((age - (double)OutroOrigin) * FramesPerSecond) & FrameMask;
    }
}
