using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public static class GoldQuestCodec
{
    public static GoldQuestEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var copy = message with { Payload = message.Payload.ToArray() };
        var reader = new GoldWorldBankReader(copy.Payload, 0);
        var index = reader.ReadCount();
        var tailOffset = -1;
        if (copy.Opcode == GoldQuestLayout.DatabaseAndEntriesOpcode)
        {
            var bodySize = reader.ReadCount();
            if (bodySize > copy.Payload.Length - 8) throw new InvalidDataException("Truncated Gold quest database body.");
            tailOffset = 8 + (int)bodySize;
            reader = new GoldWorldBankReader(reader.ReadBlock((int)bodySize).ToArray(), 0);
        }
        if (copy.Opcode == GoldQuestLayout.ProgressOpcode)
        {
            if (copy.Payload.Length < GoldQuestLayout.ProgressSize) throw new InvalidDataException("Truncated Gold quest progress.");
            var count = BinaryPrimitives.ReadInt32LittleEndian(copy.Payload.AsSpan(GoldQuestLayout.ProgressCountOffset));
            if (count > GoldQuestLayout.ProgressCapacity) throw new InvalidDataException("Gold quest progress exceeds its inline entry array.");
            var entries = new uint[Math.Max(0, count)];
            for (var i = 0; i < entries.Length; i++) entries[i] = BinaryPrimitives.ReadUInt32LittleEndian(copy.Payload.AsSpan(4 + i * 4));
            return new GoldQuestProgressEvent(copy, unchecked((int)index), Array.AsReadOnly(entries),
                BinaryPrimitives.ReadUInt32LittleEndian(copy.Payload.AsSpan(GoldQuestLayout.ProgressDatabaseOffset)));
        }

        GoldWorldBankRecord record;
        switch (copy.Opcode)
        {
            case GoldQuestLayout.InsertOpcode:
                record = GoldWorldBankRecordReader.Read(GoldWorldBankSectionKind.RecordAndWordVectors, 1, ref reader);
                return new GoldQuestInsertEvent(copy, index, record);
            case GoldQuestLayout.EntryOpcode:
                GoldQuestLayout.RequireIndex(index, GoldQuestLayout.GrowthSlots, GoldQuestLayout.EntrySize);
                record = new(ReadOnlyMemory<byte>.Empty, reader.ReadBlock(GoldQuestLayout.EntrySize), GoldQuestLayout.EntrySize);
                break;
            case GoldQuestLayout.HeaderOpcode:
                GoldQuestLayout.RequireIndex(index, 1, GoldQuestLayout.HeaderRuntimeStride);
                record = GoldWorldBankRecordReader.Read(GoldWorldBankSectionKind.NestedVectors, 1, ref reader);
                break;
            case GoldQuestLayout.DatabaseOpcode:
            case GoldQuestLayout.DatabaseAndEntriesOpcode:
                GoldQuestLayout.RequireIndex(index, copy.Opcode == GoldQuestLayout.DatabaseOpcode ? GoldQuestLayout.GrowthSlots : 1,
                    GoldQuestLayout.DatabaseRuntimeStride);
                record = GoldWorldBankRecordReader.Read(GoldWorldBankSectionKind.RecordAndWordVectors, 1, ref reader);
                break;
            case GoldQuestLayout.JournalOpcode:
                GoldQuestLayout.RequireIndex(index, 1, GoldQuestLayout.JournalRuntimeStride);
                record = new(ReadOnlyMemory<byte>.Empty, reader.ReadBlock(GoldQuestLayout.JournalWireSize), GoldQuestLayout.JournalWireSize);
                break;
            default: throw new InvalidDataException("Expected Gold quest update.");
        }
        // 1BE uses every remaining complete word; its last 0..3 bytes stay unread.
        var tail = copy.Opcode == GoldQuestLayout.DatabaseAndEntriesOpcode
            ? copy.Payload.AsMemory(tailOffset, (copy.Payload.Length - tailOffset) / 4 * 4) : ReadOnlyMemory<byte>.Empty;
        return new GoldQuestUpdateEvent(copy, index, record, tail);
    }
}
