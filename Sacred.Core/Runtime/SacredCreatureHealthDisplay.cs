namespace Sacred.Core.Runtime;

/// <summary>Gold +20C presentation clock: hover 61258A, age 537ECD, alpha 599D42.</summary>
public static class SacredCreatureHealthDisplay
{
    public const float HoverSeconds = 1;
    public const float FadeSeconds = .5f;
    public static float Hover(float remaining) => MathF.Max(remaining, HoverSeconds);
    public static float Advance(float remaining, float seconds) =>
        remaining > seconds && remaining - seconds <= 100 ? remaining - seconds : 0;
    public static uint Alpha(float remaining) => remaining < FadeSeconds
        ? (uint)(MathF.Max(0, remaining) * 512) & 255 : 255;
}
