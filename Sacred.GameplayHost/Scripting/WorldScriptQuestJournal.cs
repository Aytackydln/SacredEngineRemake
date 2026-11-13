using System.Buffers.Binary;
using Sacred.Core.GameBin.Scripts;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Fresh interpreter journal and sixteen main/other journal-index pairs.
/// Gold 0x4614FE..0x46152B initializes all 32 index words to -1. Complete native
/// records are retained; bank/wire export excludes only the runtime flag word.</summary>
public sealed class WorldScriptQuestJournal
{
    private readonly List<byte[]> _records = [];
    private readonly byte[] _indices = Enumerable.Repeat((byte)255, GoldActualQuestsLayout.Size).ToArray();
    public int Count => _records.Count;
    public byte[] GetRecord(int index) => _records[index].ToArray();
    public byte[] GetIndices() => _indices.ToArray();
    public int FindQuest(int number) => _records.FindIndex(row =>
        BinaryPrimitives.ReadInt32LittleEndian(row.AsSpan(SacredScriptJournalLayout.QuestNumberOffset)) == number);
    internal uint ReadWord(int index, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(_records[index].AsSpan(offset));
    internal void WriteWord(int index, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(_records[index].AsSpan(offset), value);
    internal int GetIndex(uint slot, bool other) => BinaryPrimitives.ReadInt32LittleEndian(
        _indices.AsSpan(checked((int)slot) * GoldActualQuestsLayout.RecordSize + (other ? sizeof(uint) : 0)));
    public static WorldScriptQuestJournal FromRuntime(ReadOnlySpan<byte> records, ReadOnlySpan<byte> indices)
    {
        if (records.Length % SacredScriptJournalLayout.SerializedSize != 0 || indices.Length != GoldActualQuestsLayout.Size)
            throw new InvalidDataException("Expected complete original journal records and sixteen index pairs.");
        var result = new WorldScriptQuestJournal(); indices.CopyTo(result._indices);
        for (var offset = 0; offset < records.Length; offset += SacredScriptJournalLayout.SerializedSize)
            result._records.Add(records.Slice(offset, SacredScriptJournalLayout.SerializedSize).ToArray());
        return result;
    }
    public byte[] GetWireRecord(int index)
    {
        var source = _records[index]; var result = new byte[GoldQuestLayout.JournalWireSize];
        source.AsSpan(0, SacredScriptJournalLayout.RuntimeFlagsOffset).CopyTo(result);
        source.AsSpan(SacredScriptJournalLayout.RuntimeFlagsOffset + SacredScriptJournalLayout.RuntimeFlagsSize)
            .CopyTo(result.AsSpan(SacredScriptJournalLayout.RuntimeFlagsOffset));
        return result;
    }
    public void UpdateBook(int questNumber,int mode,uint resource)
    {
        if (questNumber<0 || mode<0 || resource==0) return;
        var record=_records.FirstOrDefault(row=>BinaryPrimitives.ReadInt32LittleEndian(row.AsSpan(SacredScriptJournalLayout.QuestNumberOffset))==questNumber);
        if(record is null) return;
        if(mode%10==0) BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(SacredScriptJournalLayout.TitleResourceOffset),resource);
        else for(var index=0;index<SacredScriptJournalLayout.TextResourceCount;index++)
        {
            var slot=record.AsSpan(SacredScriptJournalLayout.TextResourcesOffset+index*4,4);
            if(BinaryPrimitives.ReadUInt32LittleEndian(slot)!=0)continue;
            BinaryPrimitives.WriteUInt32LittleEndian(slot,resource);break;
        }
    }
    public void AppendTriggerEntry(int questNumber, uint localPlayerSlot)
    {
        var slot = unchecked((ushort)(localPlayerSlot - 1));
        if (slot >= GoldActualQuestsLayout.SlotCount) slot = 0;
        var offset = slot * GoldActualQuestsLayout.RecordSize;
        var previous = BinaryPrimitives.ReadInt32LittleEndian(_indices.AsSpan(offset));
        var bytes = new byte[SacredScriptJournalLayout.SerializedSize];
        Word(SacredScriptJournalLayout.KindOffset, questNumber is > 0 and < 100 ? 3u : 4u);
        Word(SacredScriptJournalLayout.StateOffset, 2);
        Word(SacredScriptJournalLayout.QuestNumberOffset, unchecked((uint)questNumber));
        Word(SacredScriptJournalLayout.RuntimeFlagsOffset, 1);
        if ((uint)previous < _records.Count)
            _records[previous].AsSpan(SacredScriptJournalLayout.Native16CWordOffset,4)
                .CopyTo(bytes.AsSpan(SacredScriptJournalLayout.Native16CWordOffset));
        if (questNumber is not (> 0 and < 100)) offset += sizeof(uint);
        BinaryPrimitives.WriteInt32LittleEndian(_indices.AsSpan(offset), _records.Count);
        _records.Add(bytes);
        void Word(int at,uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at),value);
    }
}
