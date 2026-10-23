namespace Sacred.GameplayHost.World;

/// <summary>Gold cDialogDamageEntry 0x41E520: per-tick integer alpha loss, independent of the source actor.</summary>
public sealed class GameplayWorldText
{
    public GameplayWorldText(uint textId, int x, int y, uint colorArgb)
        => (TextId, X, Y, ColorArgb) = (textId, x, y, colorArgb);
    public uint TextId { get; }
    public int X { get; }
    public int Y { get; }
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
