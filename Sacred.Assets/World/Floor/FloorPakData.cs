using System.Runtime.InteropServices;
using Sacred.Core.World;
using static Sacred.Assets.PakDataHelpers;

namespace Sacred.Assets.World.Floor;

public sealed class FloorPakData
{
    private const int HeaderSize = 0x100;
    private const int RecordSize = 0x10;

    private readonly FloorOverlayRecord[] _recordsById;
    private readonly bool[] _hasRecord;

    private FloorPakData(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException("Floor.pak is too small to contain a header.");

        var count = ReadEntryCount(data, HeaderSize, EntryDescriptorSize, "Floor.pak");
        var descriptors = ReadEntryDescriptors(data, HeaderSize, count, "Floor.pak");
        _recordsById = new FloorOverlayRecord[count];
        _hasRecord = new bool[count];
        for (var floorId = 1; floorId < count; floorId++)
        {
            var offset = descriptors[floorId].Offset;
            if (offset == 0 || offset > int.MaxValue)
                continue;

            var recordOffset = (int)offset;
            if (recordOffset > data.Length - RecordSize)
                continue;

            _recordsById[floorId] = MemoryMarshal.Read<FloorOverlayRecord>(data.Slice(recordOffset, RecordSize));
            _hasRecord[floorId] = true;
        }
    }

    public static FloorPakData FromBytes(ReadOnlySpan<byte> data) => new(data);

    public FloorOverlayRecord? Get(uint floorId) =>
        floorId < _recordsById.Length && _hasRecord[floorId] ? _recordsById[floorId] : null;
}
