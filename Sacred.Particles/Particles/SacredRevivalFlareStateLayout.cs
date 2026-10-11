using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Live Undead transfers 0x14 bytes from object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredRevivalFlareStateLayout
{
    public const int SerializedSize = 0x14;
    [FieldOffset(0)] public float ElapsedTime;
    [FieldOffset(4)] public byte Active;
    [FieldOffset(5), BinaryUnknown] public byte Unknown005;
    [FieldOffset(6), BinaryUnknown] public ushort Unknown006;
    [FieldOffset(8)] public float Angle;
    [FieldOffset(12)] public float Opacity;
    [FieldOffset(16)] public byte Rising;
    [FieldOffset(17), BinaryUnknown] public byte Unknown011;
    [FieldOffset(18), BinaryUnknown] public ushort Unknown012;
}
