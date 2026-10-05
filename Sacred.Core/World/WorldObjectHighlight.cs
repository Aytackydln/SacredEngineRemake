namespace Sacred.Core.World;

/// <summary>Demo renderObjects 0x5A0754..0x5A07BD and Gold 0x631B7D..0x631BE6:
/// focused instance flag 0x20000000, triangular lighting pulse.</summary>
public static class WorldObjectHighlight
{
    public const int PeriodMilliseconds = 400;
    public const float PeriodSeconds = PeriodMilliseconds / 1000.0f;
    public const float UnfocusedIntensity = 0.8f;

    public static float Intensity(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds)) return UnfocusedIntensity;
        var milliseconds = Math.Floor(elapsedSeconds * 1000);
        if (!double.IsFinite(milliseconds)) return UnfocusedIntensity;
        var phase = ((milliseconds % PeriodMilliseconds) + PeriodMilliseconds) % PeriodMilliseconds;
        return (float)(Math.Abs(0.8 - phase * 0.004) + 0.2);
    }
}
