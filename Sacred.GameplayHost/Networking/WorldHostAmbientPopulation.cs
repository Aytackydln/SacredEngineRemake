using Sacred.Assets.Executable;
using Sacred.Core.Runtime;
using Sacred.Core.World;
using Sacred.Core.World.Sector;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Connection;
using Sacred.Network.Protocol;

namespace Sacred.GameplayHost.Networking;

/// <summary>One ambient scan on the actual first-instruction lifetime. The
/// preview validates factory placement/reservations without advancing live RNG.</summary>
public sealed class WorldHostAmbientPopulation
{
    private readonly SacredAmbientPopulationDefinitions _definitions;
    private readonly SacredSectorIndex _index;
    private readonly SacredSectorGrid _grid;
    private readonly WorldHostAmbientCreatureFactory _factory;
    private int _requestedAtIssued = -1;
    public bool IsCompleted { get; private set; }
    public int Created => _factory.Created.Count;
    public IReadOnlyList<uint> References => _factory.Created;
    public IReadOnlyList<GoldMessage> Publications => _factory.Publications;
    public int Planned { get; private set; }

    internal WorldHostAmbientPopulation(string installation, WorldScriptExecutionContext context, WorldScriptCreatureWorld world)
    {
        _definitions = SacredAmbientPopulationDefinitions.Load(installation);
        _index = SacredSectorIndex.Read(Path.Combine(installation, "World", "sectors.keyx")); _grid = _index.CreateGrid();
        _factory = world.AmbientFactory(context);
    }

    internal async Task<bool> RunAsync(WorldHostInitializationSession session, GoldGameConnection connection,
        WorldScriptExecutionContext context, WorldScriptSectorEnvironment environment, CancellationToken token)
    {
        if (IsCompleted) return true;
        var world = (WorldScriptCreatureWorld)context.CreatureWorld!;
        _factory.BindSectorRuntime(session.Replicas.SectorWork.Runtime);
        var terrain = world.Terrain ?? throw new InvalidOperationException("Ambient scan requires the actual shared terrain.");
        if (context.CurrentSector is not null || context.IsExecutingStartup || context.IsInvalidated || !context.PostStartup.IsCompleted)
            throw new InvalidOperationException("Ambient scan needs valid completed campaign work outside a running callback.");
        var preview = new SacredRandomSequence(world.Random.State); var planned = 0;
        Scan(preview, (type, x, y) =>
        {
            token.ThrowIfCancellationRequested(); _ = _factory.Validate(type, x, y); planned++;
            // The compared factory consumes two constructor draws, followed by
            // the native EnterSector orientation's two draws.
            for (var index = 0; index < 4; index++) _ = preview.Next();
        });
        Planned = planned;
        if (session.References.Available < planned)
        {
            if (_requestedAtIssued != session.References.Issued)
            {
                _requestedAtIssued = session.References.Issued;
                await connection.SendAsync(Sacred.Network.Gameplay.GoldReferenceCodec.CreateRequest(1000, session.Admission.PlayerSlot), token).ConfigureAwait(false);
            }
            return false;
        }
        context.CurrentSector = environment; context.IsEnteringSector = true;
        try
        {
            Scan(world.Random, _factory.Create);
            if (world.Random.State != preview.State || Created != planned) throw new InvalidOperationException("Ambient generation diverged from its validated reservation/RNG plan.");
            foreach (var message in Publications) await connection.SendAsync(message, token).ConfigureAwait(false);
            IsCompleted = true;
            return true;
        }
        catch { context.IsInvalidated = true; connection.Stop(); throw; }
        finally { context.CurrentSector = null; context.IsEnteringSector = false; }

        void Scan(SacredRandomSequence random, Action<uint, int, int> create)
        {
            var value = environment.Value;
            new WorldAmbientPopulationGenerator(_definitions, random, Tile, Fog, create)
                .Execute(environment.Coordinate, value.Region, value.LiquidStyleA, value.LiquidStyleB);
        }
        SacredAmbientTile? Tile(int x, int y)
        {
            token.ThrowIfCancellationRequested();
            return terrain.Tiles.GetTile(x, y) is { } tile ? new(tile.Properties,
                new(tile.VisualElevationLeft, tile.VisualElevationTop, tile.VisualElevationRight, tile.VisualElevationBottom)) : null;
        }
        bool Fog(int x, int y)
        {
            if (!_index.TryGetSector(_grid.GetSector(x, y), out var source)) return false;
            var value = source.Environment;
            for (var index = 0; index < 4; index++)
                if (((byte)value.WeatherValid & 1 << index) != 0 && value.Weather[index].Type == SectorWeatherKind.GroundFog) return true;
            return false;
        }
    }
}
