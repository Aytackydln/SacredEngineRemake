using System.Buffers.Binary;
using Sacred.Core.Pax.Hero.Objects;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Supported fresh first-host branch of 606C1D..606CEE. World lookup
/// and its real load/reinsertion effects must occur between these operations.</summary>
public static class WorldCreatureHostWorldPrelude
{
    public static WorldCreatureInitialActor BeginFreshWorld(WorldCreatureInitialActor actor)
    {
        if (!actor.HeroPostLoadCompleted || !actor.SavedEquipmentReferencesBound || actor.ModelReactivated ||
            actor.RestoredPhysical is null || actor.RestoredCreature is null || !actor.Node.Active ||
            actor.KiMode != 0 || actor.EiMode != 0 || actor.PartnerReference != 0 ||
            BinaryPrimitives.ReadInt32LittleEndian(actor.Component.Health) <= 0 ||
            BinaryPrimitives.ReadInt32LittleEndian(actor.Component.Health.AsSpan(4)) <= 0)
            throw new NotSupportedException("Saved-world prelude requires the current healthy restored hero before body reactivation; other health/action paths remain guarded.");
        // Original fresh cWorld +0x24C vector is empty; its KEYX pointer table
        // is nonempty, so 63B540 returns false. No restored-world snapshot exists
        // in this first-host path. This is independent of the loaded tile cache.
        return actor with { Flags1FC = (ushort)(actor.Flags1FC | SacredSavedCreatureLayout.PartialWorldStateFlag) };
    }

    public static WorldCreatureInitialActor CompleteOutdoor(WorldCreatureInitialActor actor)
    {
        if ((actor.Flags1FC & SacredSavedCreatureLayout.PartialWorldStateFlag) == 0 || actor.RestoredPhysical is null)
            throw new InvalidOperationException("Actual saved-world lookup/reinsertion must precede the final prelude action.");
        var equipment = actor.EquipmentReferences.ToArray();
        // Actual parameter 9 clears native +1EC (slot18) and palette +5C.
        equipment[SacredSavedCreatureLayout.SectorContextReferenceIndex] = 0;
        return actor with { EquipmentReferences = equipment, RestoredPhysical = actor.RestoredPhysical with { PaletteMask = 0 },
            SavedWorldPreludeCompleted = true };
    }
}
