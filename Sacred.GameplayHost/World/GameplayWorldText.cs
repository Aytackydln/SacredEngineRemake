namespace Sacred.GameplayHost.World;

/// <summary>Gold cDialogDamageEntry 0x41E520: per-tick integer alpha loss, independent of the source actor.</summary>
public sealed class GameplayWorldText
{
    public GameplayWorldText(uint textId, int x, int y, uint colorArgb, int digit = 0, uint group = 0)
        => (TextId, X, Y, ColorArgb, Digit, Group) = (textId, x, y, colorArgb, digit, group);
    public uint TextId { get; }
    public int X { get; }
    public int Y { get; }
    public int Digit { get; }
    public uint Group { get; }
    public uint Resource => TextId == uint.MaxValue ? 0 : TextId;
    public float VerticalOffset => -120 - (TextId == 0x3EC ? 20 : TextId == uint.MaxValue ? 80 : 0)
        - (Group > 1 ? unchecked((short)(Group * 90)) : 0) + VerticalTravel;
    public uint ColorArgb { get; private set; }
    public float AgeSeconds { get; private set; }
    public bool Expired { get; private set; }
    public float VerticalTravel => -25 * AgeSeconds;

    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (Expired) return;
        AgeSeconds += seconds;
        var value = (double)(ColorArgb >> 24) - (double)seconds * 60;
        var truncated = value < long.MinValue ? long.MinValue : (long)value;
        var alpha = unchecked((short)truncated);
        // The native helper truncates toward zero; zero alpha can remain valid for another tick.
        Expired = alpha < 0;
        ColorArgb = (ColorArgb & 0x00FFFFFF) | unchecked((uint)alpha << 24);
    }
}
