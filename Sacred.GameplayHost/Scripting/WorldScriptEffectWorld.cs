using Sacred.Assets.Executable;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.Core.World;
using Sacred.GameplayHost.Runtime;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Compared initial fixed, MAGICPRISON and BUBBLEPRISON Effect producers. Owns no
/// transport; complete preparations stay private until campaign upload. Attached,
/// deferred and other-family effects require their original lifecycle services.</summary>
public sealed class WorldScriptEffectWorld
{
    private readonly SacredFixedEffectDefinitions _definitions;
    private readonly SacredObjectProjection _projection;
    private readonly GoldObjectReferencePool _references;
    private readonly WorldObjectAllocationCounter _counter;
    private readonly WorldObjectActivationQueue _activation;
    private readonly SacredRandomSequence _random;
    private readonly WorldScriptCreatureRuntime _runtime;
    private readonly SacredSectorGrid _grid;
    private readonly Action<string> _log;
    private readonly List<WorldScriptCreatedEffect> _created = [];
    public WorldScriptEffectWorld(SacredFixedEffectDefinitions definitions, SacredObjectProjection projection,
        GoldObjectReferencePool references, WorldObjectAllocationCounter counter, WorldObjectActivationQueue activation,
        SacredRandomSequence random, WorldScriptCreatureRuntime runtime, SacredSectorGrid grid, Action<string> log)
    {
        (_definitions,_projection,_references,_counter,_activation,_random,_runtime,_grid,_log) =
            (definitions,projection,references,counter,activation,random,runtime,grid,log);
        Created = _created.AsReadOnly();
    }
    public IReadOnlyList<WorldScriptCreatedEffect> Created { get; }
    public SacredMagicPrisonDefinitions? MagicPrison { get; init; }
    public SacredBubblePrisonDefinitions? BubblePrison { get; init; }
    public void Create(SacredScriptCommand command, SacredScriptCreateObject creation, ItemsPakEntry descriptor)
        => Create(command, creation, descriptor, null, 0, _ => null);

    public void Create(SacredScriptCommand command, SacredScriptCreateObject creation, ItemsPakEntry descriptor,
        WorldScriptItemResources? resources, uint interpreterFlags, Func<uint, uint?> findObject)
    {
        if (MagicPrison is { } prison && creation.TypeId == prison.Resource)
        {
            var detail = _runtime.EffectDetailLevel ?? prison.DefaultDetailLevel;
            _ = prison.RateBits(detail);
            CreateNamedEffect(command, creation, descriptor, prison.Fx, resources, interpreterFlags, findObject,
                (identity, projected) => WorldMagicPrisonEffectSerialization.Create(prison, identity, projected, detail));
            return;
        }
        if (BubblePrison is { } bubble && creation.TypeId == bubble.Resource)
        {
            CreateNamedEffect(command, creation, descriptor, bubble.Fx, resources, interpreterFlags, findObject,
                (identity, projected) => WorldBubblePrisonEffectSerialization.Create(bubble, identity, projected, _random));
            return;
        }
        if (creation.TypeId != _definitions.Resource || descriptor.ModelDesc.Category != SacredItemCategory.Effect)
            throw new InvalidDataException("CreateObj Effect family requires its original constructor/start/state producer.");
        if (_runtime.Owned || creation.SecondaryName is not null || creation.Name is not null || creation.WorldPosition is not null || creation.HeightOffset is not null ||
            creation.FacingDegrees is not null || creation.TakeScript is not null || creation.UseScript is not null ||
            creation.TriggerReference is not null || creation.TilePosition is not { } tile || tile.X < 0 || tile.Y < 0 || tile.Z != 0)
            throw new InvalidDataException("CreateObj Effect requires its compared plain ground-position startup caller.");
        var position = new GoldObjectPosition(SacredWorldCoordinates.TileCenterToWorld(tile.X),
            SacredWorldCoordinates.TileCenterToWorld(tile.Y), 0, _grid.GetSector(tile.X,tile.Y));
        var projected = _projection.Project(position.X,position.Y,0);
        if (!_references.TryAllocate(out var reference)) throw new InvalidDataException("CreateObj Effect needs a 0x153 reservation.");
        _counter.BeginAllocation();
        var node = new WorldObjectNode { PeriodMilliseconds = _definitions.NodePeriod };
        _activation.ActivateInitial(node,_runtime.Clock()/1000d);
        // Fresh KEYX-only direct placement does not insert a tile chain and
        // deactivates the node. The particle system remains independently active.
        if (!_activation.DeactivateInitial(node)) throw new InvalidOperationException("Effect node activation lifetime is missing.");
        var flags = _definitions.BaseFlags | 0x10000u;
        if (descriptor.ModelDesc.RequiresGraphicObjectRegistration) flags |= 0x800;
        var identity = new GoldObjectBaseState(reference,creation.TypeId,flags,(ushort)position.Sector,
            position.X,position.Y,0,0,0,_counter.Value,_runtime.Owner,0);
        var state = WorldFixedEffectSerialization.Create(_definitions,identity,projected,_random);
        var publication = GoldObjectStateCodec.Create(0x16f,_runtime.Owner,state.ToPayload());
        _created.Add(new(command.FileOffset,state,node,publication,_random.State));
        _log($"Startup Effect prepared: source 0x{command.FileOffset:X}, ref {reference}, type {creation.TypeId}, counter {_counter.Value}, node period {node.PeriodMilliseconds}, {state.ToPayload().Length} bytes including active particle state; retained privately.");
    }

