using System.Runtime.InteropServices;

namespace Sacred.Core.World;

/// <summary>Header preceding sector records in <c>sectors.keyx</c>.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct KeyxHeaderLayout
{
    /// <summary>Serialized header size.</summary>
    public const int SerializedSize = 0x100;

    /// <summary>Number of 0x300-byte sector records.</summary>
    [FieldOffset(0x04)] public readonly uint SectorCount;
}

/// <summary>
/// Descriptor for one tile grid in a decompressed <c>sectors.wldx</c> sector payload.
/// The first descriptor after the outdoor tile table identifies the outdoor origin and
/// has no tile payload; subsequent descriptors identify sparse indoor grids.
/// </summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct WldxTileGridDescriptorLayout
{
    /// <summary>Serialized descriptor size.</summary>
    public const int SerializedSize = 0x24;

    /// <summary>World X coordinate of the indoor grid origin.</summary>
    [FieldOffset(0x00)] public readonly int WorldX;
    /// <summary>World Y coordinate of the indoor grid origin.</summary>
    [FieldOffset(0x04)] public readonly int WorldY;
    /// <summary>Grid width in tiles; zero on the outdoor-origin descriptor.</summary>
    [FieldOffset(0x08)] public readonly ushort Width;
    /// <summary>Grid height in tiles; zero on the outdoor-origin descriptor.</summary>
    [FieldOffset(0x0A)] public readonly ushort Height;
    /// <summary>Payload kind.</summary>
    [FieldOffset(0x0C)] public readonly WldxTileGridKind Kind;
    /// <summary>Offset of the tile array in the decompressed sector payload.</summary>
    [FieldOffset(0x10)] public readonly uint TilesOffset;
    /// <summary>Byte length of the tile array.</summary>
    [FieldOffset(0x14)] public readonly uint TilesSize;

    public bool IsOutdoorOrigin =>
        Kind == WldxTileGridKind.Terrain && Width == 0 && Height == 0 &&
        TilesOffset == 0 && TilesSize == 0;

    public bool HasIndoorTilePayload(int payloadSize)
    {
        var expectedSize = (ulong)Width * Height * WldxTileRecord.Size;
        return Kind == WldxTileGridKind.Terrain && Width != 0 && Height != 0 &&
               TilesSize == expectedSize && TilesOffset <= payloadSize &&
               TilesSize <= (ulong)payloadSize - TilesOffset;
    }

    public static WldxTileGridDescriptorLayout FromBytes(ReadOnlySpan<byte> data) =>
        MemoryMarshal.Cast<byte, WldxTileGridDescriptorLayout>(data)[0];
}

public enum WldxTileGridKind : uint
{
    Terrain = 6,
}
