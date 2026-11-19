using Sacred.Core.World;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Compared local-hero 5FCCB0 ground path. Real tile loading, retirement,
/// chain commits and sector membership are the caller's obligations. Loaded
/// model/control state and equipment survive these logical placement calls.</summary>
public static class WorldCreatureHostGroundPlacement
{
    public static WorldCreatureInitialActor Place(WorldCreatureInitialActor actor, GoldObjectPosition position,
        WldxTileRecord tile, uint currentHead)
    {
        if (actor.Base.ObjectReference is 0 or > 16 || actor.Base.Owner != actor.Base.ObjectReference ||
            !actor.Node.Active || actor.RestoredPhysical is null || !actor.SavedEquipmentReferencesBound ||
            (actor.Base.Flags & (2 | 0x80000410u)) != 2 || actor.PartnerReference != 0 ||
            actor.KiMode != 0 || actor.EiMode != 0 || position.Surface != 0 || position.Sector is 0 or > ushort.MaxValue ||
            tile.Properties.Behavior != WldxTileBehavior.None)
            throw new NotSupportedException("Host placement requires the current actual unplaced local hero and original ordinary outdoor tile.");
        return actor with
        {
            Base = actor.Base with { Sector = (ushort)position.Sector, X = position.X, Y = position.Y,
                Surface = 0, Flags = actor.Base.Flags & ~0x102u },
            StaticSurfaceReference = 0,
            NextOnTile = currentHead != 0 && currentHead != actor.Base.ObjectReference ? currentHead : actor.NextOnTile
        };
    }
}
