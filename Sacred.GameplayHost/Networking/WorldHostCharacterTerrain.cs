using System.Buffers.Binary;
using Sacred.Assets.World.Static;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;
using Sacred.World;

namespace Sacred.GameplayHost.Networking;

/// <summary>Session-owned original terrain for saved-hero reinsertion and entry.
/// Reuses the tile/surface/membership services and the owner's existing factories;
/// it owns no object registry, grant pool or world snapshot. Pre-StartCode only:
/// script tile patches have not been declared yet.</summary>
public sealed class WorldHostCharacterTerrain
{
    private readonly WorldHostHeroConstruction _hero;
    private readonly SacredSectorIndex _sectors;
    private readonly SacredSectorGrid _grid;
    private readonly Dictionary<uint, WldxSectorPayload> _payloads = [];
    private readonly WorldScriptPositionTiles _tiles;
    private readonly WorldScriptIndoorSurfaces _surfaces;
    private readonly WorldCreatureSectorMembership _membership = new();
    private WorldScriptStartupTerrain? _startup;
    public bool IsInvalidated { get; private set; }
    public int LoadedSectorCount { get { RequireCurrent(); return _tiles.Retained.Count; } }

    internal WorldHostCharacterTerrain(string installation, WorldHostHeroConstruction hero)
    {
        _hero = hero;
        _sectors = SacredSectorIndex.Read(Path.Combine(installation, "World/sectors.keyx"));
        _grid = _sectors.CreateGrid();
        _tiles = new(_sectors, (id, entry) =>
        {
            using var loader = new WldxLoader(new(Path.Combine(installation, "World/sectors.wldx"),
                FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            var payload = loader.LoadSector(id, entry);
            _payloads.Add(id, payload);
            return payload.OutdoorTiles;
        }, Initialize);
        _tiles.Evicted += id => _payloads.Remove(id);
        _surfaces = new(_tiles.GetTile, id => _payloads[id], id =>
        {
            using var archive = StaticPakArchive.Load(Path.Combine(installation, "World/Static.pak"));
            return archive.Get(id);
        });
    }

    internal void Invalidate() { IsInvalidated = true; _tiles.Invalidate(); _payloads.Clear(); }

    internal WorldScriptStartupTerrain BeginStartup(WorldScriptCreatureWorld world, WorldScriptCreatureAssets assets, Action<string> log)
    {
        RequireCurrent();
        if (_startup is not null) throw new InvalidOperationException("Host terrain already belongs to a script startup lifetime.");
        _startup = new(world, assets, _tiles, _surfaces, _membership, _payloads, log);
        return _startup;
    }

    public WldxTileRecord? GetLoadedTile(int x, int y)
    {
        RequireCurrent();
        return _tiles.IsLoaded(_grid.GetSector(x, y)) ? _tiles.GetTile(x, y) : null;
    }

    public IReadOnlyList<uint> Members(ushort sector) { RequireCurrent(); return _membership.Get(sector); }
    internal bool IsSectorLoaded(ushort sector) { RequireCurrent(); return _tiles.IsLoaded(sector); }

    internal void ResolveSavedSurface()
    {
        RequireCurrent();
        var actor = _hero.Actor;
        if (actor.Base.Surface != 0 || actor.StaticSurfaceReference != 0)
            throw new NotSupportedException("Saved host surface lookup requires its compared outdoor/unlinked position.");
        var x = SacredWorldCoordinates.WorldToTile(actor.Base.X);
        var y = SacredWorldCoordinates.WorldToTile(actor.Base.Y);
        if (_surfaces.FindParent(x, y) != 0)
            throw new NotSupportedException("Saved host has a real indoor parent; its visibility/static-surface effects remain required.");
        if (_tiles.GetTile(x, y) is null)
            throw new InvalidDataException("Saved hero has no original world tile.");
    }

    internal void PlaceEntry(GoldObjectPosition authored)
    {
        RequireCurrent();
        if (authored.Surface != 0) throw new NotSupportedException("Host entry requires its original elevated placement path.");
        var x = SacredWorldCoordinates.WorldToTile(authored.X);
        var y = SacredWorldCoordinates.WorldToTile(authored.Y);
        var sector = _grid.GetSector(x, y);
        if (sector == 0 || authored.Sector != 0 && authored.Sector != sector)
            throw new InvalidDataException("Host entry does not resolve inside its original KEYX sector.");
        var old = _hero.Actor;
        var oldX = SacredWorldCoordinates.WorldToTile(old.Base.X);
        var oldY = SacredWorldCoordinates.WorldToTile(old.Base.Y);
        var oldTile = _tiles.GetTile(oldX, oldY) ?? throw new InvalidDataException("Placed hero's original tile is missing.");
        if ((old.Base.Flags & 2) != 0 || old.StaticSurfaceReference != 0 ||
            oldTile.WorldObjectChainHeadId != old.Base.ObjectReference || old.NextOnTile != 0)
            throw new NotSupportedException("Host retirement requires its actual sole current outdoor tile-chain member.");
        // 5FC8B0: retain the old sector until 5FCB70 moves membership.
        _tiles.SetHead(oldX, oldY, 0);
        _hero.ApplyWorldActor(old with { Base = old.Base with { Flags = old.Base.Flags | 2u }, NextOnTile = 0 });
        if (_surfaces.FindParent(x, y) != 0)
            throw new NotSupportedException("Host entry has an original indoor parent; its elevated placement remains required.");
        var tile = _tiles.GetTile(x, y) ?? throw new InvalidDataException("Original entry tile is absent.");
        var placed = WorldCreatureHostGroundPlacement.Place(_hero.Actor, authored with { Sector = sector }, tile, tile.WorldObjectChainHeadId);
        _tiles.SetHead(x, y, placed.Base.ObjectReference);
        _membership.Move(placed.Base.ObjectReference, old.Base.Sector, placed.Base.Sector);
        _hero.ApplyWorldActor(placed with { CampaignEntryPlaced = true });
    }

    private void Initialize(uint sector, byte[] tiles)
    {
        RequireCurrent();
        if (_startup is not null) { _startup.Initialize(sector, tiles); return; }
        // The real fresh load callback walks the SAME manager graph. Saved items
        // on this supported path are unplaced; additional actors need their own
        // existing sector-reload caller before this adapter can accept them.
        if (_hero.Factory.Actors.Count != 1 || _hero.EquipmentFactory.Items.Values.Any(item => (item.Physical.Base.Flags & 2) == 0))
            throw new NotSupportedException("Pre-script host terrain requires its actual single hero and unplaced saved items.");
        for (var index = 0; index < 4096; index++)
            if (BinaryPrimitives.ReadUInt32LittleEndian(tiles.AsSpan(index * 32 + 8)) != 0)
                throw new NotSupportedException("Fresh host terrain contains authored dynamic-chain references requiring restoration.");
        var actor = _hero.Actor;
        if (actor.Base.Sector != sector || (actor.Base.Flags & 2) != 0) return;
        if (actor.Base.Surface != 0 || actor.StaticSurfaceReference != 0 || !_sectors.TryGetSector(sector, out var entry))
            throw new NotSupportedException("Host sector callback requires the actual ordinary outdoor saved hero.");
        var x = SacredWorldCoordinates.WorldToTile(actor.Base.X);
        var y = SacredWorldCoordinates.WorldToTile(actor.Base.Y);
        var indexX = x - SacredSectorCoordinates.ToTileOrigin(entry.RawX);
        var indexY = y - SacredSectorCoordinates.ToTileOrigin(entry.RawY);
        if ((uint)indexX >= 64 || (uint)indexY >= 64) throw new InvalidDataException("Saved hero lies outside its sector payload.");
        var offset = (indexY * 64 + indexX) * 32;
        var tile = WldxTileRecord.FromBytes(tiles.AsSpan(offset));
        var detached = actor with { Base = actor.Base with { Flags = actor.Base.Flags | 2u, Sector = 0 }, NextOnTile = 0 };
        var placed = WorldCreatureHostGroundPlacement.Place(detached, new(actor.Base.X, actor.Base.Y, 0, sector), tile, tile.WorldObjectChainHeadId);
        _membership.Move(actor.Base.ObjectReference, actor.Base.Sector, 0);
        _membership.Move(actor.Base.ObjectReference, 0, placed.Base.Sector);
        BinaryPrimitives.WriteUInt32LittleEndian(tiles.AsSpan(offset + 8), placed.Base.ObjectReference);
        _hero.ApplyWorldActor(placed);
    }

    private void RequireCurrent()
    {
        if (IsInvalidated || _hero.IsInvalidated) throw new InvalidOperationException("Host terrain lifetime ended.");
    }
}
