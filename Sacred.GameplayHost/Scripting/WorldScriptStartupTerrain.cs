using System.Buffers.Binary;
using Sacred.Core.World;
using Sacred.Network.Gameplay;
using Sacred.World;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Compared initial sector demand-loading for script sampling. Startup
/// sectors with non-creature occupants or queued script tile patches still need
/// their native callbacks before their tiles may be used.</summary>
public sealed class WorldScriptStartupTerrain
{
    private readonly WorldScriptCreatureWorld _world;
    private readonly WorldScriptCreatureAssets _assets;
    private readonly SacredSectorGrid _grid;
    private readonly Action<string> _log;
    private WorldScriptExecutionContext? _context;
    private readonly Dictionary<uint,WorldScriptCreatedObject> _groundObjects = [];
    private readonly Dictionary<uint,WldxSectorPayload> _payloads;
    public WorldScriptIndoorSurfaces? Surfaces { get; }
    public WorldCreatureSectorMembership Membership { get; }
    public WorldScriptPositionTiles Tiles { get; }

    public WorldScriptStartupTerrain(WorldScriptCreatureWorld world, WorldScriptCreatureAssets assets,
        Func<uint, KeyxSectorRecord, byte[]> read, Action<string> log)
    {
        (_world, _assets, _log) = (world, assets, log);
        _payloads = [];
        Membership = new();
        _grid=assets.Sectors.CreateGrid();
        Tiles = new(assets.Sectors,(id,entry)=>
        {
            if (assets.ReadSectorTiles is not { } readSector) return read(id,entry);
            var payload=readSector(id,entry);_payloads.Add(id,payload);return payload.OutdoorTiles;
        }, Initialize);
        Tiles.Evicted += id => { _payloads.Remove(id); _log($"Startup sector tiles evicted: sector {id}; actors retained for re-placement."); };
        if (assets.ReadSectorTiles is not null && assets.ReadStaticObject is { } readStatic)
            Surfaces = new(Tiles.GetTile,GetPayload,readStatic);
    }

    internal WorldScriptStartupTerrain(WorldScriptCreatureWorld world, WorldScriptCreatureAssets assets,
        WorldScriptPositionTiles tiles, WorldScriptIndoorSurfaces surfaces, WorldCreatureSectorMembership membership,
        Dictionary<uint,WldxSectorPayload> payloads, Action<string> log)
    {
        (_world, _assets, _log) = (world, assets, log);
        _grid = assets.Sectors.CreateGrid();
        (Tiles, Surfaces, Membership, _payloads) = (tiles, surfaces, membership, payloads);
    }

    private WldxSectorPayload GetPayload(uint sector)
    {
        if (!_assets.Sectors.TryGetSector(sector,out var entry)) throw new InvalidDataException("Static floor has no original sector.");
        _=Tiles.GetTile(SacredSectorCoordinates.ToTileOrigin(entry.RawX),SacredSectorCoordinates.ToTileOrigin(entry.RawY));
        return _payloads[sector];
    }

    public WorldCreatureLoadedPlacementPlan PreparePlacement(WorldScriptExecutionContext context,GoldObjectPosition position)
    {
        _context=context;
        if (Surfaces is not { } surfaces)
            throw new NotSupportedException("CreateNPC positioned factory requires original static records and complete indoor sector grids before allocation.");
        var resolved=surfaces.ResolveFactoryPosition(position);
        var x=SacredWorldCoordinates.WorldToTile(resolved.X);var y=SacredWorldCoordinates.WorldToTile(resolved.Y);
        var parent=resolved.Surface==0 ? 0 : surfaces.FindParent(x,y);
        var floor=resolved.Surface==0 ? 0 : surfaces.Child(parent,checked((ushort)resolved.Surface));
        if (resolved.Surface!=0 && (surfaces.GetStatic(floor) is not { } child || child.ContainerId!=parent || child.HeightLevelId==0))
            throw new NotSupportedException("CreateNPC positioned factory requires its resolved indoor floor chain.");
        var tile=surfaces.GetTile(x,y,floor);
        if (tile is not { } destination)
            throw new NotSupportedException("CreateNPC positioned factory requires an original destination tile.");
        if (!WorldCreatureLoadedPlacement.SupportsTile(destination,resolved,floor))
            throw new NotSupportedException($"CreateNPC positioned factory requires its remaining special-tile placement branch: behavior {destination.Properties.Behavior}, flags {destination.PathFlags}, tile {x},{y}, surface {resolved.Surface}, floor {floor}, sector {resolved.Sector}.");
        var trigger=surfaces.GetStatic(parent)?.TriggerId ?? 0;
        if (trigger==0 || context.WorldTriggers?.Records.ContainsKey(trigger)!=true) trigger=0;
        Action<uint> commit;
        if (floor==0) commit=reference=>Tiles.SetHead(x,y,reference);
        else
        {
            var cell=surfaces.IndoorCell(x,y,floor)!.Value;
            commit=reference=>BinaryPrimitives.WriteUInt32LittleEndian(cell.Bytes.AsSpan(cell.Offset+8),reference);
        }
        return new(resolved,floor,trigger,destination,commit);
    }

