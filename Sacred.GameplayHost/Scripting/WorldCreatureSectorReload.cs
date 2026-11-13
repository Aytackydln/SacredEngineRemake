using Sacred.Core.World;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x637AF0 dormant ground NPC re-insertion after tiles load.
/// The caller walks references in ascending order, removes previous membership,
/// commits each returned tile head, then applies sector script patches.</summary>
public static class WorldCreatureSectorReload
{
    public static WorldCreatureGroundPlacementResult Reinsert(WorldCreatureInitialActor actor,
        WldxTileRecord tile, uint currentTileHead)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.Node.Active ||
            (actor.Base.Flags & 2) != 0 || actor.Base.Surface != 0)
            throw new NotSupportedException($"Sector reload needs the compared dormant, placed ground NPC (ref {actor.Base.ObjectReference}, type {actor.Base.ObjectType}, at {actor.Base.X},{actor.Base.Y}, surface {actor.Base.Surface}, flags 0x{actor.Base.Flags:X8}, active {actor.Node.Active}, floor {actor.StaticSurfaceReference}).");
        var position = new GoldObjectPosition(actor.Base.X, actor.Base.Y, actor.Base.Surface, actor.Base.Sector);
        // Native detach clears the old tile link and marks the actor unplaced;
        // the callback clears the sector before the normal placement call.
        var detached = actor with { Base = actor.Base with { Flags = actor.Base.Flags | 2u, Sector = 0 }, NextOnTile = 0 };
        return WorldCreatureGroundPlacement.Place(detached, position, tile, currentTileHead);
    }
    /// <summary>Compared elevated reload with an absent or matching existing floor link. The caller supplies
    /// original static-floor selection and commits the returned indoor head.</summary>
    public static WorldCreatureGroundPlacementResult ReinsertIndoor(WorldCreatureInitialActor actor,
        WldxTileRecord tile, uint currentTileHead, uint floor)
    {
        if (actor.Node.Active || (actor.Base.Flags & (2u | 0x80000410u | GoldPhysicalStateLayout.ModelLoadedFlag)) != 0 ||
            actor.Base.Surface == 0 || (actor.StaticSurfaceReference != 0 && actor.StaticSurfaceReference != floor) || floor == 0 || tile.Properties.Behavior != WldxTileBehavior.None)
            throw new NotSupportedException("Elevated reload requires a dormant unlinked actor and its original ordinary indoor floor.");
        var placed = actor with
        {
            Base = actor.Base with { Flags = actor.Base.Flags & ~0x102u },
            StaticSurfaceReference = floor,
            NextOnTile = currentTileHead != 0 && currentTileHead != actor.Base.ObjectReference ? currentTileHead : 0
        };
        return new(placed,actor.Base.ObjectReference,true);
    }

}
