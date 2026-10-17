using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.Pax.Hero.Inventory;

/// <summary>Gold inventory cell. 0x5DE8E0 refreshes the low halfword at +8 from the referenced object's resource.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredSavedInventoryCellLayout
{
    public const int Size = 12;
    public const int CachedItemTypeOffset = 8;
    [FieldOffset(0)] public readonly uint ObjectReference;
    [FieldOffset(4), BinaryUnknown] public readonly uint Word04;
    /// <summary>Combined cached type and retained upper halfword.</summary>
    [FieldOffset(8)] public readonly uint Word08;
    [FieldOffset(8)] public readonly ushort CachedItemType;
    [FieldOffset(10), BinaryUnknown] public readonly ushort Word0A;
}
