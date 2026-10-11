using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Call of Death transfers 0x10 bytes from object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredDeathCallFlareStateLayout
{
    public const int SerializedSize = 0x10;
    [FieldOffset(0)] public float ElapsedTime;
    [FieldOffset(4)] public byte Active;
    [FieldOffset(5), BinaryUnknown] public byte Unknown005;
    [FieldOffset(6), BinaryUnknown] public ushort Unknown006;
    [FieldOffset(8)] public float LayerWeight;
    [FieldOffset(12)] public float Radius;
}
