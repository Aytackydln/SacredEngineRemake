namespace Sacred.Network.Gameplay;

/// <summary>Original expected-reference guard; inventory placement and derived recalculation are separate work.</summary>
internal static class GoldEquipmentRemovalReducer
{
    public static GoldReplicatedObject? Apply(GoldReplicatedObject actor, GoldEquipmentRemovalEvent removal)
    {
        if (actor.Creature is not { } creature || (actor.BaseState.Flags & 0x10) != 0 ||
            removal.Slot >= GoldEquipmentRemovalLayout.SlotCount) return null;
        var slot = (int)removal.Slot;
        var current = creature.EquipmentReferences[slot];
        if (current == 0 || removal.ExpectedItemReference != 0 && current != removal.ExpectedItemReference) return null;
        return actor with { Creature = creature.WithEquipmentReference(slot, 0) };
    }
}
