using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold High Speed serializer 79A9C0 writes this payload from object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredHighSpeedParticleStateLayout
{
    public const int SerializedSize = 0x498;
    [FieldOffset(0x000)] public readonly float ElapsedTime;
    [FieldOffset(0x004)] public readonly byte Active;
    [FieldOffset(0x005), BinaryUnknown] public readonly byte Unknown005;
    [FieldOffset(0x006), BinaryUnknown] public readonly ushort Unknown006;
    [FieldOffset(0x008)] public readonly SacredParticleColorTableLayout Colors;
    [FieldOffset(0x408)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x428)] public readonly SacredParticleEmissionLayout Emission;
    [FieldOffset(0x48C)] public readonly float BirthSizeFactor;
    [FieldOffset(0x490)] public readonly float Lifetime;
    [FieldOffset(0x494)] public readonly byte Alternate;
    [FieldOffset(0x495), BinaryUnknown] public readonly byte Unknown495;
    [FieldOffset(0x496), BinaryUnknown] public readonly ushort Unknown496;
}

