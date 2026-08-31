using System.Runtime.InteropServices;
using Sacred.Core.World.Pathing;
using Sacred.Core.World.Sector;

namespace Sacred.Core.World;

/// <summary>Fixed-size sector index record stored in <c>sectors.keyx</c>.</summary>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public readonly record struct KeyxSectorRecord
{
    public const int FileHeaderSize = KeyxHeaderLayout.SerializedSize;
    public const int Size = 0x300;

    /// <summary>Sector identifier referenced by the world archive.</summary>
    [FieldOffset(0x24)] public readonly uint Id;
    /// <summary>Raw authored X position used to derive the sector-grid origin.</summary>
    [FieldOffset(0x3C)] public readonly int RawX;
    /// <summary>Raw authored Y position used to derive the sector-grid origin.</summary>
    [FieldOffset(0x40)] public readonly int RawY;
    /// <summary>Offset of the outdoor tile table in the decompressed WLDX payload.</summary>
    [FieldOffset(0x0D4)] public readonly int TilesRelativeOffset;
    /// <summary>Declared byte size of the outdoor tile table.</summary>
    [FieldOffset(0x0D8)] public readonly uint TilesSize;
    /// <summary>Absolute offset of the compressed sector payload in <c>sectors.wldx</c>.</summary>
    [FieldOffset(0x0EC)] public readonly uint CompressedOffset;
    /// <summary>Compressed sector payload size in bytes.</summary>
    [FieldOffset(0x0F0)] public readonly uint CompressedSize;
    /// <summary>Authored environment and sector-edge flags.</summary>
    [FieldOffset(0x1CC)] public readonly SectorEnvironmentFlags EnvironmentFlags;
    /// <summary>Style byte associated with environment value 0x90.</summary>
    [FieldOffset(0x2E0)] public readonly byte Style90;
    /// <summary>Style byte associated with environment value 0xA0.</summary>
    [FieldOffset(0x2E1)] public readonly byte StyleA0;

    public WorldZone Zone => (EnvironmentFlags & SectorEnvironmentFlags.Dungeon) != 0
        ? WorldZone.Cave
        : WorldZone.Outdoors;

    public static KeyxSectorRecord FromBytes(ReadOnlySpan<byte> data)
    {
        // Cast the first record-sized span through the explicit FieldOffset layout.
        return MemoryMarshal.Cast<byte, KeyxSectorRecord>(data)[0];
    }
}

/// <summary>One 0x20-byte outdoor or indoor tile record in a decompressed WLDX payload.</summary>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public readonly record struct WldxTileRecord
{
    public const int Size = 0x20;

    /// <summary>Ground-tile definition identifier.</summary>
    [FieldOffset(0x00)] public readonly uint GroundTileId;
    /// <summary>Head identifier of the linked Static.pak object chain.</summary>
    [FieldOffset(0x04)] public readonly uint StaticChainHeadId;
    /// <summary>Head identifier of the linked Floor.pak overlay chain.</summary>
    [FieldOffset(0x0C)] public readonly uint FloorChainHeadId;
    /// <summary>Liquid alpha at the tile's left corner.</summary>
    [FieldOffset(0x10)] public readonly sbyte LiquidAlphaLeft;
    /// <summary>Liquid alpha at the tile's top corner.</summary>
    [FieldOffset(0x11)] public readonly sbyte LiquidAlphaTop;
    /// <summary>Liquid alpha at the tile's right corner.</summary>
    [FieldOffset(0x12)] public readonly sbyte LiquidAlphaRight;
    /// <summary>Liquid alpha at the tile's bottom corner.</summary>
    [FieldOffset(0x13)] public readonly sbyte LiquidAlphaBottom;
    // Verified by exact corner continuity across adjacent WLDX tiles. Values are
    // authored in steps of 0x14, with 0xFF representing an unoccluded vertex.
    /// <summary>Baked terrain brightness at the south-west corner.</summary>
    [FieldOffset(0x14)] public readonly byte BakedBrightnessSouthWest;
    /// <summary>Baked terrain brightness at the north-west corner.</summary>
    [FieldOffset(0x15)] public readonly byte BakedBrightnessNorthWest;
    /// <summary>Baked terrain brightness at the north-east corner.</summary>
    [FieldOffset(0x16)] public readonly byte BakedBrightnessNorthEast;
    /// <summary>Baked terrain brightness at the south-east corner.</summary>
    [FieldOffset(0x17)] public readonly byte BakedBrightnessSouthEast;
    // Verified by exact corner continuity across adjacent elevated WLDX tiles.
    /// <summary>Signed terrain elevation at the south-west corner.</summary>
    [FieldOffset(0x18)] public readonly sbyte ElevationSouthWest;
    /// <summary>Signed terrain elevation at the north-west corner.</summary>
    [FieldOffset(0x19)] public readonly sbyte ElevationNorthWest;
    /// <summary>Signed terrain elevation at the north-east corner.</summary>
    [FieldOffset(0x1A)] public readonly sbyte ElevationNorthEast;
    /// <summary>Signed terrain elevation at the south-east corner.</summary>
    [FieldOffset(0x1B)] public readonly sbyte ElevationSouthEast;
    /// <summary>
    /// Navigation and surface-projection flags. Value 0x01 marks vertical-only actor
    /// elevation; value 0x02 adds the rightward projection used by raised bridges.
    /// </summary>
    [FieldOffset(0x1E)] public readonly WorldPathFlags PathFlags;
    /// <summary>
    /// Packed path type in the low nibble and terrain-surface flags in the high nibble;
    /// bit 0x10 suppresses visual tessellation while retaining elevation for actors.
    /// </summary>
    [FieldOffset(0x1F)] public readonly byte TypeAndSurface;

    public static WldxTileRecord FromBytes(ReadOnlySpan<byte> data)
    {
        // Cast the first record-sized span through the explicit FieldOffset layout.
        return MemoryMarshal.Cast<byte, WldxTileRecord>(data)[0];
    }
}
