using System.Runtime.InteropServices;
using Sacred.Core.World;
using static Sacred.Assets.PakDataHelpers;

namespace Sacred.Assets.World.Floor;

public sealed class FloorPakData
{
    private const int HeaderSize = 0x100;
    private const int RecordSize = 0x10;

    private readonly Dictionary<uint, FloorOverlayRecord> _recordsById = new();

    private FloorPakData(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException("Floor.pak is too small to contain a header.");

        var count = ReadEntryCount(data, HeaderSize, EntryDescriptorSize, "Floor.pak");
        var descriptors = ReadEntryDescriptors(data, HeaderSize, count, "Floor.pak");
        for (uint floorId = 0; floorId < count; floorId++)
        {
            var offset = descriptors[(int)floorId].Offset;
            if (floorId == 0 || offset == 0 || offset > int.MaxValue)
                continue;

            var recordOffset = (int)offset;
            if (recordOffset + RecordSize > data.Length)
                continue;

            _recordsById[floorId] = MemoryMarshal.Read<FloorOverlayRecord>(data.Slice(recordOffset, RecordSize));
        }
    }

    public static FloorPakData FromBytes(ReadOnlySpan<byte> data) => new(data);

    public FloorOverlayRecord? Get(uint floorId) =>
        _recordsById.TryGetValue(floorId, out var record) ? record : null;
}
