namespace Sacred.Core.World;

public readonly record struct KeyxSectorRecord(
    uint Id,
    int RawX,
    int RawY,
    uint CompressedOffset,
    uint CompressedSize,
    int TilesRelativeOffset,
    int TilesSize,
    byte Style90,
    byte StyleA0)
{
    public const int FileHeaderSize = 0x100;
    public const int Size = 0x300;

    private const int IdOffset = 0x24;
    private const int AbsoluteRawXOffset = 0x3C;
    private const int AbsoluteRawYOffset = 0x40;
    private const int TilesRelativeOffsetOffset = 0x0D4;
    private const int TilesSizeOffset = 0x0D8;
    private const int CompressedOffsetOffset = 0x0EC;
    private const int CompressedSizeOffset = 0x0F0;
    private const int Style90Offset = 0x2E0;
    private const int StyleA0Offset = 0x2E1;

    public static KeyxSectorRecord FromBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size)
            throw new InvalidDataException("A sectors.keyx entry is truncated.");

        return new KeyxSectorRecord(
            BitConverter.ToUInt32(data.Slice(IdOffset, 4)),
            BitConverter.ToInt32(data.Slice(AbsoluteRawXOffset, 4)),
            BitConverter.ToInt32(data.Slice(AbsoluteRawYOffset, 4)),
            BitConverter.ToUInt32(data.Slice(CompressedOffsetOffset, 4)),
            BitConverter.ToUInt32(data.Slice(CompressedSizeOffset, 4)),
            checked((int)BitConverter.ToUInt32(data.Slice(TilesRelativeOffsetOffset, 4))),
            checked((int)BitConverter.ToUInt32(data.Slice(TilesSizeOffset, 4))),
            data[Style90Offset],
            data[StyleA0Offset]);
    }
}

public readonly record struct WldxTileRecord(
    uint GroundTileId,
    uint StaticChainHeadId,
    uint FloorChainHeadId,
    byte SurfaceType,
    sbyte LiquidAlphaLeft,
    sbyte LiquidAlphaTop,
    sbyte LiquidAlphaRight,
    sbyte LiquidAlphaBottom)
{
    public const int Size = 0x20;

    private const int StaticChainHeadOffset = 0x04;
    private const int FloorChainHeadOffset = 0x0C;
    private const int LiquidAlphaLeftOffset = 0x10;
    private const int LiquidAlphaTopOffset = 0x11;
    private const int LiquidAlphaRightOffset = 0x12;
    private const int LiquidAlphaBottomOffset = 0x13;
    private const int SurfaceTypeOffset = 0x1F;

    public static WldxTileRecord FromBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size)
            throw new InvalidDataException("A sectors.wldx tile descriptor is truncated.");

        return new WldxTileRecord(
            BitConverter.ToUInt32(data.Slice(0, 4)),
            BitConverter.ToUInt32(data.Slice(StaticChainHeadOffset, 4)),
            BitConverter.ToUInt32(data.Slice(FloorChainHeadOffset, 4)),
            data[SurfaceTypeOffset],
            unchecked((sbyte)data[LiquidAlphaLeftOffset]),
            unchecked((sbyte)data[LiquidAlphaTopOffset]),
            unchecked((sbyte)data[LiquidAlphaRightOffset]),
            unchecked((sbyte)data[LiquidAlphaBottomOffset]));
    }
}
