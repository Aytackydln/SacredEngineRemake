using System.Runtime.InteropServices;
using Sacred.Core.Binary;
using Sacred.Core.World.Sector;

namespace Sacred.Core.World;

/// <summary>Fixed-size sector index record stored in <c>sectors.keyx</c>.</summary>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public readonly record struct KeyxSectorRecord
{
    public const int FileHeaderSize = KeyxHeaderLayout.SerializedSize;
    public const int Size = 0x300;

    [FieldOffset(0x00), BinaryString("SectorName", 32, "ISO-8859-1")]
    private readonly byte _sectorName;
    /// <summary>Native clientID.</summary>
    [FieldOffset(0x20)] public readonly uint ClientId;

    /// <summary>Sector identifier referenced by the world archive.</summary>
    [FieldOffset(0x24)] public readonly uint Id;
    [FieldOffset(0x28)] public readonly uint ServerRevisionId;
    /// <summary>Native surround[8]. Neighbor order is not inferred here.</summary>
    [FieldOffset(0x2C)] public readonly SectorNeighbors Neighbors;
    /// <summary>Raw authored X position used to derive the sector-grid origin.</summary>
    [FieldOffset(0x3C)] public readonly int RawX;
    /// <summary>Raw authored Y position used to derive the sector-grid origin.</summary>
    [FieldOffset(0x40)] public readonly int RawY;
    /// <summary>Native bis_x and bis_y, the opposite authored sector bounds.</summary>
    [FieldOffset(0x44)] public readonly int RawEndX;
    [FieldOffset(0x48)] public readonly int RawEndY;
    /// <summary>Native lvlChunks[32]. Existing offset fields below alias selected members.</summary>
    [FieldOffset(0x4C)] public readonly WorldFileChunks Chunks;
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
    /// <summary>Native surround2[0]; meaning is not established beyond the native name.</summary>
    [FieldOffset(0x1D0)] public readonly uint Surround2A;
    /// <summary>Native surround2[1].</summary>
    [FieldOffset(0x1D4)] public readonly uint Surround2B;
    [FieldOffset(0x1D8)] public readonly byte NatureType;
    [FieldOffset(0x1D9)] public readonly WorldSystemTimeLayout LastModified;
    [FieldOffset(0x1E9)] public readonly SectorEnvironmentLayout Environment;
    /// <summary>Style identifier for WLDX liquid material A.</summary>
    [FieldOffset(0x2E0)] public readonly byte LiquidStyleA;
    /// <summary>Style identifier for WLDX liquid material B.</summary>
    [FieldOffset(0x2E1)] public readonly byte LiquidStyleB;
    [FieldOffset(0x2E9), BinaryUnknown] public readonly WorldBytes23 Reserved;

    public WorldZone Zone => EnvironmentFlags.HasFlag(SectorEnvironmentFlags.Dungeon)
        ? WorldZone.Cave
        : WorldZone.Outdoors;

    public static KeyxSectorRecord FromBytes(ReadOnlySpan<byte> data)
    {
        // Cast the first record-sized span through the explicit FieldOffset layout.
        return MemoryMarshal.Cast<byte, KeyxSectorRecord>(data)[0];
    }
}