    private void CreateNamedEffect(SacredScriptCommand command, SacredScriptCreateObject creation,
        ItemsPakEntry descriptor, SacredObjectFxInitialDefinition definitions, WorldScriptItemResources? resources,
        uint interpreterFlags, Func<uint, uint?> findObject,
        Func<GoldObjectBaseState, System.Numerics.Vector3, GoldObjectFxState> serialize)
    {
        if (descriptor.ModelDesc.Category != SacredItemCategory.Effect || _runtime.Owned || (interpreterFlags & 1) != 0 ||
            (creation.Name is not null || creation.SecondaryName is not null) && resources is null ||
            creation.WorldPosition is not null || creation.HeightOffset is not null || creation.FacingDegrees is not null ||
            creation.TakeScript is not null || creation.UseScript is not null || creation.TriggerReference is not null ||
            creation.TilePosition is not { X: >= 0, Y: >= 0, Z: >= 0 and <= byte.MaxValue } tile)
            throw new InvalidDataException("CreateObj named Effect requires its compared fresh, nondeferred tile-position caller.");
        if (tile.Z != 0 && (BubblePrison is null || creation.TypeId != BubblePrison.Resource))
            throw new InvalidDataException("Elevated named Effect requires its compared BUBBLEPRISON caller.");
        _ = resources?.ResolveSecondary(creation.SecondaryName);
        var position = new GoldObjectPosition(SacredWorldCoordinates.TileCenterToWorld(tile.X),
            SacredWorldCoordinates.TileCenterToWorld(tile.Y), (uint)tile.Z, _grid.GetSector(tile.X, tile.Y));
        // Gold's unloaded-sector caller retains the surface byte but starts the
        // particle at height zero. Loaded floor selection is a separate lifecycle.
        var projected = _projection.Project(position.X, position.Y, 0);
        var resource = resources?.Resolve(creation.Name) ?? GoldScriptResourceLayout.UnnamedResource;
        if (resource is not (0 or uint.MaxValue) && findObject(resource) is { } existing)
            throw new InvalidDataException($"CreateObj named Effect primary resource {resource} already resolves to object {existing}; native duplicate lifecycle is required.");
        if (!_references.TryAllocate(out var reference)) throw new InvalidDataException("CreateObj Effect needs a 0x153 reservation.");
        _counter.BeginAllocation();
        var node = new WorldObjectNode { PeriodMilliseconds = definitions.NodePeriod };
        _activation.ActivateInitial(node, _runtime.Clock() / 1000d);
        if (!_activation.DeactivateInitial(node)) throw new InvalidOperationException("Effect node activation lifetime is missing.");
        var flags = definitions.BaseFlags | 0x10000u;
        if (descriptor.ModelDesc.RequiresGraphicObjectRegistration) flags |= 0x800;
        var identity = new GoldObjectBaseState(reference, creation.TypeId, flags, (ushort)position.Sector,
            position.X, position.Y, checked((byte)position.Surface), 0, 0, _counter.Value, _runtime.Owner, 0);
        if (resources is not null) identity = resources.Bind(identity, resource);
        var state = serialize(identity, projected);
        var publication = GoldObjectStateCodec.Create(0x16f, _runtime.Owner, state.ToPayload());
        _created.Add(new(command.FileOffset, state, node, publication, _random.State));
        _log($"Startup Effect prepared: source 0x{command.FileOffset:X}, ref {reference}, type {creation.TypeId}, resource {state.BaseState.Word3C}, {state.ToPayload().Length} bytes; retained privately.");
    }
}
