using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold +20A0, serialized as 0x9C bytes at 0x79C020. Resource handles follow the payload.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredNatureHealingParticleStateLayout
{
    public const int SerializedSize = 0x9C;
    [FieldOffset(0x00)] public readonly float ElapsedTime;
    [FieldOffset(0x04)] public readonly byte Active;
    [FieldOffset(0x05), BinaryUnknown] public readonly byte Unknown005;
    [FieldOffset(0x06), BinaryUnknown] public readonly ushort Unknown006;
    [FieldOffset(0x08)] public readonly uint UpperLeftColor;
    [FieldOffset(0x0C)] public readonly uint LowerLeftColor;
    [FieldOffset(0x10)] public readonly uint UpperRightColor;
    [FieldOffset(0x14)] public readonly uint LowerRightColor;
    [FieldOffset(0x18)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x38)] public readonly SacredParticleEmissionLayout Emission;
}
