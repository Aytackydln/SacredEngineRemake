using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Serializer 0x7AB880 writes 0x490 bytes from object +0x20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredReanimateParticleStateLayout
{
    public const int SerializedSize = 0x490;
    [FieldOffset(0)] public readonly float ElapsedTime;
    [FieldOffset(4)] public readonly byte Active;
    [FieldOffset(5), BinaryUnknown] public readonly byte Padding05;
    [FieldOffset(6), BinaryUnknown] public readonly ushort Padding06;
    [FieldOffset(8)] public readonly float OrbitAngle;
    [FieldOffset(0xC)] public readonly SacredParticleColorTableLayout Colors;
    [FieldOffset(0x40C)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x42C)] public readonly SacredParticleEmissionLayout Emission;
}
