using System.Runtime.InteropServices;
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
    /// <summary>Style identifier for WLDX liquid material A.</summary>
    [FieldOffset(0x2E0)] public readonly byte LiquidStyleA;
    /// <summary>Style identifier for WLDX liquid material B.</summary>
    [FieldOffset(0x2E1)] public readonly byte LiquidStyleB;

    public WorldZone Zone => (EnvironmentFlags & SectorEnvironmentFlags.Dungeon) != 0
        ? WorldZone.Cave
        : WorldZone.Outdoors;

    public static KeyxSectorRecord FromBytes(ReadOnlySpan<byte> data)
    {
        // Cast the first record-sized span through the explicit FieldOffset layout.
        return MemoryMarshal.Cast<byte, KeyxSectorRecord>(data)[0];
    }
}
