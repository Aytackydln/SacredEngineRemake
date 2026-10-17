namespace Sacred.Network.Gameplay;

/// <summary>Verified reference/position writes. Inventory placement and native actor callbacks remain separate.</summary>
internal static class GoldEquipmentAssignmentReducer
{
    public static bool Apply(Dictionary<uint, GoldReplicatedObject> objects, GoldEquipmentAssignmentEvent entry,
        Func<uint, bool> isHorseResource)
    {
        if (entry.Slot >= GoldCreatureStateLayout.EquipmentCount ||
            !objects.TryGetValue(entry.CreatureReference, out var actor) || actor.Creature is not { } creature ||
            (actor.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0) return false;
        var special = entry.Slot == GoldEquipmentAssignmentLayout.SpecialSlot;
        if (!special && entry.Slot != 0 && isHorseResource(actor.BaseState.ObjectType)) return false;
        var flags = actor.BaseState.Flags;
        if (special && entry.ItemReference != 0) flags |= GoldEquipmentAssignmentLayout.SpecialSlotActorFlag;
        var changed = false;
        if (creature.EquipmentReferences[(int)entry.Slot] != entry.ItemReference || flags != actor.BaseState.Flags)
        {
            objects[entry.CreatureReference] = actor with
            {
                Creature = creature.WithEquipmentReference((int)entry.Slot, entry.ItemReference),
                BaseState = actor.BaseState with { Flags = flags }
            };
            changed = true;
        }
        // The native routine stores unresolved references too. Resolve their appearance when the object arrives.
        if (!special && entry.ItemReference != 0 && objects.TryGetValue(entry.ItemReference, out var item) && item.Item is not null)
        {
            var state = item.BaseState with
            {
                Flags = (item.BaseState.Flags & ~GoldEquipmentAssignmentLayout.ItemAssignmentClearFlag) |
                    ((item.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) == 0 ? GoldEquipmentAssignmentLayout.ItemOutsideWorldFlag : 0),
                Sector = 0, X = 0, Y = 0, Surface = 0
            };
            if (state != item.BaseState)
            {
                objects[entry.ItemReference] = item with { BaseState = state };
                changed = true;
            }
        }
        return changed;
    }
}
