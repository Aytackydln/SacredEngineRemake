using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>0x48C-byte block serialized from cParticleSystem_dwarfmagic +0x20A0
/// by 0x78A420. Framing is UInt32 length, this block, 0xDEADC0DE, then base state.
/// Texture handles are outside this serialized block. Gaps remain unmapped.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredDwarfMagicParticleStateLayout
{
    public const int SerializedSize = 0x48C;
    [FieldOffset(0x000)] public readonly byte Active;
    [FieldOffset(0x001), BinaryUnknown] public readonly byte Unknown001;
    [FieldOffset(0x002), BinaryUnknown] public readonly ushort Unknown002;
    [FieldOffset(0x004)] public readonly float ElapsedTime;
    [FieldOffset(0x008)] public readonly SacredParticleColorTableLayout Colors;
    [FieldOffset(0x408)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x428)] public readonly SacredParticleEmissionLayout Emission;
}
