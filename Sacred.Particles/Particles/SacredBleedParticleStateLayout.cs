using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold Bleed serializer 76B280 writes this payload from object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredBleedParticleStateLayout
{
    public const int SerializedSize = 0xA0;
    [FieldOffset(0x00)] public readonly float ElapsedTime;
    [FieldOffset(0x04)] public readonly byte Active;
    [FieldOffset(0x05), BinaryUnknown] public readonly byte Unknown05;
    [FieldOffset(0x06), BinaryUnknown] public readonly ushort Unknown06;
    [FieldOffset(0x8)] public readonly uint CornerColor0;
    [FieldOffset(0xc)] public readonly uint CornerColor1;
    [FieldOffset(0x10)] public readonly uint CornerColor2;
    [FieldOffset(0x14)] public readonly uint CornerColor3;
    [FieldOffset(0x18)] public readonly uint BloodType;
    [FieldOffset(0x1C)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x3C)] public readonly SacredParticleEmissionLayout Emission;
}

