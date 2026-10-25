namespace Sacred.GameplayHost.World;

/// <summary>Gold mode-16 notice: seven-second hold, then a one-second alpha countdown (723600).</summary>
public sealed class GameplayHudNotice(string text, uint font, long order, uint mode = 16, uint? color = null)
{
    public string Text { get; } = text;
    public uint Font { get; } = font;
    public long Order { get; } = order;
    public uint Mode { get; } = mode;
    public float Age { get; private set; }
    public float RemainingAlpha { get; private set; } = 1;
    public uint ColorArgb => ((color ?? Mode switch { 2 => 0xFF00A0FF, 3 => 0xFFFF6347, 4 => 0xFF00FF50, _ => 0xFFFFFFFF }) & 0xFFFFFF) |
        (uint)(Math.Clamp(RemainingAlpha, 0, 1) * 255) << 24;
    public bool Expired => Age > 7 && RemainingAlpha <= 0;
    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        (Age, RemainingAlpha) = AdvanceState(Age, RemainingAlpha, seconds);
    }
    public static (float Age, float Alpha) AdvanceState(float age, float alpha, float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        age += seconds;
        // Native subtracts the full tick after crossing the threshold, not only its excess.
        if (age > 7) alpha -= seconds;
        return (age, alpha);
    }
}
