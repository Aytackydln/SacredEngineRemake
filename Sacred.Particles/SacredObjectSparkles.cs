using System.Numerics;

namespace Sacred.Particles;

/// <summary>Gold cGranny world sparkle draw (0x408B70); no cObjectFX or particle-system resource.</summary>
public static class SacredObjectSparkles
{
    public const string OuterTexture = "PARTICLE_SPARK04.TGA";
    public const string InnerTexture = "PARTICLE_SPARK07.TGA";
    public const float OuterHalfSize = 4;

    // Original float table at 0x8E808C, shared by every object and every quality.
    private static ReadOnlySpan<float> Frequencies =>
    [1.3452f, 6.6458f, 4.6646f, 2.7472f, 6.4337f, 4.5351f, 1.7371f, .9962f,
     2.5993f, 3.9645f, 2.3992f, 1.7379f, 2.8925f, .0085f, 6.7786f, 5.5555f];

    public static int Count(SacredParticleQuality quality) => quality switch
    {
        SacredParticleQuality.Low => 3,
        SacredParticleQuality.Medium => 5,
        _ => 8
    };

    /// <summary>The native clock is integer milliseconds, with reference * 100 added before scaling.</summary>
    public static (Vector3 Offset, float InnerHalfSize, uint OuterColor) Evaluate(uint reference, ulong milliseconds, int index)
    {
        var t = unchecked((uint)milliseconds + reference * 100) * .0005f;
        var i = index + 1;
        var z = Sine(t, i + 4, i + 6);
        return (new(
            (Sine(t, i - 5, i) + 1) * Sine(t, i - 6, i - 7) * 5,
            (Sine(t, i + 5, i + 1) + 1) * Sine(t, i + 7, i - 8) * 5,
            (Sine(t, i + 2, i + 3) + 1) * z * 5), z + 2,
            Hue(Frequencies[(i + 4) & 15] - t));
    }

    private static float Sine(float time, int frequency, int phase) =>
        MathF.Sin((float)((double)time * Frequencies[frequency & 15] + Frequencies[phase & 15]));

    private static uint Hue(float value)
    {
        // Gold 0x642420: six sectors, brightness 125, truncating each channel.
        var hue = (double)(value - MathF.Floor(value)) * 5.9999f;
        var sector = (int)hue;
        var fraction = (float)(hue - sector);
        var rising = (uint)((double)fraction * 125);
        var falling = (uint)((double)(1 - fraction) * 125);
        return 0xff000000 | (sector switch
        {
            0 => rising << 8 | 125,
            1 => 125 << 8 | falling,
            2 => (rising << 8 | 125) << 8,
            3 => (125 << 8 | falling) << 8,
            4 => 125 << 16 | rising,
            _ => falling << 16 | 125
        });
    }
}
