using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Complete native saved block at object +20A0; unknown padding is retained.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x24)]
public struct SacredTransdimFxStateLayout
{
    [FieldOffset(0x00)] public float ElapsedTime;
    [FieldOffset(0x04)] public byte Active;
    [FieldOffset(0x05), BinaryUnknown] public byte Padding05;
    [FieldOffset(0x06), BinaryUnknown] public ushort Padding06;
    [FieldOffset(0x08)] public float OffsetX;
    [FieldOffset(0x0C)] public float OffsetY;
    [FieldOffset(0x10)] public float OffsetZ;
    [FieldOffset(0x14), BinaryUnknown] public uint Unknown14;
    [FieldOffset(0x18)] public float Angle;
    [FieldOffset(0x1C)] public float CycleTime;
    [FieldOffset(0x20)] public float Lifetime;
}
