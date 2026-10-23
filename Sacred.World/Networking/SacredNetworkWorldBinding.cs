using System.Collections.Frozen;
using System.Numerics;
using Sacred.Core.World.Sector;
using Sacred.Network.Gameplay;

namespace Sacred.World.Networking;

/// <summary>Loads native confirmed sectors and exposes replicas in authored world coordinates, independently of a renderer.</summary>
public sealed class SacredNetworkWorldBinding : IDisposable
{
    private readonly IReadOnlyDictionary<ushort, Sector> _initialSectors;
    private readonly VisibleWorld _initialWorld;
    private SacredNetworkTerrainStream? _terrain;
    private SacredNetworkPatchTerrain? _patchTerrain;
    private SacredWorldArchive? _archive;
    private readonly SacredNetworkSectorCopies _copies = new();
    public SacredNetworkSectorPatches SectorPatches { get; private set; } = null!;
    public SacredNetworkDoorStates DoorStates { get; private set; } = null!;
    private SacredNetworkWorldBinding(GoldWorldReplica replicas, Dictionary<ushort, Sector> sectors, byte playerSlot, long sessionVersion, ushort heroSector)
    {
        (Replicas, _initialSectors, PlayerSlot, SessionVersion) = (replicas, sectors.ToFrozenDictionary(), playerSlot, sessionVersion);
        _initialWorld = new(sectors[heroSector].Coord, sectors.Values.ToArray(), 0);
    }
    public GoldWorldReplica Replicas { get; }
    public IReadOnlyDictionary<ushort, Sector> Sectors => _terrain?.Sectors ?? _initialSectors;
    public byte PlayerSlot { get; }
    public long SessionVersion { get; }
    public VisibleWorld VisibleWorld => _terrain?.World ?? _initialWorld;
    public bool TerrainSettled => _terrain?.IsSettled == true;
    /// <summary>Gold's ambient scan queries a two-tile border and lazily loads
    /// neighboring terrain through 0x634340. Current session copies take precedence.</summary>
    public async Task<IReadOnlyDictionary<SectorCoord,Sector>> LoadPopulationTerrainAsync(ushort sector)
    {
        var archive=_archive ?? throw new InvalidOperationException("Population requires bound original terrain.");
        if(!IsCurrentSession || !archive.TryGetSectorCoordinate(sector,out var center))
            throw new InvalidOperationException("Population sector belongs to a retired or missing world.");
        var sessionCopies=Sectors.Values.ToDictionary(row=>row.Coord);
        var tasks=new List<Task<Sector?>>();
        for(var dy=-1;dy<=1;dy++)
            for(var dx=-1;dx<=1;dx++)
            {
                var coordinate=new SectorCoord(center.X+dx,center.Y+dy);
                tasks.Add(sessionCopies.TryGetValue(coordinate,out var loaded)?Task.FromResult<Sector?>(loaded):archive.TryLoadSector(coordinate));
            }
        var loadedSectors=await Task.WhenAll(tasks).ConfigureAwait(false);
        return loadedSectors.OfType<Sector>().ToDictionary(row=>row.Coord);
    }
    public string? TerrainError => _terrain?.Error;
    // Native reply work is retained until a verified script/actor initializer handles it.
    public int PendingNativeSectorWork => IsCurrentSession
        ? Replicas.SectorWork.PendingCount + Replicas.SectorWork.FailedCount : 0;
    public void Update()
    {
        DoorStates.Update();
        _terrain?.Update();
        if (_terrain?.IsSettled == true)
            foreach (var sector in Sectors.Values) _copies.Synchronize(sector);
        if (IsCurrentSession && _archive is { } archive && SectorPatches.ApplyReceived(Sectors,
                id => archive.TryGetSectorCoordinate(id, out var coord) ? coord : null))
            _terrain?.PublishTileChanges();
    }
    /// <returns>False while confirmed terrain or the native pending vectors are still loading.</returns>
    public bool TryApplySectorPatches(GoldSectorWork work)
    {
        if (!IsCurrentSession || work.Session != Replicas.SectorWork.Session)
            throw new InvalidOperationException("Sector patch work belongs to a retired session.");
        if (!Replicas.SectorWork.Work.TryGetValue(work.Id, out var current) || !ReferenceEquals(current, work) || work.State != GoldSectorWorkState.Executing)
            throw new InvalidOperationException("Sector patch work requires the current executing queue operation.");
        if (!work.ApplyPatches) throw new InvalidOperationException("Sector patch pass requires a zero-mode reply or completed EnterSector production.");
        if (TerrainError is { } error) throw new InvalidDataException(error);
        if (!TerrainSettled || !Sectors.TryGetValue(work.Entry.Sector, out var sector)) return false;
        var patches = work.PatchBank ?? Replicas.WorldBanks.Current.GetValueOrDefault(GoldWorldBankLayout.Bank3Opcode);
        var positions = work.PositionBank ?? Replicas.WorldBanks.Current.GetValueOrDefault(GoldWorldBankLayout.Bank1Opcode);
        if (patches is null || positions is null) return false;
        if (!SectorPatches.Apply(sector, Sectors, patches, positions)) return false;
        _terrain!.PublishTileChanges();
        SectorPatches.NotifyEntered(work.Entry.Sector);
        return true;
    }
    public void Dispose() { _terrain?.Dispose(); _patchTerrain?.Dispose(); }
    public bool IsCurrentSession => Replicas.LocalPlayerSlot == PlayerSlot && Replicas.SessionVersion == SessionVersion;
    public GoldReplicatedObject LocalHero => IsCurrentSession && Replicas.Objects.TryGetValue(PlayerSlot, out var hero)
        ? hero : throw new InvalidOperationException("Bound participant session has ended.");
    public Vector2 LocalPosition => SacredNetworkPosition.ToTiles(LocalHero.BaseState);
    public bool CanNavigateSector(ushort sector) => IsCurrentSession && Replicas.Sectors.ContainsKey(sector) &&
        Sectors.ContainsKey(sector) && Replicas.SectorWork.IsSectorComplete(sector);
    public bool CanNavigateSector(SectorCoord coordinate) => Sectors.Any(pair => pair.Value.Coord == coordinate && CanNavigateSector(pair.Key));
    public bool TryGetMovementPosition(Vector2 tiles, out GoldObjectPosition position) =>
        TryGetPosition(tiles, out position) && CanNavigateSector(unchecked((ushort)position.Sector));
    /// <summary>Resolve the rounded wire anchor against loaded, server-confirmed terrain.</summary>
    public bool TryGetPosition(Vector2 tiles, out GoldObjectPosition position)
    {
        position = default;
        if (!IsCurrentSession || !float.IsFinite(tiles.X) || !float.IsFinite(tiles.Y)) return false;
        var x = checked((int)MathF.Round(tiles.X * SacredNetworkPosition.NativeUnitsPerTile));
        var y = checked((int)MathF.Round(tiles.Y * SacredNetworkPosition.NativeUnitsPerTile));
        var sectorX = (int)MathF.Floor((float)x / (SacredNetworkPosition.NativeUnitsPerTile * Sector.TileCount));
        var sectorY = (int)MathF.Floor((float)y / (SacredNetworkPosition.NativeUnitsPerTile * Sector.TileCount));
        foreach (var entry in Sectors)
            if (Replicas.Sectors.ContainsKey(entry.Key) && entry.Value.Coord.X == sectorX && entry.Value.Coord.Y == sectorY)
            {
                position = new(x, y, LocalHero.BaseState.Surface, entry.Key);
                return true;
            }
        return false;
    }
    public float OutdoorHeight(GoldObjectBaseState state)
    {
        if (!Sectors.TryGetValue(state.Sector, out var sector)) return 0;
        var position = SacredNetworkPosition.ToTiles(state);
        var tileX = (int)MathF.Floor(position.X); var tileY = (int)MathF.Floor(position.Y);
        var x = tileX - sector.Coord.X * Sector.TileCount; var y = tileY - sector.Coord.Y * Sector.TileCount;
        return (uint)x < Sector.TileCount && (uint)y < Sector.TileCount
            ? WorldElevationSampler.SampleTile(sector.Elevation[x,y], position.X-tileX, position.Y-tileY) * WorldElevationSampler.WorldHeightPerSample : 0;
    }

