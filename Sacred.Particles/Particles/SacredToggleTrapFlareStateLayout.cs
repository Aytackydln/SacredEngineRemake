using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Trap01 transfers 0x14 bytes from object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredToggleTrapFlareStateLayout
{
    public const int SerializedSize = 0x14;
    [FieldOffset(0)] public float ElapsedTime;
    [FieldOffset(4)] public byte Active;
    [FieldOffset(5), BinaryUnknown] public byte Unknown005;
    [FieldOffset(6), BinaryUnknown] public ushort Unknown006;
    [FieldOffset(8)] public int AtlasCell;
    [FieldOffset(12)] public byte Enabled;
    [FieldOffset(13), BinaryUnknown] public byte Unknown00D;
    [FieldOffset(14), BinaryUnknown] public ushort Unknown00E;
    [FieldOffset(16)] public float Brightness;
}
