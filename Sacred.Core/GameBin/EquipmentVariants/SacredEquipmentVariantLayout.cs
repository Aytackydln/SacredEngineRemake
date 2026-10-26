using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.EquipmentVariants;

/// <summary>Gold Rust.bin (0x42FF8F..0x430066): outer uint profile count,
/// then a uint row count and that many eight-byte source/replacement pairs.
/// The native 0x43CA20 lookup preserves row order.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredEquipmentVariantLayout
{
    public const int Size = 8;
    [FieldOffset(0)] public readonly uint SourceType;
    [FieldOffset(4)] public readonly uint ReplacementType;
}

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 4)]
public readonly struct SacredEquipmentVariantCountLayout
{
    [FieldOffset(0)] public readonly uint Count;
}