    /// <summary>Sample the presentation position while a received path advances between anchors.</summary>
    public float OutdoorHeightAt(Vector2 position)
    {
        var tileX = (int)MathF.Floor(position.X);
        var tileY = (int)MathF.Floor(position.Y);
        foreach (var sector in Sectors.Values)
        {
            var x = tileX - sector.Coord.X * Sector.TileCount;
            var y = tileY - sector.Coord.Y * Sector.TileCount;
            if ((uint)x < Sector.TileCount && (uint)y < Sector.TileCount)
                return WorldElevationSampler.SampleTile(sector.Elevation[x,y], position.X-tileX, position.Y-tileY) * WorldElevationSampler.WorldHeightPerSample;
        }
        return 0;
    }

    public IEnumerable<GoldReplicatedObject> WorldObjects => IsCurrentSession
        ? Replicas.Objects.Values.Where(obj => Sectors.ContainsKey(obj.BaseState.Sector) && Replicas.Sectors.ContainsKey(obj.BaseState.Sector) && (obj.BaseState.Flags & 0x10) == 0 && obj.Item?.InventoryId is null or 0)
        : [];

    public static async Task<SacredNetworkWorldBinding> LoadAsync(SacredWorldArchive archive, GoldWorldReplica replicas,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(replicas);
        var slot = replicas.LocalPlayerSlot;
        var sessionVersion = replicas.SessionVersion;
        var sectorRevision = replicas.SectorInterest.Revision;
        if (slot == 0 || !replicas.Objects.TryGetValue(slot, out var hero) || hero.Creature is null)
            throw new InvalidOperationException("World binding requires a seeded participant hero.");
        var entries = replicas.Sectors.Values.ToArray();
        if (!entries.Any(entry => entry.Sector == hero.BaseState.Sector))
            throw new InvalidOperationException("World binding requires confirmation of the hero's sector.");
        // Capture IDs before yielding; replicas continue updating on the connection owner's thread.
        var tasks = entries.Select(async entry =>
        {
            var sector = await archive.TryLoadSectorById(entry.Sector).ConfigureAwait(false)
                ?? throw new InvalidDataException($"Confirmed network sector {entry.Sector} is absent from authored world data.");
            return (entry.Sector, Data: sector);
        }).ToArray();
        var loaded = await Task.WhenAll(tasks).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        // A caller checks session identity again when committing this result on its update thread.
        var binding = new SacredNetworkWorldBinding(replicas, loaded.ToDictionary(row => row.Sector, row => row.Data), slot, sessionVersion, hero.BaseState.Sector);
        binding._archive=archive;
        binding.DoorStates = new(replicas, archive.ObjectScript.Placements.Where(placement => placement.Door is not null).Select(placement => placement.Door!));
        binding._patchTerrain = new(archive, binding._copies);
        binding.SectorPatches = new(archive.ReadStaticRecord, binding._patchTerrain, replicas.WorldBanks.Registrations);
        binding.SectorPatches.BindTriggerPatches(replicas.WorldBanks.TriggerPatches);
        var copies = loaded.ToDictionary(row => row.Sector, row => binding._copies.Create(row.Data));
        foreach (var sector in copies.Values) binding._copies.Synchronize(sector);
        binding._terrain = new(binding._patchTerrain.LoadVisible,
            replicas, copies.ToFrozenDictionary(), new(copies[hero.BaseState.Sector].Coord, copies.Values.ToArray(), 0), sessionVersion, sectorRevision);
        return binding;
    }
}
