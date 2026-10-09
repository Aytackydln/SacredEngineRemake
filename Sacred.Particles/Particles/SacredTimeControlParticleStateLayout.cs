using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold +20A0, serialized as 0x490 bytes at 0x79DE80. Duration is event +38 in milliseconds.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredTimeControlParticleStateLayout
{
    public const int SerializedSize = 0x490;
    [FieldOffset(0x000)] public readonly float ElapsedTime;
    [FieldOffset(0x004)] public readonly byte Active;
    [FieldOffset(0x005), BinaryUnknown] public readonly byte Unknown005;
    [FieldOffset(0x006), BinaryUnknown] public readonly ushort Unknown006;
    [FieldOffset(0x008)] public readonly SacredParticleColorTableLayout Colors;
    [FieldOffset(0x408)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x428)] public readonly SacredParticleEmissionLayout Emission;
    [FieldOffset(0x48C)] public readonly float DurationSeconds;
}
