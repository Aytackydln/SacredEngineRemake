using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Both native updates consume bit 0x0008 at base object +0x74 and retain the other bits.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredRingShieldFxHitFlagsLayout
{
    public const int SerializedSize = 0x76;
    public const ushort HitMask = 0x0008;
    [FieldOffset(0x74)] public ushort Flags;
}
