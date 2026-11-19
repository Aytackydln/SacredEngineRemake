using Sacred.GameplayHost.Equipment;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold C2 555230's two-phase result, resolving each ORIGINAL slot
/// once through the existing character plan. This avoids old/new ID collisions.
/// Every nonzero result must be the actual owned factory item from that source.
/// Identity binding does not perform model attachment or equipped calculation.</summary>
public static class WorldCreatureSavedEquipmentReferences
{
    public static WorldCreatureInitialActor Bind(WorldCreatureInitialActor actor, IReadOnlyDictionary<uint, uint> references,
        IReadOnlyDictionary<uint, WorldEquipmentInitialItem> items)
    {
        if (actor.SavedEquipmentReferencesBound) return actor;
        if (!actor.HeroPostLoadCompleted || actor.RestoredCreature is not { } saved || !actor.Node.Active ||
            actor.PartnerReference != 0 || actor.EquipmentReferences.Any(value => value != 0))
            throw new NotSupportedException("Saved equipment binding requires the current post-C0 hero and its original pending slots, without a mount or installed graph.");
        var equipment = new uint[saved.PendingEquipmentReferences.Count];
        for (var slot = 0; slot < equipment.Length; slot++)
        {
            var original = saved.PendingEquipmentReferences[slot];
            if (original == 0) continue;
            if (!references.TryGetValue(original, out var current) || !items.TryGetValue(current, out var item) ||
                !item.Node.Active || item.Physical.Base.ObjectReference != current || item.Physical.Base.Owner != actor.Base.Owner ||
                item.SavedState?.Source.BaseState.ObjectReference != original ||
                item.Physical.AttachmentParent != actor.Base.ObjectReference)
                throw new InvalidDataException("Saved equipment reference does not resolve to its actual owned attached-parent factory object.");
            equipment[slot] = current;
        }
        return actor with { EquipmentReferences = equipment, SavedEquipmentReferencesBound = true };
    }
}
