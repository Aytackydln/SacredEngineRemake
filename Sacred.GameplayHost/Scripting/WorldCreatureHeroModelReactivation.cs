using System.Numerics;
using Sacred.Core.Pax.Hero.Objects;
using Sacred.GameplayHost.Runtime;
using Sacred.GameplayHost.Equipment;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 606CEE..606D06 with loaded body and unloaded saved item models.
/// Actual body-resource/pose ownership is the caller's obligation. This does not
/// claim loaded equipment attachment or equipped-result/entry completion.</summary>
public static class WorldCreatureHeroModelReactivation
{
    public static WorldCreatureInitialActor Prepare(WorldCreatureInitialActor actor,
        IReadOnlyDictionary<uint, WorldEquipmentInitialItem> items, Vector3 modelRoot, SacredObjectProjection projection)
    {
        if (actor.ModelReactivated) return actor;
        if (!actor.HeroPostLoadCompleted || !actor.SavedEquipmentReferencesBound || !actor.Node.Active ||
            actor.RestoredPhysical is null || actor.RestoredCreature is not { } saved || actor.KiMode != 0 || actor.EiMode != 0 ||
            actor.PartnerReference != 0 || actor.Base.Owner != actor.Base.ObjectReference || (actor.Base.Flags & 0x04000000) == 0 ||
            saved.Short(SacredSavedCreatureLayout.AuxiliaryPhysicalReferenceOffset) != 0 ||
            Enumerable.Range(0, 3).Any(index => saved.Word(SacredSavedCreatureLayout.SecondaryAnimationControlOffset + index * 4) != 0))
            throw new NotSupportedException("Model reactivation requires the current post-C0 body, bound references and empty auxiliary control/mount actions.");
        foreach (var reference in actor.EquipmentReferences.Where(reference => reference != 0))
            if (!items.TryGetValue(reference, out var item) || !item.Node.Active || !item.SavedPostLoadCompleted ||
                item.Physical.AttachmentParent != actor.Base.ObjectReference || (item.Physical.Base.Flags & 0x04000000) != 0)
                throw new NotSupportedException("Reactivation requires its actual normalized bound items with unloaded models; loaded binding remains separate.");
        return actor with
        {
            ModelReactivated = true,
            RestoredPhysical = actor.RestoredPhysical with
            {
                Animation = 0, LoopCount = 0, PlaybackSpeed = 0, RootOffset = modelRoot,
                // 44C330 initializes the new body sequence with this model direction;
                // the logical saved direction is unchanged until the later event 0x40.
                ModelOrientation = WorldCreatureModelOrientation.Create(actor.Base, new GoldPhysicalVector(0, 1, 0), projection)
            }
        };
    }

    public static void CompleteUnloadedSlots(WorldCreatureInitialActor actor, IReadOnlyDictionary<uint, WorldEquipmentInitialItem> items)
    {
        if (!actor.ModelReactivated) throw new InvalidOperationException("Slot cleanup follows the completed body reactivation.");
        foreach (var reference in actor.EquipmentReferences.Where(reference => reference != 0))
            if (!items.TryGetValue(reference, out var item) || !item.Node.Active || !item.SavedPostLoadCompleted ||
                item.Physical.AttachmentParent != actor.Base.ObjectReference || (item.Physical.Base.Flags & 0x04000000) != 0)
                throw new NotSupportedException("Slot cleanup requires the same actual unloaded saved-item graph.");
        foreach (var reference in actor.EquipmentReferences.Where(reference => reference != 0))
        {
            var item = items[reference];
            // 555300 -> 44D7B0, then 5559B8. Direction, parent and values survive.
            item.Physical.Vector7C = default;
            item.Physical.Vector88 = default;
            item.Physical.Base = item.Physical.Base with { Flags = item.Physical.Base.Flags & ~0x200u };
        }
    }
}
