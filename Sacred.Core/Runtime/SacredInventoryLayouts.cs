using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.Runtime;

[Flags]
public enum SacredInventoryCellFlags : byte
{
    None = 0, Used = 1, QuestHighlight = 0x20, TemporaryHighlight = 0x40
}

/// <summary>2004 cInventoryItem. Only the anchor cell has an object reference.
/// At the anchor, WidthOrAnchorX/HeightOrAnchorY are item dimensions; other occupied
/// cells store the anchor's coordinates. Verified by markSlotsUsed and getBaseSlot.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredInventoryCellLayout
{
    public const int Size = 12;
    [FieldOffset(0)] public readonly uint ObjectReference;
    [FieldOffset(4)] public readonly SacredInventoryCellFlags Flags;
    [FieldOffset(5)] public readonly byte WidthOrAnchorX;
    [FieldOffset(6)] public readonly byte HeightOrAnchorY;
    [FieldOffset(7), BinaryUnknown] public readonly byte CacheState;
    [FieldOffset(8), BinaryUnknown] public readonly byte Reserved;
}

[InlineArray(256)] public struct SacredInventoryCells { private SacredInventoryCellLayout _first; }
[InlineArray(256)] public struct SacredInventoryKeyReferences { private uint _first; }

/// <summary>2004 cInventoryEntry. Fixed storage holds 256 cells; default active grid
/// is 20 by 8, established by the default constructor at 0x554E60.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredInventoryEntryLayout
{
    public const int Size = 4480;
    public const int CellCapacity = 256;
    public const int DefaultWidth = 20;
    public const int DefaultHeight = 8;
    [FieldOffset(0)] public readonly SacredRuntimeVectorLayout QuestItems;
    [FieldOffset(12)] public readonly SacredRuntimeVectorLayout ScrollItems;
    [FieldOffset(24)] public readonly SacredInventoryCells Cells;
    // +0xC18: two banks of five 12-byte taskbar item entries.
    // +0xC90: two banks of five 20-byte taskbar spell entries.
    [FieldOffset(0xD58)] public readonly ushort ActiveSlot;
    [FieldOffset(0xD5A)] public readonly ushort VampireActiveSlot;
    [FieldOffset(0xD5C)] public readonly SacredInventoryKeyReferences Keys;
    [FieldOffset(0x115C)] public readonly ushort Width;
    [FieldOffset(0x115E)] public readonly ushort Height;
    [FieldOffset(0x1160)] public readonly ushort ScreenX;
    [FieldOffset(0x1162)] public readonly ushort ScreenY;
    [FieldOffset(0x1164)] public readonly ushort InventoryId;
    [FieldOffset(0x1166)] public readonly ushort Flags;
    [FieldOffset(0x1168)] public readonly uint OwnerReference;
    [FieldOffset(0x116C)] public readonly SacredRuntimeVectorLayout Effects;
    [FieldOffset(0x1178)] public readonly uint CachedSurfaceAddress;
    [FieldOffset(0x117C), BinaryUnknown] public readonly byte Refresh;
}
