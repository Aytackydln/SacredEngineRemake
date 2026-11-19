using Sacred.Core.Pak.Items;
using Sacred.GameplayHost.Equipment;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Reached 54D6E0(true) on an unloaded ordinary member. 450960(true)
/// has no model work. 550470(true) deactivates an already-active equipped Weapon,
/// then its false placement callback retires no FX in this initial state. Other
/// categories, inactive equipment and loaded models remain separate dependencies.</summary>
public static class WorldCreatureSectorReactivation
{
    public static void Require(WorldCreatureInitialActor actor, WorldObjectActivationQueue scheduler,
        IReadOnlyDictionary<uint, WorldEquipmentInitialItem>? items)
    {
        if (actor.Node.Active) return;
        if ((actor.Base.Flags & GoldPhysicalStateLayout.ModelLoadedFlag) != 0 || actor.Node.QueueKind != 1)
            throw new NotSupportedException("Inactive sector member requires its loaded-model or alternate scheduler activation callback.");
        var seen = new HashSet<uint>();
        foreach (var reference in actor.EquipmentReferences.Where(value => value != 0))
        {
            if (!seen.Add(reference) || items is null || !items.TryGetValue(reference, out var item) ||
                item.Physical.Base.ObjectReference != reference || !item.Generated || item.SavedState is not null ||
                item.ResourceCategory != SacredItemCategory.Weapon || !item.Node.Active ||
                item.Physical.AttachmentParent != actor.Base.ObjectReference ||
                (item.Physical.Base.Flags & GoldPhysicalStateLayout.ModelLoadedFlag) != 0 || !scheduler.Nodes.Contains(item.Node))
                throw new NotSupportedException($"Sector member {actor.Base.ObjectReference} requires its actual initial active/unloaded attached Weapon {reference}; other equipment activation remains guarded.");
        }
    }

    public static bool Activate(WorldCreatureInitialActor actor, WorldObjectActivationQueue scheduler,
        IReadOnlyDictionary<uint, WorldEquipmentInitialItem>? items, double clock)
    {
        Require(actor, scheduler, items);
        return scheduler.ActivateExisting(actor.Node, clock, () =>
        {
            foreach (var reference in actor.EquipmentReferences.Where(value => value != 0))
                if (!scheduler.DeactivateInitial(items![reference].Node))
                    throw new InvalidOperationException("The validated attached item's original activation record is no longer current.");
            // Unloaded 450960/5BE5F0(false)/5CDB60(false) leave these initial
            // item's coordinates, attachment, values and graphics bytes unchanged.
        });
    }
}
