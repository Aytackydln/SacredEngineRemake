using System.Runtime.InteropServices;
using Sacred.Core.World;
using static Sacred.Assets.PakDataHelpers;

namespace Sacred.Assets.World.Floor;

public sealed class FloorPakData
{
    private const int HeaderSize = 0x100;
    private const int RecordSize = 0x10;

    private readonly Dictionary<uint, FloorOverlayRecord> _recordsById;

    private FloorPakData(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException("Floor.pak is too small to contain a header.");

        var count = ReadEntryCount(data, HeaderSize, EntryDescriptorSize, "Floor.pak");
        var descriptors = ReadEntryDescriptors(data, HeaderSize, count, "Floor.pak");
        var descriptorArray = descriptors.ToArray();

        var firstRecordOffset = descriptorArray
            .Where(static descriptor => descriptor.Offset > 0)
            .Select(static descriptor => descriptor.Offset)
            .DefaultIfEmpty()
            .Min();

        var recordBytes = data[(int)firstRecordOffset..];
        var records = MemoryMarshal.Cast<byte, FloorOverlayRecord>(
            recordBytes[..(recordBytes.Length / RecordSize * RecordSize)]
        ).ToArray();

        _recordsById = Enumerable
            .Range(1, count - 1)
            .Select(floorId => new
            {
                FloorId = (uint)floorId,
                RecordIndex = GetRecordIndex(descriptorArray[floorId].Offset, firstRecordOffset)
            })
            .Where(entry => entry.RecordIndex >= 0 && entry.RecordIndex < records.Length)
            .ToDictionary(
                static entry => entry.FloorId,
                entry => records[entry.RecordIndex]
            );
    }

    public static FloorPakData FromBytes(ReadOnlySpan<byte> data) => new(data);

    public FloorOverlayRecord? Get(uint floorId) =>
        _recordsById.TryGetValue(floorId, out var record) ? record : null;

    private static int GetRecordIndex(uint offset, uint firstRecordOffset)
    {
        var offsetDelta = offset - firstRecordOffset;
        return checked((int)(offsetDelta / RecordSize));
    }
}