    public WorldCreatureInitialActor Place(WorldCreatureInitialActor actor,WorldCreatureLoadedPlacementPlan plan)
    {
        var placed=WorldCreatureLoadedPlacement.Place(actor,plan,_world.Factory.Activation);
        Membership.Move(actor.Base.ObjectReference,actor.Base.Sector,placed.Base.Sector);
        _log($"Startup positioned NPC: ref {placed.Base.ObjectReference}, sector {placed.Base.Sector}, surface {placed.Base.Surface}, static floor {placed.StaticSurfaceReference}, trigger {placed.Base.TriggerReference}.");
        return placed;
    }

    internal void RegisterCreatedContainer(WorldScriptCreatedObject value) =>
        _groundObjects.Add(value.BaseState.ObjectReference, value);

    internal WorldCreatureLoadedPlacementPlan PrepareContainerPlacement(WorldScriptExecutionContext context, GoldObjectPosition position)
    {
        _context = context;
        var x = SacredWorldCoordinates.WorldToTile(position.X);
        var y = SacredWorldCoordinates.WorldToTile(position.Y);
        var surfaces = Surfaces ?? throw new NotSupportedException("Loaded Container requires original static/floor records.");
        var parent = position.Surface == 0 ? 0 : surfaces.FindParent(x, y);
        var floor = position.Surface == 0 ? 0 : surfaces.Child(parent, checked((ushort)position.Surface));
        if (position.Surface != 0 && (surfaces.GetStatic(floor) is not { } child || child.ContainerId != parent || child.HeightLevelId == 0))
            throw new NotSupportedException("Loaded Container requires its resolved original indoor floor.");
        var tile = surfaces.GetTile(x, y, floor) ?? throw new InvalidDataException("Loaded Container destination has no original cell.");
        var trigger = surfaces.GetStatic(parent)?.TriggerId ?? 0;
        if (trigger == 0 || context.WorldTriggers?.Records.ContainsKey(trigger) != true) trigger = 0;
        Action<uint> commit;
        if (floor == 0) commit = reference => Tiles.SetHead(x, y, reference);
        else
        {
            var cell = surfaces.IndoorCell(x, y, floor)!.Value;
            commit = reference => BinaryPrimitives.WriteUInt32LittleEndian(cell.Bytes.AsSpan(cell.Offset + 8), reference);
        }
        var plan = new WorldCreatureLoadedPlacementPlan(position, floor, trigger, tile, commit);
        WorldScriptContainerPlacement.Require(plan);
        return plan;
    }

    private void MarkTile(int x,int y,ushort surface,byte flag)
    {
        (byte[] Bytes,int Offset)? cell;
        if (surface == 0) cell=Tiles.GetCell(x,y);
        else
        {
            var surfaces=Surfaces ?? throw new NotSupportedException("Sector patches require original indoor grids.");
            var floor=surfaces.Child(surfaces.FindParent(x,y),surface);
            if (floor==0) return;
            cell=surfaces.GetStatic(floor)?.HeightLevelId==0 ? Tiles.GetCell(x,y) : surfaces.IndoorCell(x,y,floor);
        }
        if (cell is { } target) target.Bytes[target.Offset+0x1E] |= flag;
    }

