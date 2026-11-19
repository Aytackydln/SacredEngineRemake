using Sacred.Core.World;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Compared owned ambient 5FBA40/5FCCB0 ground placement. The owned
/// actor stays scheduled and is linked to its tile. Sector membership is a
/// separate runtime-interest gate applied by the caller.</summary>
internal static class WorldCreatureAmbientPlacement
{
    public static WorldCreatureInitialActor Place(WorldCreatureInitialActor actor, WorldCreatureLoadedPlacementPlan plan)
    {
        if (!actor.Node.Active || (actor.Base.Flags & 0x10000000) == 0 || actor.EquipmentReferences.Any(value => value != 0) ||
            plan.Position.Surface != 0 || plan.StaticSurfaceReference != 0 || plan.TriggerReference != 0 ||
            !WorldCreatureLoadedPlacement.SupportsTile(plan.Tile, plan.Position, plan.StaticSurfaceReference))
            throw new NotSupportedException($"Ambient placement requires the compared fresh owned, ordinary outdoor actor and actual tile: active {actor.Node.Active}, flags 0x{actor.Base.Flags:X8}, equipment {actor.EquipmentReferences.Count(value => value != 0)}, surface {plan.Position.Surface}, floor {plan.StaticSurfaceReference}, trigger {plan.TriggerReference}, behavior {plan.Tile.Properties.Behavior}.");
        var head = plan.Tile.WorldObjectChainHeadId;
        var placed = actor with
        {
            Base = actor.Base with { Sector = checked((ushort)plan.Position.Sector), X = plan.Position.X, Y = plan.Position.Y,
                Surface = 0, Flags = actor.Base.Flags & ~0x102u, Word34 = 0 },
            NextOnTile = head != 0 && head != actor.Base.ObjectReference ? head : actor.NextOnTile
        };
        plan.CommitHead(placed.Base.ObjectReference);
        return placed;
    }
}
