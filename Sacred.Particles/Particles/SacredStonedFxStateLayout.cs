using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold 0x7861F0 saves 0x14 bytes beginning at object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredStonedFxStateLayout
{
    public const int SerializedSize = 0x14;
    [FieldOffset(0x00)] public float ElapsedTime;
    [FieldOffset(0x04)] public byte Active;
    [FieldOffset(0x05), BinaryUnknown] public byte Unknown005;
    [FieldOffset(0x06), BinaryUnknown] public ushort Unknown006;
    [FieldOffset(0x08)] public float Duration;
    [FieldOffset(0x0C), BinaryUnknown] public uint Unknown00C;
    [FieldOffset(0x10)] public float Heading;
}
