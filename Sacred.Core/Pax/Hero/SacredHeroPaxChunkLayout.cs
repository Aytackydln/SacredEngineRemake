using System.Runtime.InteropServices;

namespace Sacred.Core.Pax.Hero;

/// <summary>Twelve-byte AMH directory record. Length is decoded size, not necessarily file extent.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredHeroPaxChunkLayout
{
    public const int Size = 12;
    [FieldOffset(0)] public readonly uint Type;
    /// <summary>Absolute stream seek at Gold 0x605995..0x6059AF.</summary>
    [FieldOffset(4)] public readonly uint FileOffset;
    /// <summary>Decoded output capacity passed by 0x607640 to uncompress; actual output may be shorter. C3 reads 556 independently.</summary>
    [FieldOffset(8)] public readonly uint DecodedSize;
}
