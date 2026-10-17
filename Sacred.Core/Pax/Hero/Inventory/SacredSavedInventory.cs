using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Sacred.Core.Pax.Hero.Inventory;

/// <summary>Lossless Gold saved inventory, also used by its network writer. Reading does not reassign identity or object references.</summary>
public sealed class SacredSavedInventory
{
    private readonly byte[] _payload;
    private readonly int _scrollCountOffset;
    private SacredSavedInventory(byte[] payload, int quests, int scrolls, int scrollCountOffset, int size)
        => (_payload, QuestCount, ScrollCount, _scrollCountOffset, SerializedSize) = (payload, quests, scrolls, scrollCountOffset, size);
    public ushort InventoryId => U16(SacredSavedInventoryLayout.InventoryIdOffset);
    public ushort Flags => U16(SacredSavedInventoryLayout.FlagsOffset);
    public ushort Word04 => U16(SacredSavedInventoryLayout.Word04Offset);
    public ushort Word06 => U16(SacredSavedInventoryLayout.Word06Offset);
    public ushort Width => U16(SacredSavedInventoryLayout.WidthOffset);
    public ushort Height => U16(SacredSavedInventoryLayout.HeightOffset);
    public ushort ActiveSlot => U16(SacredSavedInventoryLayout.ActiveSlotOffset);
    public ushort VampireActiveSlot => U16(SacredSavedInventoryLayout.VampireActiveSlotOffset);
    public ushort SelectedWeaponSlot => U16(SacredSavedInventoryLayout.SelectedWeaponSlotOffset);
    public ushort SelectedCombatArtSlot => U16(SacredSavedInventoryLayout.SelectedCombatArtSlotOffset);
    public uint GetWeaponSetReference(int slot, bool mainHand)
    {
        var index = checked(slot * SacredSavedItemBarRecordLayout.HandsPerSet +
            (mainHand ? SacredSavedItemBarRecordLayout.MainHandIndex : SacredSavedItemBarRecordLayout.OffhandIndex));
        return BinaryPrimitives.ReadUInt32LittleEndian(GetItemBarRecord(index));
    }
    public uint OwnerReference => U32(SacredSavedInventoryLayout.OwnerReferenceOffset);
    public int QuestCount { get; }
    public int ScrollCount { get; }
    public int SerializedSize { get; }
    public ReadOnlySpan<byte> RemainingData => _payload.AsSpan(SerializedSize);
    public uint GetKeyWord(int index) => U32(Offset(index, SacredSavedInventoryLayout.KeyCount, SacredSavedInventoryLayout.KeysOffset, 4));
    public SacredSavedInventoryCellLayout GetCell(int index) => MemoryMarshal.Read<SacredSavedInventoryCellLayout>(
        _payload.AsSpan(Offset(index, SacredSavedInventoryLayout.CellCount, SacredSavedInventoryLayout.CellsOffset, SacredSavedInventoryLayout.CellSize)));
    public ReadOnlySpan<byte> GetItemBarRecord(int index) => _payload.AsSpan(
        Offset(index, SacredSavedInventoryLayout.ItemBarCount, SacredSavedInventoryLayout.ItemBarOffset, SacredSavedInventoryLayout.ItemBarRecordSize),
        SacredSavedInventoryLayout.ItemBarRecordSize);
    public ReadOnlySpan<byte> GetSpellBarRecord(int index) => _payload.AsSpan(
        Offset(index, SacredSavedInventoryLayout.SpellBarCount, SacredSavedInventoryLayout.SpellBarOffset, SacredSavedInventoryLayout.SpellBarRecordSize),
        SacredSavedInventoryLayout.SpellBarRecordSize);
    public ReadOnlySpan<byte> GetQuestRecord(int index) => _payload.AsSpan(
        Offset(index, QuestCount, SacredSavedInventoryLayout.QuestRecordsOffset, SacredSavedInventoryLayout.QuestRecordSize), SacredSavedInventoryLayout.QuestRecordSize);
    public uint GetScrollWord(int index) => U32(Offset(index, ScrollCount, _scrollCountOffset + 4, 4));
    public byte[] ToPayload() => _payload.ToArray();

    public static SacredSavedInventory FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SacredSavedInventoryLayout.MinimumSize) throw new InvalidDataException("Truncated Gold inventory.");
        var quests = BinaryPrimitives.ReadUInt32LittleEndian(payload[SacredSavedInventoryLayout.QuestCountOffset..]);
        var scrollOffset = (ulong)SacredSavedInventoryLayout.QuestRecordsOffset + (ulong)quests * SacredSavedInventoryLayout.QuestRecordSize;
        if (scrollOffset + 4 > (ulong)payload.Length) throw new InvalidDataException("Truncated Gold inventory quest records.");
        var scrolls = BinaryPrimitives.ReadUInt32LittleEndian(payload[(int)scrollOffset..]);
        var size = scrollOffset + 4 + (ulong)scrolls * SacredSavedInventoryLayout.ScrollRecordSize;
        if (size > (ulong)payload.Length) throw new InvalidDataException("Truncated Gold inventory scroll records.");
        return new(payload.ToArray(), (int)quests, (int)scrolls, (int)scrollOffset, (int)size);
    }

    private ushort U16(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(_payload.AsSpan(offset));
    private uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(offset));
    private static int Offset(int index, int count, int start, int stride)
    {
        if ((uint)index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        return start + index * stride;
    }
}
