using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.Pax.Hero;

/// <summary>Gold 0x607640 reads 32 bytes and uses +4 as the zlib input length. Marker/reserved words are not validated.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredHeroPaxCompressionLayout
{
    public const int Size = 32;
    [FieldOffset(0), BinaryUnknown] public readonly uint Marker;
    [FieldOffset(4)] public readonly uint CompressedSize;
    [FieldOffset(8), BinaryUnknown] public readonly uint Word08;
    [FieldOffset(12), BinaryUnknown] public readonly uint Word12;
    [FieldOffset(16), BinaryUnknown] public readonly uint Word16;
    [FieldOffset(20), BinaryUnknown] public readonly uint Word20;
    [FieldOffset(24), BinaryUnknown] public readonly uint Word24;
    [FieldOffset(28), BinaryUnknown] public readonly uint Word28;
}