    internal void MarkInitializingTrigger(WorldScriptExecutionContext context, Sacred.Core.GameBin.Scripts.SacredScriptPosition cell)
    {
        if (!context.IsInitializingSector || context.CurrentSector is not { } sector || cell.Z != 0 ||
            _grid.GetSector(cell.X,cell.Y) != sector.Sector || !Tiles.IsLoaded(sector.Sector))
            throw new NotSupportedException("Initial-sector trigger marking needs its real loaded outdoor cell and callback scope.");
        _context = context;
        _ = Tiles.GetCell(cell.X,cell.Y) ?? throw new InvalidDataException("Initial-sector trigger tile is missing.");
        MarkTile(cell.X,cell.Y,0,4);
    }

    /// <summary>493440 after completed EnterSector restoration, on the SAME
    /// loaded outdoor/indoor cells and pending mouseover/trigger sources used
    /// during startup reload. Registration retains the existing active registry.</summary>
    internal void ApplyEnteredPatches(WorldScriptExecutionContext context, WorldScriptSectorEnvironment sector)
    {
        if (!Tiles.IsLoaded(sector.Sector)) throw new InvalidOperationException("Final sector patches require the actual loaded terrain.");
        var mouseovers = context.Mouseovers ?? throw new NotSupportedException("Final sector patches require the actual pending mouseover source and active registry.");
        var triggers = context.WorldTriggers ?? throw new NotSupportedException("Final sector patches require the actual pending trigger positions.");
        _context = context;
        WorldScriptSectorTilePatches.Apply(sector.Coordinate.X, sector.Coordinate.Y, mouseovers.Pending,
            triggers.PendingPositions.Select(value => (value.Position.X, value.Position.Y, unchecked((byte)value.Position.Z))),
            MarkTile, mouseovers.Active.Register);
    }

    /// <summary>This initial no-player profile has no entered/initialized script-sector flags.
    /// The separately compared tile load contributes only runtime 0x10000.</summary>
    public uint GetInitialSectorFlags(int x,int y) => Tiles.IsLoaded(_grid.GetSector(x,y)) ? 0x10000u : 0;

    public void MarkMouseover(WorldScriptExecutionContext context, WorldScriptMouseoverCreation creation)
    {
        _context = context;
        WorldScriptMouseoverTileMarks.Mark(creation,Tiles.GetCell,(x,y,level) =>
        {
            var surfaces = Surfaces ?? throw new NotSupportedException("Mouseover indoor marks require original floor grids.");
            var floor = surfaces.Child(surfaces.FindParent(x,y),level);
            if (floor == 0) return null;
            return surfaces.GetStatic(floor)?.HeightLevelId == 0 ? Tiles.GetCell(x,y) : surfaces.IndoorCell(x,y,floor);
        });
    }

    public bool IsAllowed(WorldScriptExecutionContext context, int x, int y)
    {
        if (!context.IsExecutingStartup || context.WorldTriggers is null)
            throw new NotSupportedException("Script-position demand-loading requires initial startup and known trigger patch state.");
        _context = context;
        return WorldScriptPositionValidity.IsAllowed(Tiles.GetTile(x,y), reference =>
        {
            if (_world.Factory.Actors.TryGetValue(reference,out var actor))
                return new WorldScriptPositionOccupant(actor.NextOnTile,_assets.Objects.GetItem(actor.Base.ObjectType).ModelDesc.IsCreature);
            if (_world.EquipmentFactory.Items.TryGetValue(reference,out var item))
                return new WorldScriptPositionOccupant(item.NextOnTile,_assets.Objects.GetItem(item.Physical.Base.ObjectType).ModelDesc.IsCreature);
            if (_groundObjects.TryGetValue(reference,out var ground))
                return new WorldScriptPositionOccupant(ground.NextOnTile,false);
            return null;
        });
    }

