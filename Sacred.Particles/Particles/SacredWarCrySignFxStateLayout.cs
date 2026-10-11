using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>War Cry Sign saves 0x20 bytes beginning at object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredWarCrySignFxStateLayout
{
    public const int SerializedSize = 0x20;
    [FieldOffset(0x00)] public float ElapsedTime;
    [FieldOffset(0x04)] public byte Active;
    [FieldOffset(0x05), BinaryUnknown] public byte Unknown005;
    [FieldOffset(0x06), BinaryUnknown] public ushort Unknown006;
    [FieldOffset(0x08)] public float Lifetime;
    [FieldOffset(0x0C)] public float Weight;
    [FieldOffset(0x10)] public byte Phase;
    [FieldOffset(0x11), BinaryUnknown] public byte Unknown011;
    [FieldOffset(0x12), BinaryUnknown] public ushort Unknown012;
    [FieldOffset(0x14)] public float MinimumReach;
    [FieldOffset(0x18)] public float MaximumReach;
    [FieldOffset(0x1C)] public float Sweep;
}
