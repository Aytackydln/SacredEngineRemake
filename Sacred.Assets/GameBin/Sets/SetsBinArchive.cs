using System.Buffers.Binary;
using Sacred.Core.GameBin.Sets;

namespace Sacred.Assets.GameBin.Sets;

public static class SetsBinArchive
{
    public static IReadOnlyList<SacredSetEntry> Load(string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var data = new byte[checked((int)fs.Length)];
        fs.ReadExactly(data);

        return Parse(data, filePath);
    }

    public static IReadOnlyList<SacredSetEntry> Parse(ReadOnlySpan<byte> data, string archiveName = "sets.bin")
    {
        if (data.Length < sizeof(uint))
            throw new InvalidDataException($"{archiveName} is too small to contain a set count.");

        var count = BinaryPrimitives.ReadUInt32LittleEndian(data[..sizeof(uint)]);
        if (count > int.MaxValue)
            throw new InvalidDataException($"{archiveName} set count {count} is too large.");

        var expectedLength = sizeof(uint) + checked((long)count * SacredSetEntry.Size);
        if (data.Length != expectedLength)
            throw new InvalidDataException($"{archiveName} length is {data.Length}, expected {expectedLength} for {count} set records.");

        var records = new SacredSetEntry[(int)count];
        for (var i = 0; i < records.Length; i++)
        {
            var offset = sizeof(uint) + i * SacredSetEntry.Size;
            var record = SacredSetEntry.FromBytes(i, offset, data.Slice(offset, SacredSetEntry.Size));
            if (!record.HasConsistentPackedFields)
            {
                throw new InvalidDataException(
                    $"{archiveName} set record {i} has packed index/count 0x{record.PackedSetIndexAndItemCount:X8}, " +
                    $"but contains {record.ItemIds.Count} item ids.");
            }

            records[i] = record;
        }

        return records;
    }
}
