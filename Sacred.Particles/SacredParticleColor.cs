namespace Sacred.Particles;

/// <summary>Gold stdCreation packed colour at 0x7658B9..0x765970. Channels truncate;
/// the original shifts/ORs do not clamp values to bytes before packing.</summary>
public static class SacredParticleColor
{
    public static uint Randomize(uint center, uint halfRanges, Func<double> nextUnit)
    {
        if (halfRanges == 0) return center;
        uint packed = 0;
        for (var shift = 24; shift >= 0; shift -= 8)
        {
            var channel = (center >> shift) & 255;
            var width = (halfRanges >> shift) & 255;
            var sample = channel + ((float)nextUnit() * 2 - 1) * width;
            packed = (packed << 8) | unchecked((uint)(int)sample);
        }
        return packed;
    }
}
