using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Creature;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

namespace Sacred.GameplayHost.Networking;

/// <summary>The reached owned ambient factory on the existing host graph.
/// Publication captures constructor state before local flags/orientation change.</summary>
internal sealed class WorldHostAmbientCreatureFactory(WorldScriptCreatureWorld world, WorldScriptCreatureAssets assets,
    WorldScriptCreatureRuntime runtime, WorldScriptExecutionContext context)
{
    private readonly SacredSectorGrid _grid = assets.Sectors.CreateGrid();
    private GoldSectorEntryRuntime? _sectorRuntime;
    private readonly List<GoldMessage> _publications = [];
    private readonly List<uint> _created = [];
    public IReadOnlyList<GoldMessage> Publications => _publications.AsReadOnly();
    public IReadOnlyList<uint> Created => _created.AsReadOnly();
    public void BindSectorRuntime(GoldSectorEntryRuntime runtime)
    {
        if (_sectorRuntime is not null && !ReferenceEquals(_sectorRuntime, runtime))
            throw new InvalidOperationException("Ambient factory cannot change its actual sector runtime owner.");
        _sectorRuntime = runtime;
    }

    public WorldCreatureLoadedPlacementPlan Validate(uint type, int x, int y)
    {
        var item = assets.Objects.GetItem(type);
        if (assets.Results.StartupPrecision != Sacred.Core.Runtime.SacredX87Precision.Single)
            throw new NotSupportedException("Host ambient placement requires its compared initial x87 precision.");
        var template = assets.Creatures.ResolveTemplate(item);
        if (item.ModelDesc.Category != SacredItemCategory.Creature || template is null || template.Class == SacredCreatureClass.Hero)
            throw new NotSupportedException("Ambient generation requires its resolved original ordinary creature template.");
        _ = Sacred.GameplayHost.Runtime.SacredStartupHeading.ToDirection(item.ModelDesc.Angle3D);
        var plan = (world.Terrain ?? throw new InvalidOperationException("Ambient generation needs the shared terrain."))
            .PreparePlacement(context, new(SacredWorldCoordinates.TileCenterToWorldStartup(x), SacredWorldCoordinates.TileCenterToWorldStartup(y), 0,
                _grid.GetSector(x, y)));
        if (_sectorRuntime is null || !_sectorRuntime.Flags.ContainsKey(checked((ushort)plan.Position.Sector)))
            throw new NotSupportedException("Ambient placement needs its actual received sector runtime flags.");
        if (plan.Position.Surface != 0 || plan.StaticSurfaceReference != 0 || plan.TriggerReference != 0)
            throw new NotSupportedException("Ambient factory requires its remaining indoor/static-trigger placement profile.");
        return plan;
    }

    public void Create(uint type, int x, int y)
    {
        var plan = Validate(type, x, y);
        var item = assets.Objects.GetItem(type);
        var clock = runtime.Clock();
        var actor = world.Factory.Create(assets.Creatures.ResolveTemplate(item)!, item, clock, clock / 1000d, true, true);
        actor = WorldCreatureAmbientPlacement.Place(actor, plan);
        world.Terrain!.Membership.RegisterAmbient(actor, _sectorRuntime!.Flags[checked((ushort)plan.Position.Sector)]);
        world.Factory.Update(actor);
        var state = WorldCreatureInitialSerialization.Write(actor, item, world.EquipmentFactory.Items, assets.Objects.GetItem);
        _publications.Add(GoldObjectStateCodec.Create(0x16f, runtime.Owner, state.ToPayload()));
        var dy = (float)((world.Random.Next() % 101 - 50) * (double)0.02f);
        var dx = (float)((world.Random.Next() % 101 - 50) * (double)0.02f);
        actor = actor with { Base = actor.Base with { Flags = (actor.Base.Flags & ~0x10000u) | 0x90000000u }, Direction = new(dx, dy, 0) };
        world.Factory.Update(actor);
        _created.Add(actor.Base.ObjectReference);
    }
}
