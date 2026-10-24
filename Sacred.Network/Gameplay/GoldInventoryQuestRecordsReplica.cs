using Sacred.Core.Pax.Hero.Inventory;

namespace Sacred.Network.Gameplay;

/// <summary>145 quest-pair deltas over a known 122 baseline. A newer full inventory supersedes these changes.</summary>
public sealed class GoldInventoryQuestRecordsReplica
{
    private readonly Dictionary<uint, (SacredSavedInventory Baseline, SacredSavedInventory Current)> _inventories = [];
    public long AppendedCount { get; private set; }
    public long SuppressedCount { get; private set; }
    internal SacredSavedInventory? Find(uint id, SacredSavedInventory? baseline)
    {
        if (_inventories.TryGetValue(id, out var state))
        {
            if (ReferenceEquals(state.Baseline, baseline)) return state.Current;
            _inventories.Remove(id);
        }
        return baseline;
    }
    internal void Apply(uint id, SacredSavedInventory inventory, uint key, uint word04)
    {
        for (var i = 0; i < inventory.QuestCount; i++)
        {
            var row = SacredSavedInventoryQuestRecords.Read(inventory, i);
            if (row.KeyWord == key || row.Word04 == 0) { SuppressedCount++; return; }
        }
        var baseline = _inventories.TryGetValue(id, out var state) && ReferenceEquals(state.Current, inventory) ? state.Baseline : inventory;
        _inventories[id] = (baseline, SacredSavedInventoryQuestRecords.Append(inventory, key, word04)); AppendedCount++;
    }
    internal void Clear() { _inventories.Clear(); AppendedCount = SuppressedCount = 0; }
}
