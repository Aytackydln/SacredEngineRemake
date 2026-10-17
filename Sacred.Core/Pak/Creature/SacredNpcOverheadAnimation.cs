namespace Sacred.Core.Pak.Creature;

/// <summary>Gold 0x599FAB / 0x409761..0x4097BA / 0x409B34..0x409B79.
/// The base icon is followed by the same texture with an additive grayscale pulse.</summary>
public static class SacredNpcOverheadAnimation
{
    public static byte Glow(uint elapsedMilliseconds, uint objectReference)
    {
        // Native adds ref*100 with UInt32 wrap, zero-extends, then stores the phase
        // as float before x87 fsin. Its integer conversion truncates toward zero.
        var milliseconds = unchecked(elapsedMilliseconds + objectReference * 100);
        var phase = (float)(milliseconds * (double)0.005f);
        return (byte)((Math.Sin(phase) + 1) * 127);
    }
}
