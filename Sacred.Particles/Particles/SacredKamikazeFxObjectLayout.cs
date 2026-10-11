using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x253C)]
public struct SacredKamikazeFxObjectLayout
{
    [FieldOffset(0)] public SacredExpandingBurstFxObjectLayout Common;
    /// <summary>Unsigned event +0x44 copied by initialization; ring radius is rate times stored elapsed time.</summary>
    [FieldOffset(0x2538)] public uint RadiusRate;
}
