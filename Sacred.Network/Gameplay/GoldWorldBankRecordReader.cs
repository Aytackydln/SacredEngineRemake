namespace Sacred.Network.Gameplay;

internal static class GoldWorldBankRecordReader
{
    public static int GetMinimumSize(GoldWorldBankSectionKind kind, int elementSize) => kind switch
    {
        GoldWorldBankSectionKind.NamedWords => GoldWorldBankLayout.NameSize + sizeof(uint),
        GoldWorldBankSectionKind.KeyedWords or GoldWorldBankSectionKind.KeyedRecords or GoldWorldBankSectionKind.SectorRecords => 2 * sizeof(uint),
        GoldWorldBankSectionKind.NestedVectors => GoldWorldBankLayout.NestedVectorsPrefixSize + 4 * sizeof(uint),
        GoldWorldBankSectionKind.RecordAndWordVectors => GoldWorldBankLayout.RecordVectorsPrefixSize + 2 * sizeof(uint),
        _ => elementSize
    };

    public static GoldWorldBankRecord Read(GoldWorldBankSectionKind kind, int elementSize, ref GoldWorldBankReader reader)
    {
        switch (kind)
        {
            case GoldWorldBankSectionKind.NestedVectors:
                return ReadNested(ref reader);
            case GoldWorldBankSectionKind.RecordAndWordVectors:
                var prefix = reader.ReadBlock(GoldWorldBankLayout.RecordVectorsPrefixSize);
                var start = reader.Cursor;
                GoldWorldBankRecord[] vectors = [ReadVector(GoldWorldBankLayout.RecordVectorElementSize, ref reader), ReadVector(4, ref reader)];
                return new(prefix, reader.ReadSince(start), 1, vectors, []);
            case GoldWorldBankSectionKind.NamedWords:
            case GoldWorldBankSectionKind.KeyedWords:
            case GoldWorldBankSectionKind.KeyedRecords:
            case GoldWorldBankSectionKind.SectorRecords:
                var size = kind == GoldWorldBankSectionKind.NamedWords ? GoldWorldBankLayout.NameSize : sizeof(uint);
                var key = reader.ReadBlock(size);
                return new(key, reader.ReadVector(elementSize), elementSize);
            default:
                return new(ReadOnlyMemory<byte>.Empty, reader.ReadBlock(elementSize), elementSize);
        }
    }

    private static GoldWorldBankRecord ReadNested(ref GoldWorldBankReader reader)
    {
        var prefix = reader.ReadBlock(GoldWorldBankLayout.NestedVectorsPrefixSize);
        var start = reader.Cursor;
        GoldWorldBankRecord[] vectors = [ReadVector(4, ref reader), ReadVector(4, ref reader), ReadVector(4, ref reader)];
        var count = reader.ReadCount();
        reader.RequireRecords(count, sizeof(uint) + GoldWorldBankLayout.NestedRecordFixedSize);
        var children = new GoldWorldBankRecord[(int)count];
        for (var index = 0; index < children.Length; index++)
        {
            var words = ReadVector(4, ref reader);
            // Native 0x4DD430 reads the word vector first, then 24 fixed bytes; no runtime pointers enter the wire data.
            children[index] = new(ReadOnlyMemory<byte>.Empty, reader.ReadBlock(GoldWorldBankLayout.NestedRecordFixedSize), GoldWorldBankLayout.NestedRecordFixedSize, [words], []);
        }
        return new(prefix, reader.ReadSince(start), 1, vectors, children);
    }

    private static GoldWorldBankRecord ReadVector(int elementSize, ref GoldWorldBankReader reader) =>
        new(ReadOnlyMemory<byte>.Empty, reader.ReadVector(elementSize), elementSize);
}
