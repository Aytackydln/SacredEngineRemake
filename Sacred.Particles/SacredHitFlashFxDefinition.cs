namespace Sacred.Particles;

/// <summary>Dark SM Hit event 59, selector 416: a rotating, fading stdLensflare at the victim root.</summary>
public sealed record SacredHitFlashFxDefinition(IReadOnlyList<string> Textures, float HalfSize, float FadeRate,
    float AlphaScale)
{
    public bool Additive { get; init; }
    public bool SourceColorOnly { get; init; }
}

public sealed class SacredHitFlashFxPlayback(SacredHitFlashFxDefinition definition)
{
    public float Age { get; private set; }
    public float Weight { get; private set; } = 1;
    public bool Active { get; private set; } = true;
    public uint Color => ColorAt(Weight, definition.AlphaScale);
    public static uint ColorAt(float weight, float alphaScale) =>
        unchecked((uint)(int)(weight * (double)alphaScale) << 24) | 0xFFFFFF;

    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (!Active) return;
        (Age, Weight, Active) = AdvanceState(Age, Weight, seconds, definition.FadeRate);
    }

    /// <summary>One native update, also used to replay recorded controller states.</summary>
    public static (float Age, float Weight, bool Active) AdvanceState(float age, float weight, float seconds, float fadeRate)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        // Native x87 compares the subtraction before storing its rounded single precision value.
        var remaining = weight - (double)seconds * fadeRate;
        return (age + seconds, remaining < 0 ? 0 : (float)remaining, remaining >= 0);
    }
}
