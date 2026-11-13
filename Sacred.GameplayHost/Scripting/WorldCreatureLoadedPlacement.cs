using Sacred.Core.World;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

public sealed record WorldCreatureLoadedPlacementPlan(GoldObjectPosition Position,uint StaticSurfaceReference,
    uint TriggerReference,WldxTileRecord Tile,Action<uint> CommitHead);

/// <summary>Compared 0x5FBA40 positioned factory continuation: initial ordinary
/// tile insertion, static floor link, trigger identity and inactive-sector node.</summary>
public static class WorldCreatureLoadedPlacement
{
    /// <summary>Explicit elevated blockers bypass native ground-floor probing;
    /// the complete assigned NPC caller preserves that floor and inserts normally.</summary>
    public static bool SupportsTile(WldxTileRecord tile, GoldObjectPosition position, uint floor) =>
        tile.Properties.Behavior == WldxTileBehavior.None ||
        position.Surface > 0 && floor != 0 && tile.Properties.Behavior is
            WldxTileBehavior.MovementBlockerA or WldxTileBehavior.MovementBlockerB;

    public static WorldCreatureInitialActor Place(WorldCreatureInitialActor actor,WorldCreatureLoadedPlacementPlan plan,
        WorldObjectActivationQueue activation)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(activation);
        if ((actor.Base.Flags & 2) == 0 || !actor.Node.Active || actor.EquipmentReferences.Any(value=>value!=0) ||
            (actor.Base.Flags & (0x90000410|GoldPhysicalStateLayout.ModelLoadedFlag)) != 0 ||
            !SupportsTile(plan.Tile,plan.Position,plan.StaticSurfaceReference))
            throw new NotSupportedException("Positioned factory needs its compared fresh unloaded actor and compared ordinary/elevated-blocker destination tile.");
        var head = plan.Tile.WorldObjectChainHeadId;
        var placed = actor with
        {
            Base = actor.Base with { Sector=checked((ushort)plan.Position.Sector),X=plan.Position.X,Y=plan.Position.Y,
                Surface=checked((byte)plan.Position.Surface),Flags=actor.Base.Flags & ~0x102u,Word34=plan.TriggerReference },
            StaticSurfaceReference=plan.StaticSurfaceReference,
            NextOnTile=head!=0 && head!=actor.Base.ObjectReference ? head : actor.NextOnTile
        };
        plan.CommitHead(placed.Base.ObjectReference);
        if (!activation.DeactivateInitial(placed)) throw new InvalidOperationException("Positioned initial NPC node is missing.");
        return placed;
    }
}
