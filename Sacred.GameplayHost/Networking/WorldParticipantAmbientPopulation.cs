using Sacred.Core.Runtime;
using Sacred.Core.World;
using Sacred.Core.World.Sector;
using Sacred.GameplayHost.World;
using Sacred.Network.Gameplay;
using Sacred.World.Networking;

namespace Sacred.GameplayHost.Networking;

/// <summary>Loads the native scan's border, reserves real references and waits
/// for ordered creation sends. The callback and RNG are never repeated while waiting.</summary>
public sealed class WorldParticipantAmbientPopulation
{
    private readonly WorldCreatureConstructionAssets _assets;
    private readonly GoldObjectReferencePool _references;
    private readonly GoldWorldReplica _replicas;
    private readonly SacredSectorIndex _index;
    private readonly SacredSectorGrid _grid;
    private readonly SacredRandomSequence _random;
    private readonly SacredNetworkWorldBinding _world;
    private readonly WorldParticipantPublications _publications;
    private readonly WorldAmbientCreatureFactory _factory;
    private WorldParticipantSectorPopulation? _request;
    private Task<IReadOnlyDictionary<SectorCoord, Sector>>? _load;
    private IReadOnlyDictionary<SectorCoord, Sector>? _terrain;
    private bool _generated;
    private int _requestedAtIssued = -1;
    public WorldParticipantAmbientPopulation(WorldCreatureConstructionAssets assets, GoldObjectReferencePool references,
        GoldWorldReplica replicas, SacredSectorIndex index, SacredRandomSequence random, SacredNetworkWorldBinding world,
        IGameplayNetworkTransport transport)
    {
        (_assets, _references, _replicas, _index, _random, _world) = (assets, references, replicas, index, random, world);
        _grid = index.CreateGrid();
        _publications = new(transport, replicas);
        _factory = new(assets, references, replicas, _grid, random, _publications);
    }
    public bool Execute(WorldParticipantSectorPopulation request)
    {
        if (!_world.IsCurrentSession) throw new InvalidOperationException("Population belongs to a retired world session.");
        if (request.RestoredObjects.Count != 0) throw new InvalidDataException("Stored sector objects require native restoration and publication.");
        if (!ReferenceEquals(_request, request))
        {
            if (_request is not null && !_publications.IsComplete) throw new InvalidOperationException("Population cannot overtake preceding sends.");
            _request = request; _generated = false; _terrain = null;
            _load = _world.LoadPopulationTerrainAsync(request.Sector);
        }
        if (!_load!.IsCompleted) return false;
        _terrain ??= _load.GetAwaiter().GetResult();
        var environment = request.Environment ?? throw new InvalidDataException("Population requires its executed sector environment.");
        if (!_generated)
        {
            // Preflight uses a private RNG copy. It validates every constructor and
            // counts the exact reservations before the live sequence changes.
            var planned = 0; var preview = new SacredRandomSequence(_random.State);
            Scan(preview, (type, _, _) => { _factory.Validate(type); planned++; for (var i = 0; i < 4; i++) _ = preview.Next(); });
            if (_references.Available < planned)
            {
                if (_requestedAtIssued != _references.Issued)
                {
                    _requestedAtIssued = _references.Issued;
                    _publications.Send(GoldReferenceCodec.CreateRequest(1000, _replicas.LocalPlayerSlot));
                }
                _ = _publications.IsComplete; // Observe send failures while grants are pending.
                return false;
            }
            if (planned > 0 && _references.Available < 500 && _requestedAtIssued != _references.Issued)
            {
                _requestedAtIssued = _references.Issued;
                _publications.Send(GoldReferenceCodec.CreateRequest(1000, _replicas.LocalPlayerSlot));
            }
            Scan(_random, _factory.Create);
            _generated = true;
            Console.WriteLine($"Network ambient population: sector {request.Sector}, {planned} creatures created; waiting for publication.");
        }
        return _publications.IsComplete;

        void Scan(SacredRandomSequence sequence, Action<uint, int, int> create)
        {
            var scan = new WorldAmbientPopulationGenerator(_assets.Ambient, sequence, Tile, GroundFog, create);
            var value = environment.Value;
            scan.Execute(environment.Coordinate, value.Region, value.LiquidStyleA, value.LiquidStyleB);
        }
    }
    private SacredAmbientTile? Tile(int x, int y)
    {
        if (x < 0 || y < 0 || !_terrain!.TryGetValue(new(x / 64, y / 64), out var sector)) return null;
        return new(sector.Pathing[x % 64, y % 64].Properties, sector.VisualElevation[x % 64, y % 64]);
    }
    private bool GroundFog(int x, int y)
    {
        var id = _grid.GetSector(x, y);
        if (!_index.TryGetSector(id, out var sector)) return false;
        var environment = sector.Environment;
        for (var i = 0; i < 4; i++)
            if (((byte)environment.WeatherValid & 1 << i) != 0 && environment.Weather[i].Type == SectorWeatherKind.GroundFog) return true;
        return false;
    }
}
