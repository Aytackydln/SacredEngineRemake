using Sacred.Core.GameBin.Scripts;
using Sacred.Core.World;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

public sealed partial class WorldScriptEffectWorld
{
    internal WorldCreatureLoadedPlacementPlan PrepareEntered(WorldScriptExecutionContext context,
        SacredScriptCommand command, WorldScriptSectorEnvironment environment, WorldScriptCreatureWorld world)
    {
        var creation = WorldScriptEnteredFixedEffect.Read(command);
        if (!context.Multiplayer || context.HasActiveHero != true || context.ObjectActionFlags != 0 ||
            creation.TypeId != _definitions.Resource ||
            context.CurrentSectorRuntimeFlags is not { } flags || (flags & GoldSectorEntryRuntime.EnteredFlag) == 0)
            throw new NotSupportedException("Entered fixed Effect requires its actual multiplayer hero, original descriptor and entered-sector runtime.");
        var source = creation.WorldPosition!.Value;
        var sector = _grid.GetSector(SacredWorldCoordinates.WorldToTile(source.X), SacredWorldCoordinates.WorldToTile(source.Y));
        if (sector != environment.Sector || world.Terrain is not { } terrain || !terrain.Tiles.IsLoaded(sector))
            throw new NotSupportedException("Entered fixed Effect requires its same actual loaded callback sector.");
        var plan = terrain.PrepareContainerPlacement(context, new(source.X, source.Y, 0, sector));
        if (plan.StaticSurfaceReference != 0 || plan.TriggerReference != 0 || plan.Tile.WorldObjectChainHeadId != 0)
            throw new NotSupportedException("Entered fixed Effect requires its compared unoccupied outdoor cell without a floor trigger.");
        _ = _references.PeekAvailable(1);
        return plan;
    }

    internal void CreateEntered(WorldScriptExecutionContext context, SacredScriptCommand command, WorldScriptCreatureWorld world)
    {
        if (!context.IsEnteringSector || context.CurrentSector is not { } environment || context.IsExecutingStartup)
            throw new InvalidOperationException("Entered Effect needs the actual EnterSector invocation scope.");
        var plan = PrepareEntered(context, command, environment, world);
        var creation = WorldScriptEnteredFixedEffect.Read(command);
        var resource = world.ItemResources.Resolve(creation.Name);
        if (resource != GoldScriptResourceLayout.UnnamedResource)
            throw new InvalidOperationException("NON_UNIQUE must retain the native unnamed resource sentinel.");
        var projected = _projection.Project(plan.Position.X, plan.Position.Y, 0);
        if (!_references.TryAllocate(out var reference)) throw new InvalidDataException("Entered Effect needs its actual 0x153 reservation.");
        _counter.BeginAllocation();
        var node = new WorldObjectNode { PeriodMilliseconds = _definitions.NodePeriod };
        _activation.ActivateInitial(node, world.SchedulerClock);
        var identity = new GoldObjectBaseState(reference, creation.TypeId, _definitions.BaseFlags | 0x10010000u,
            checked((ushort)plan.Position.Sector), plan.Position.X, plan.Position.Y, 0, 0, 0, _counter.Value, _runtime.Owner, 0);
        var state = WorldFixedEffectSerialization.Create(_definitions, identity, projected, _random);
        var publication = GoldObjectStateCodec.Create(0x16f, _runtime.Owner, state.ToPayload());
        var created = new WorldScriptCreatedEffect(command.FileOffset, state, node, publication, _random.State)
        {
            RuntimeState = state.BaseState with { Flags = state.BaseState.Flags | 0x80000000u },
            NextOnTile = plan.Tile.WorldObjectChainHeadId
        };
        plan.CommitHead(reference);
        world.Terrain!.Membership.Move(reference, 0, created.BaseState.Sector);
        _created.Add(created);
        _log($"Entered Effect created: ref {reference}, type {creation.TypeId}, sector {created.BaseState.Sector}, active node; one complete 0x16F awaiting transport.");
    }
}
