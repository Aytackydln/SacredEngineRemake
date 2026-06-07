namespace Sacred.Assets.World.Static;

public sealed class StaticPakData
{
    private const int HeaderSize = 0x100;
    private const int DescriptorSize = 0x0C;
    private const int RecordSize = 0x40;

    private readonly Dictionary<uint, StaticObjectRecord> _recordsById = new();

    private StaticPakData(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException("Static.pak is too small to contain a header.");

        var count = PakDataHelpers.ReadEntryCount(data, HeaderSize, DescriptorSize, "Static.pak");
        for (uint staticId = 0; staticId < count; staticId++)
        {
            var descriptorOffset = HeaderSize + (int)staticId * DescriptorSize;
            if (descriptorOffset + DescriptorSize > data.Length)
                break;

            var descriptorType = BitConverter.ToUInt32(data.Slice(descriptorOffset, 4));
            var offset = BitConverter.ToUInt32(data.Slice(descriptorOffset + 4, 4));
            var size = BitConverter.ToUInt32(data.Slice(descriptorOffset + 8, 4));
            if (staticId == 0 || offset == 0 || offset > int.MaxValue)
                continue;

            var recordOffset = (int)offset;
            if (recordOffset + RecordSize > data.Length)
                continue;

            _recordsById[staticId] = new StaticObjectRecord(
                staticId,
                descriptorType,
                size,
                BitConverter.ToUInt32(data.Slice(recordOffset, 4)),
                BitConverter.ToUInt32(data.Slice(recordOffset + 0x04, 4)),
                BitConverter.ToUInt32(data.Slice(recordOffset + 0x08, 4)),
                BitConverter.ToUInt16(data.Slice(recordOffset + 0x0C, 2)),
                BitConverter.ToInt32(data.Slice(recordOffset + 0x0E, 4)),
                BitConverter.ToInt32(data.Slice(recordOffset + 0x12, 4)),
                BitConverter.ToUInt32(data.Slice(recordOffset + 0x1F, 4)),
                BitConverter.ToInt16(data.Slice(recordOffset + 0x2B, 2)),
                data[recordOffset + 0x2E],
                data[recordOffset + 0x2F],
                data[recordOffset + 0x30],
                data[recordOffset + 0x33]);
        }
    }

    public static StaticPakData FromBytes(ReadOnlySpan<byte> data) => new(data);

    public StaticObjectRecord? Get(uint staticId) =>
        _recordsById.TryGetValue(staticId, out var record) ? record : null;
}