using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

internal static class GoldQuestRecordVectors
{
    public static uint[] Words(GoldWorldBankRecord vector) => Enumerable.Range(0, vector.Count)
        .Select(i => BinaryPrimitives.ReadUInt32LittleEndian(vector.GetElement(i))).ToArray();

    public static GoldWorldBankRecord WithWords(GoldWorldBankRecord record, int vectorIndex, IReadOnlyList<uint> words)
    {
        var bytes = new byte[checked(words.Count * 4)];
        for (var i = 0; i < words.Count; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), words[i]);
        var vectors = record.Vectors.ToArray();
        vectors[vectorIndex] = new(ReadOnlyMemory<byte>.Empty, bytes, 4);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        foreach (var vector in vectors) { writer.Write(vector.Count); writer.Write(vector.Data); }
        if (vectors.Length == 3)
        {
            writer.Write(record.ChildRecords.Count);
            foreach (var child in record.ChildRecords)
            {
                writer.Write(child.Vectors[0].Count); writer.Write(child.Vectors[0].Data); writer.Write(child.Data);
            }
        }
        return new(record.Prefix.ToArray(), stream.ToArray(), 1, vectors, record.ChildRecords.ToArray());
    }
}
