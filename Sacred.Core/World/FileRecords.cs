using System.Runtime.InteropServices;

namespace Sacred.Core.World;

[StructLayout(LayoutKind.Explicit, Size = Size)]
public readonly record struct KeyxSectorRecord
{
    public const int FileHeaderSize = 0x100;
    public const int Size = 0x300;

    [FieldOffset(0x24)] public readonly uint Id;
    [FieldOffset(0x3C)] public readonly int RawX;
    [FieldOffset(0x40)] public readonly int RawY;
    [FieldOffset(0x0D4)] public readonly int TilesRelativeOffset;
    [FieldOffset(0x0D8)] public readonly uint TilesSize;
    [FieldOffset(0x0EC)] public readonly uint CompressedOffset;
    [FieldOffset(0x0F0)] public readonly uint CompressedSize;
    [FieldOffset(0x2E0)] public readonly byte Style90;
    [FieldOffset(0x2E1)] public readonly byte StyleA0;

    public static KeyxSectorRecord FromBytes(ReadOnlySpan<byte> data)
    {
        // MemoryMarshal.Read<T> copies sizeof(T) bytes from the start of the
        // span into a new T using the FieldOffset layout above — no manual
        // slicing/BitConverter calls needed.
        return MemoryMarshal.Read<KeyxSectorRecord>(data);
    }
}

[StructLayout(LayoutKind.Explicit, Size = Size)]
public readonly record struct WldxTileRecord
{
    public const int Size = 0x20;

    [FieldOffset(0x00)] public readonly uint GroundTileId;
    [FieldOffset(0x04)] public readonly uint StaticChainHeadId;
    [FieldOffset(0x0C)] public readonly uint FloorChainHeadId;
    [FieldOffset(0x10)] public readonly sbyte LiquidAlphaLeft;
    [FieldOffset(0x11)] public readonly sbyte LiquidAlphaTop;
    [FieldOffset(0x12)] public readonly sbyte LiquidAlphaRight;
    [FieldOffset(0x13)] public readonly sbyte LiquidAlphaBottom;
    // Verified by exact corner continuity across adjacent elevated WLDX tiles.
    [FieldOffset(0x18)] public readonly sbyte ElevationSouthWest;
    [FieldOffset(0x19)] public readonly sbyte ElevationNorthWest;
    [FieldOffset(0x1A)] public readonly sbyte ElevationNorthEast;
    [FieldOffset(0x1B)] public readonly sbyte ElevationSouthEast;
    [FieldOffset(0x1E)] public readonly byte PathFlags;
    [FieldOffset(0x1F)] public readonly byte SurfaceType;

    public static WldxTileRecord FromBytes(ReadOnlySpan<byte> data)
    {
        // MemoryMarshal.Read<T> copies sizeof(T) bytes from the start of the
        // span into a new T using the FieldOffset layout above — no manual
        // slicing/BitConverter calls needed.
        return MemoryMarshal.Read<WldxTileRecord>(data);
    }
}
