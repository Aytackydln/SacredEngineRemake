using Sacred.Core.Pax.Hero.Inventory;

namespace Sacred.Network.Gameplay;

/// <summary>Gold Engine parameter 0x1D -> 5D8520 clears the primary quest vector.
/// Fixed fields, scrolls and all other inventory state are preserved.</summary>
public static class GoldSavedInventoryQuestReset
{
    public static SacredSavedInventory Apply(SacredSavedInventory source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.RemainingData.IsEmpty) throw new InvalidDataException("Quest reset requires a fully accounted inventory.");
        if (source.QuestCount == 0) return source;
        var bytes = source.ToPayload();
        var result = new byte[checked(bytes.Length - source.QuestCount * SacredSavedInventoryLayout.QuestRecordSize)];
        bytes.AsSpan(0, SacredSavedInventoryLayout.QuestCountOffset).CopyTo(result);
        // The new count remains zero. Copy the original scroll count/list.
        bytes.AsSpan(SacredSavedInventoryLayout.QuestRecordsOffset + source.QuestCount * SacredSavedInventoryLayout.QuestRecordSize)
            .CopyTo(result.AsSpan(SacredSavedInventoryLayout.QuestRecordsOffset));
        return SacredSavedInventory.FromPayload(result);
    }
}
