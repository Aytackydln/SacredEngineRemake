using Sacred.Core.Pak.Items;
using Sacred.GameplayHost.Equipment;
using Sacred.GameplayHost.Runtime;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 606D06..606F33 with actual normalized unloaded saved items.
/// The caller must fix both inventories between item cleanup and the heading
/// event. This private stage does not establish the preceding world-surface
/// prelude, placement, publication or readiness.</summary>
public static class WorldCreatureSavedLoaderTail
{
    public static WorldCreatureInitialActor Prepare(WorldCreatureInitialActor actor,
        IReadOnlyDictionary<uint, WorldEquipmentInitialItem> items, ItemsPakEntry descriptor,
        Func<uint, ItemsPakEntry> itemDescriptor, Func<uint, ushort, uint> defaultResource,
        SacredObjectProjection projection)
    {
        if (actor.SavedLoaderTailPrepared) return actor;
        if (!actor.ModelReactivated || descriptor.ItemIndex != actor.Base.ObjectType ||
            actor.RestoredPhysical is not { Animation: 0 } || actor.RestoredCreature is null ||
            actor.KiMode != 0 || actor.EiMode != 0 || actor.PartnerReference != 0 || !actor.Node.Active)
            throw new NotSupportedException("Saved-loader tail requires its current reactivated body and supported action/mount state.");
        for (ushort slot = 0; slot < actor.EquipmentReferences.Length; slot++)
        {
            var reference = actor.EquipmentReferences[slot];
            if (reference == 0)
            {
                if (defaultResource(actor.Base.ObjectType, slot) != 0)
                    throw new NotSupportedException("Empty saved slot requires its original detach/default factory action.");
                continue;
            }
            if (!items.TryGetValue(reference, out var item) || !item.Node.Active || !item.SavedPostLoadCompleted ||
                item.Physical.Base.ObjectReference != reference || item.Physical.Base.Owner != actor.Base.Owner ||
                item.Physical.AttachmentParent != actor.Base.ObjectReference ||
                (item.Physical.Base.Flags & (2 | 0x04000000)) != 2 ||
                itemDescriptor(item.Physical.Base.ObjectType).ModelDesc.RejectedFromSavedHeroEquipment)
                throw new NotSupportedException("Saved slot requires its actual owned unloaded item; retirement, invalid-category detach and loaded binding remain guarded.");
        }
        var facing = SacredStartupHeading.ToDirection(descriptor.ModelDesc.Angle3D);
        var direction = new GoldPhysicalVector(facing.X, facing.Y, facing.Z);
        return actor with
        {
            Direction = direction, SavedLoaderTailPrepared = true,
            RestoredPhysical = actor.RestoredPhysical with
            {
                ModelOrientation = WorldCreatureModelOrientation.Create(actor.Base, direction, projection)
            }
        };
    }

    /// <summary>Called only after Prepare and both current inventory fixes validate.</summary>
    public static void CommitItems(WorldCreatureInitialActor prepared, IReadOnlyDictionary<uint, WorldEquipmentInitialItem> items)
    {
        if (!prepared.SavedLoaderTailPrepared) throw new InvalidOperationException("Final slot validation must precede its mutations.");
        WorldCreatureHeroModelReactivation.CompleteUnloadedSlots(prepared, items);
        foreach (var reference in prepared.EquipmentReferences.Where(reference => reference != 0))
        {
            var item = items[reference];
            item.Physical.Base = item.Physical.Base with { Sector = 0, X = 0, Y = 0, Surface = 0 };
        }
    }
}
