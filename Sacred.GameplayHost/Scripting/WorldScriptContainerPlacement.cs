using Sacred.Core.World;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 5FBA40/5FCCB0 fresh outdoor Container insertion. Containers
/// retain their active scheduler node and are excluded by 428710 from creature
/// sector membership. Indoor ordinary/blocker cells retain the resolved floor;
/// other special tile creation remains guarded.</summary>
internal static class WorldScriptContainerPlacement
{
    public static void Require(WorldCreatureLoadedPlacementPlan plan)
    {
        var elevated = plan.Position.Surface != 0 && plan.StaticSurfaceReference != 0;
        if ((!elevated && (plan.Position.Surface != 0 || plan.StaticSurfaceReference != 0)) ||
            (plan.Tile.Properties.Behavior != WldxTileBehavior.None &&
                !(elevated && plan.Tile.Properties.Behavior is WldxTileBehavior.MovementBlockerA or WldxTileBehavior.MovementBlockerB)))
            throw new NotSupportedException("Loaded Container creation requires its remaining floor/special-tile lifecycle.");
    }

    public static void PlaceOnFloor(WorldScriptCreatedObject value, WorldCreatureLoadedPlacementPlan plan)
    {
        Require(plan);
        if (!value.Node.Active || (value.BaseState.Flags & 2) == 0 || value.StaticSurfaceReference != 0)
            throw new InvalidOperationException("Loaded Container needs its actual fresh active constructor object.");
        var state = value.BaseState;
        value.BaseState = state with { Sector = checked((ushort)plan.Position.Sector), X = plan.Position.X,
            Y = plan.Position.Y, Surface = checked((byte)plan.Position.Surface),
            Word34 = plan.TriggerReference, Flags = state.Flags & ~0x102u };
        value.StaticSurfaceReference = plan.StaticSurfaceReference;
        var head = plan.Tile.WorldObjectChainHeadId;
        if (head != 0 && head != state.ObjectReference) value.NextOnTile = head;
        plan.CommitHead(state.ObjectReference);
    }
}
