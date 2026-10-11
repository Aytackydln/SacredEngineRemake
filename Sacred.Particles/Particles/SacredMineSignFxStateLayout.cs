using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold +0x20A0: the serializer at 0x7BAA80 writes all 0x498 bytes.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredMineSignFxStateLayout
{
    public const int SerializedSize = 0x498;
    [FieldOffset(0x000)] public float ElapsedTime;
    [FieldOffset(0x004)] public byte Active;
    [FieldOffset(0x005), BinaryUnknown] public byte Unknown005;
    [FieldOffset(0x006), BinaryUnknown] public ushort Unknown006;
    [FieldOffset(0x008)] public uint RemainingMines;
    [FieldOffset(0x00C)] public uint InitialMines;
    [FieldOffset(0x010)] public float BirthCarry;
    [FieldOffset(0x014)] public SacredParticleColorTableLayout Colors;
    [FieldOffset(0x414)] public SacredParticleMotionLayout Motion;
    [FieldOffset(0x434)] public SacredParticleEmissionLayout Emission;
}
