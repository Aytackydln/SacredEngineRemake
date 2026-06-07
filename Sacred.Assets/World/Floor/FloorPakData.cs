using static Sacred.Assets.PakDataHelpers;

namespace Sacred.Assets.World.Floor;

public sealed class FloorPakData
{
    private const int HeaderSize = 0x100;
    private const int DescriptorSize = 0x0C;
    private const int RecordSize = 0x10;
    private const uint PrimaryTileMask = 0x1FFFF;
    private const int SecondaryTileShift = 17;
    private const uint SecondaryTileMask = 0x7FFF;

    private readonly Dictionary<uint, FloorOverlayRecord> _recordsById = new();

    private FloorPakData(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException("Floor.pak is too small to contain a header.");

        var count = ReadEntryCount(data, HeaderSize, DescriptorSize, "Floor.pak");
        for (uint floorId = 0; floorId < count; floorId++)
        {
            var descriptorOffset = HeaderSize + (int)floorId * DescriptorSize;
            if (descriptorOffset + DescriptorSize > data.Length)
                break;

            var offset = BitConverter.ToUInt32(data.Slice(descriptorOffset + 4, 4));
            if (floorId == 0 || offset == 0 || offset > int.MaxValue)
                continue;

            var recordOffset = (int)offset;
            if (recordOffset + RecordSize > data.Length)
                continue;

            _recordsById[floorId] = new FloorOverlayRecord(
                floorId,
                BitConverter.ToUInt32(data.Slice(recordOffset + 0x04, 4)),
                BitConverter.ToUInt32(data.Slice(recordOffset + 0x0C, 4)));
        }
    }

    public static FloorPakData FromBytes(ReadOnlySpan<byte> data) => new(data);

    public FloorOverlayRecord? Get(uint floorId) =>
        _recordsById.TryGetValue(floorId, out var record) ? record : null;

    public static uint PrimaryTileId(uint tileOrBlendRef) => tileOrBlendRef & PrimaryTileMask;

    public static uint SecondaryTileId(uint tileOrBlendRef) =>
        (tileOrBlendRef >> SecondaryTileShift) & SecondaryTileMask;
}