    internal void Initialize(uint sector, byte[] tiles)
    {
        var context = _context ?? throw new InvalidOperationException("No startup tile query is active.");

        var items = _world.EquipmentFactory.Items.Values.Where(item=>item.Physical.Base.Sector==sector &&
            item.Physical.Base.Flags==0x201 && item.Physical.Base.Surface==0 && !item.Node.Active &&
            _assets.Objects.GetItem(item.Physical.Base.ObjectType).ModelDesc.Category==Sacred.Core.Pak.Items.SacredItemCategory.QuestItem)
            .ToDictionary(item=>item.Physical.Base.ObjectReference);
        var containers = ((context.ObjectWorld as WorldScriptObjectWorld)?.Objects ?? [])
            .Where(value=>value.BaseState.Sector==sector && (value.BaseState.Flags & ~0x10000u)==0x201)
            .ToDictionary(value=>value.BaseState.ObjectReference);
        var others = (context.ObjectWorld as WorldScriptObjectWorld)?.Objects.Select(value => value.BaseState)
            ?? Enumerable.Empty<Sacred.Network.Gameplay.GoldObjectBaseState>();
        others = others.Concat(_world.EquipmentFactory.Items.Values.Select(value => value.Physical.Base));
        if (_world.Effects is { } effects) others = others.Concat(effects.Created.Select(value => value.BaseState));
        var occupants = others.Where(value => value.Sector == sector && (value.Flags & 2) == 0 && !items.ContainsKey(value.ObjectReference) && !containers.ContainsKey(value.ObjectReference)).ToArray();
        if (occupants.Length != 0)
            throw new NotSupportedException($"Script-position sector loading requires non-creature object re-placement in sector {sector}: " +
                string.Join(", ",occupants.Select(value=>$"ref {value.ObjectReference}/type {value.ObjectType}/surface {value.Surface}/flags 0x{value.Flags:X}")));

        for (var i = 0; i < 4096; i++)
            if (BinaryPrimitives.ReadUInt32LittleEndian(tiles.AsSpan(i*32+8)) != 0)
                throw new NotSupportedException("Initial script-position tiles require original authored dynamic-chain restoration.");
        if (!_assets.Sectors.TryGetSector(sector,out var entry))
            throw new InvalidDataException("Loaded script-position sector is missing its KEYX record.");
        var actors = _world.Factory.Actors.Values.Where(value => value.Base.Sector == sector && (value.Base.Flags & 2) == 0)
            .OrderBy(value => value.Base.ObjectReference).ToArray();
        var actorIndex=actors.ToDictionary(actor=>actor.Base.ObjectReference);
        foreach (var reference in actorIndex.Keys.Concat(items.Keys).Concat(containers.Keys).Order())
        {
            if (items.TryGetValue(reference,out var item))
            {
                var itemX=SacredWorldCoordinates.WorldToTile(item.Physical.Base.X)-SacredSectorCoordinates.ToTileOrigin(entry.RawX);
                var itemY=SacredWorldCoordinates.WorldToTile(item.Physical.Base.Y)-SacredSectorCoordinates.ToTileOrigin(entry.RawY);
                if ((uint)itemX>=64 || (uint)itemY>=64) throw new InvalidDataException("Reloaded QuestItem is outside its sector.");
                var offset=(itemY*64+itemX)*32;
                var itemTile=WldxTileRecord.FromBytes(tiles.AsSpan(offset));
                var head=WorldScriptQuestItemSectorReload.Reinsert(item,itemTile,itemTile.WorldObjectChainHeadId);
                BinaryPrimitives.WriteUInt32LittleEndian(tiles.AsSpan(offset+8),head);
                continue;
            }
            if (containers.TryGetValue(reference,out var container))
            {
                var worldX=SacredWorldCoordinates.WorldToTile(container.BaseState.X);
                var worldY=SacredWorldCoordinates.WorldToTile(container.BaseState.Y);
                var containerX=worldX-SacredSectorCoordinates.ToTileOrigin(entry.RawX);
                var containerY=worldY-SacredSectorCoordinates.ToTileOrigin(entry.RawY);
                if ((uint)containerX>=64 || (uint)containerY>=64) throw new InvalidDataException("Reloaded Container is outside its sector.");
                if (container.BaseState.Surface==0)
                {
                    var offset=(containerY*64+containerX)*32;
                    var containerTile=WldxTileRecord.FromBytes(tiles.AsSpan(offset));
                    container.NextOnTile=WorldScriptGroundObjectSectorReload.Next(container.BaseState,containerTile,containerTile.WorldObjectChainHeadId);
                    BinaryPrimitives.WriteUInt32LittleEndian(tiles.AsSpan(offset+8),reference);
                }
                else
                {
                    var surfaces=Surfaces ?? throw new NotSupportedException("Elevated Container reload requires original static/indoor grids.");
                    var floor=surfaces.Child(surfaces.FindParent(worldX,worldY),container.BaseState.Surface);
                    var cell=surfaces.IndoorCell(worldX,worldY,floor) ?? throw new NotSupportedException("Elevated Container reload requires an original indoor cell.");
                    var containerTile=WldxTileRecord.FromBytes(cell.Bytes.AsSpan(cell.Offset));
                    var links=WorldScriptElevatedObjectSectorReload.Reinsert(container.BaseState,container.StaticSurfaceReference,containerTile,containerTile.WorldObjectChainHeadId,floor);
                    container.StaticSurfaceReference=links.Floor;
                    container.NextOnTile=links.NextOnTile;
                    BinaryPrimitives.WriteUInt32LittleEndian(cell.Bytes.AsSpan(cell.Offset+8),links.TileHead);
                }
                _groundObjects[reference]=container;
                continue;
            }
            var actor=actorIndex[reference];
            var x = SacredWorldCoordinates.WorldToTile(actor.Base.X)-SacredSectorCoordinates.ToTileOrigin(entry.RawX);
            var y = SacredWorldCoordinates.WorldToTile(actor.Base.Y)-SacredSectorCoordinates.ToTileOrigin(entry.RawY);
            if ((uint)x >= 64 || (uint)y >= 64) throw new InvalidDataException("Reloaded NPC is outside its sector.");
            WorldCreatureGroundPlacementResult result;
            if (actor.Base.Surface == 0)
            {
                var at = (y*64+x)*32; var tile = WldxTileRecord.FromBytes(tiles.AsSpan(at));
                if (actor.Base.ObjectReference == _world.LocalPlayerSlot && actor.CampaignEntryPlaced)
                {
                    var detached = actor with { Base = actor.Base with { Flags = actor.Base.Flags | 2u, Sector = 0 }, NextOnTile = 0 };
                    var placed = WorldCreatureHostGroundPlacement.Place(detached,
                        new(actor.Base.X, actor.Base.Y, 0, actor.Base.Sector), tile, tile.WorldObjectChainHeadId);
                    result = new(placed, placed.Base.ObjectReference, true);
                }
                else result = WorldCreatureSectorReload.Reinsert(actor,tile,tile.WorldObjectChainHeadId);
                BinaryPrimitives.WriteUInt32LittleEndian(tiles.AsSpan(at+8),result.TileHead);
            }
            else
            {
                var surfaces = Surfaces ?? throw new NotSupportedException("Elevated reload requires original static/indoor grids.");
                var worldX = SacredWorldCoordinates.WorldToTile(actor.Base.X); var worldY = SacredWorldCoordinates.WorldToTile(actor.Base.Y);
                var floor = surfaces.Child(surfaces.FindParent(worldX,worldY),actor.Base.Surface);
                var cell = surfaces.IndoorCell(worldX,worldY,floor) ?? throw new NotSupportedException("Elevated reload requires an original indoor tile.");
                var tile = WldxTileRecord.FromBytes(cell.Bytes.AsSpan(cell.Offset));
                result = WorldCreatureSectorReload.ReinsertIndoor(actor,tile,tile.WorldObjectChainHeadId,floor);
                BinaryPrimitives.WriteUInt32LittleEndian(cell.Bytes.AsSpan(cell.Offset+8),result.TileHead);
            }
            Membership.Move(actor.Base.ObjectReference,actor.Base.Sector,0);
            Membership.RegisterUnenteredReload(result.Actor);
            _world.UpdateScriptActor(result.Actor);
        }
        WorldScriptSectorTilePatches.Apply(SacredSectorCoordinates.ToTileOrigin(entry.RawX)/64,
            SacredSectorCoordinates.ToTileOrigin(entry.RawY)/64,context.Mouseovers?.Pending ?? [],
            context.WorldTriggers!.PendingPositions.Select(value=>(value.Position.X,value.Position.Y,unchecked((byte)value.Position.Z))),
            (x,y,surface,flag)=>MarkTile(x,y,surface,flag), patch=>context.Mouseovers?.Active.Register(patch));
        _log($"Startup sector tiles loaded: sector {sector}, {actors.Length} dormant NPCs and {items.Count} QuestItems / {containers.Count} Containers reinserted; script-position validation ready.");
    }
}

