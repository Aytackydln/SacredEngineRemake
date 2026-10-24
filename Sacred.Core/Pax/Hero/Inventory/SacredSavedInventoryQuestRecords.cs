using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Sacred.Core.Pax.Hero.Inventory;

/// <summary>Lossless operations on the existing saved quest-pair vector; scrolls and trailing bytes retain their order.</summary>
public static class SacredSavedInventoryQuestRecords
{
    public static SacredSavedInventoryQuestRecordLayout Read(SacredSavedInventory inventory, int index) =>
        MemoryMarshal.Read<SacredSavedInventoryQuestRecordLayout>(inventory.GetQuestRecord(index));
    public static SacredSavedInventory Append(SacredSavedInventory inventory, uint keyWord, uint word04)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var original = inventory.ToPayload();
        var offset = checked(SacredSavedInventoryLayout.QuestRecordsOffset + inventory.QuestCount * SacredSavedInventoryQuestRecordLayout.Size);
        var bytes = new byte[checked(original.Length + SacredSavedInventoryQuestRecordLayout.Size)];
        original.AsSpan(0, offset).CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(SacredSavedInventoryLayout.QuestCountOffset), checked(inventory.QuestCount + 1));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), keyWord);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 4), word04);
        original.AsSpan(offset).CopyTo(bytes.AsSpan(offset + SacredSavedInventoryQuestRecordLayout.Size));
        return SacredSavedInventory.FromPayload(bytes);
    }
}
