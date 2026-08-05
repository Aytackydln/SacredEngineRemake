using System.Runtime.InteropServices;
using Sacred.Core.World;

namespace Sacred.Assets.World.Static;

public sealed class StaticPakData
{
    private const int HeaderSize = 0x100;

    private readonly StaticObjectRecord[] _recordsById;
    private readonly bool[] _hasRecord;

    private StaticPakData(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException("Static.pak is too small to contain a header.");

        var count = PakDataHelpers.ReadEntryCount(data, HeaderSize, PakDataHelpers.EntryDescriptorSize, "Static.pak");
        var descriptors = PakDataHelpers.ReadEntryDescriptors(data, HeaderSize, count, "Static.pak");
        _recordsById = new StaticObjectRecord[count];
        _hasRecord = new bool[count];
        for (uint staticId = 0; staticId < count; staticId++)
        {
            var offset = descriptors[(int)staticId].Offset;
            if (staticId == 0 || offset == 0 || offset > int.MaxValue)
                continue;

            var recordOffset = (int)offset;
            if (recordOffset + StaticObjectRecord.SerializedSize > data.Length)
                continue;

            _recordsById[staticId] = MemoryMarshal.Read<StaticObjectRecord>(
                data.Slice(recordOffset, StaticObjectRecord.SerializedSize));
            _hasRecord[staticId] = true;
        }
    }

    public static StaticPakData FromBytes(ReadOnlySpan<byte> data) => new(data);

    public StaticObjectRecord? Get(uint staticId) =>
        staticId < _recordsById.Length && _hasRecord[staticId] ? _recordsById[staticId] : null;
}
