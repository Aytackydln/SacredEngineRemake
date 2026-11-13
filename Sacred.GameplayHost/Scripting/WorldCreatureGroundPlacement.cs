using Sacred.Core.World;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

public sealed record WorldCreatureGroundPlacementResult(WorldCreatureInitialActor Actor, uint TileHead, bool SectorChanged);

/// <summary>Gold 0x5FCCB0 ground path. Caller must resolve/validate the original
/// world tile first and commit the returned head to that same runtime tile.
/// Elevated, special-tile, inventory, owned and parent-linked paths are excluded.</summary>
public static class WorldCreatureGroundPlacement
{
    public static WorldCreatureGroundPlacementResult Place(WorldCreatureInitialActor actor,
        GoldObjectPosition position, WldxTileRecord tile, uint currentTileHead)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.Base.ObjectReference <= 16 || (actor.Base.Flags & 2) == 0 ||
            (actor.Base.Flags & (0x80000410 | GoldPhysicalStateLayout.ModelLoadedFlag)) != 0 ||
            position.Surface != 0 || position.Sector > ushort.MaxValue ||
            (tile.Properties.Behavior != WldxTileBehavior.None &&
                (tile.Properties.Behavior != WldxTileBehavior.IndoorExit || actor.StaticSurfaceReference != 0)))
            throw new NotSupportedException($"Ground placement requires an unplaced ordinary NPC and a validated original ground tile (ref {actor.Base.ObjectReference}, flags 0x{actor.Base.Flags:X8}, surface {position.Surface}, tile {tile.Properties.Behavior}, at {position.X},{position.Y}, sector {position.Sector}).");
        var reference = actor.Base.ObjectReference;
        var placed = actor with
        {
            Base = actor.Base with { Sector = (ushort)position.Sector, X = position.X, Y = position.Y,
                Surface = 0, Flags = actor.Base.Flags & ~0x102u },
            NextOnTile = currentTileHead != 0 && currentTileHead != reference ? currentTileHead : actor.NextOnTile
        };
        return new(placed, reference, actor.Base.Sector != position.Sector);
    }
}
