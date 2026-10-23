using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Sixteen raw quest/companion pairs, preserving unreceived bytes as unknown.</summary>
public sealed class GoldActualQuestsReplica
{
    private readonly byte[] _bytes = new byte[GoldActualQuestsLayout.Size];
    private readonly bool[] _known = new bool[GoldActualQuestsLayout.Size];
    public int KnownByteCount { get; private set; }
    /// <summary>Zero-based table slot, independent of transport player-slot numbering.</summary>
    public uint? GetQuestWord(int slot) => Read(slot, GoldActualQuestsLayout.QuestWordOffset);
    /// <summary>Zero-based table slot; null until all four bytes of this word have arrived.</summary>
    public uint? GetCompanionWord(int slot) => Read(slot, GoldActualQuestsLayout.CompanionWordOffset);

    public bool TryGetPayload(out byte[] payload)
    {
        payload = KnownByteCount == _bytes.Length ? _bytes.ToArray() : [];
        return KnownByteCount == _bytes.Length;
    }

    internal void Apply(GoldActualQuestsEvent entry)
    {
        // Trust the retained wire bytes, including when callers construct events directly.
        switch (GoldActualQuestsCodec.Decode(entry.Message))
        {
            case GoldActualQuestsResetEvent reset:
                for (var slot = 0; slot < GoldActualQuestsLayout.SlotCount; slot++)
                {
                    var offset = slot * GoldActualQuestsLayout.RecordSize;
                    BinaryPrimitives.WriteUInt32LittleEndian(_bytes.AsSpan(offset), reset.QuestWord);
                    MarkKnown(offset, sizeof(uint));
                }
                break;
            case GoldActualQuestsSnapshotEvent snapshot:
                ApplyPrefix(snapshot.Message.Payload);
                break;
        }
    }

    internal void Apply(GoldWorldBankSnapshot bank)
    {
        if (bank.Opcode != GoldWorldBankLayout.Bank4Opcode) return;
        var records = bank.Sections[GoldActualQuestsLayout.BankSection].Records;
        // Bank 4 counts individual bytes here; a zero count leaves the table unchanged.
        ApplyPrefix(records.Select(record => record.Data[0]).ToArray());
    }

    internal void Clear()
    {
        Array.Clear(_bytes); Array.Clear(_known); KnownByteCount = 0;
    }

    private void ApplyPrefix(ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length > _bytes.Length) throw new InvalidDataException("Gold actual-quest table exceeds 128 bytes.");
        prefix.CopyTo(_bytes);
        MarkKnown(0, prefix.Length);
    }

    private void MarkKnown(int offset, int length)
    {
        for (var index = offset; index < offset + length; index++)
            if (!_known[index]) { _known[index] = true; KnownByteCount++; }
    }

    private uint? Read(int slot, int wordOffset)
    {
        if ((uint)slot >= GoldActualQuestsLayout.SlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
        var offset = slot * GoldActualQuestsLayout.RecordSize + wordOffset;
        for (var index = offset; index < offset + sizeof(uint); index++)
            if (!_known[index]) return null;
        return BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(offset));
    }
}
