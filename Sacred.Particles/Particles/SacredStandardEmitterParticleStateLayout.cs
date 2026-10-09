using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>State at Gold object +0x20A0, serialized as 0x48C bytes by Arac Attack (0x78E760),
/// Laser Beam Impact (0x7A66F0) and Bat Signal (0x7A5730). Texture handles follow the payload.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredStandardEmitterParticleStateLayout
{
    public const int SerializedSize = 0x48C;
    [FieldOffset(0x000)] public readonly float ElapsedTime;
    [FieldOffset(0x004)] public readonly byte Active;
    [FieldOffset(0x005), BinaryUnknown] public readonly byte Unknown005;
    [FieldOffset(0x006), BinaryUnknown] public readonly ushort Unknown006;
    [FieldOffset(0x008)] public readonly SacredParticleColorTableLayout Colors;
    [FieldOffset(0x408)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x428)] public readonly SacredParticleEmissionLayout Emission;
}
