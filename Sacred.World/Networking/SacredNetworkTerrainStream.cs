using System.Collections.Frozen;
using Sacred.Core.World.Sector;
using Sacred.Network.Gameplay;

namespace Sacred.World.Networking;

/// <summary>Commits confirmed terrain on the owner's update thread. Stale asynchronous loads cannot restore retired sectors.</summary>
internal sealed class SacredNetworkTerrainStream : IDisposable
{
    private readonly Func<ushort, Task<Sector?>> _loadSector;
    private readonly GoldWorldReplica _replicas;
    private readonly long _session;
    private long _revision;
    private long _loadingRevision;
    private Task<Dictionary<ushort, Sector>>? _pending;
    private bool _disposed;
    private ushort _center;
    private IReadOnlyDictionary<ushort, Sector> _published;

    public SacredNetworkTerrainStream(Func<ushort, Task<Sector?>> loadSector, GoldWorldReplica replicas,
        IReadOnlyDictionary<ushort, Sector> initial, VisibleWorld world, long session, long revision)
    {
        (_loadSector, _replicas, _session, _revision) = (loadSector, replicas, session, revision);
        Sectors = initial;
        _published = initial;
        _center = CenterId();
        World = initial.TryGetValue(_center, out var center) ? new(center.Coord, world.Sectors, world.LoadingSectors) : world;
    }

    public IReadOnlyDictionary<ushort, Sector> Sectors { get; private set; }
    public VisibleWorld World { get; private set; }
    public string? Error { get; private set; }
    public bool IsSettled => !_disposed && _replicas.SessionVersion == _session && _pending is null && Error is null && World.LoadingSectors == 0;
    // Navigation caches tile values by VisibleWorld identity.
    public void PublishTileChanges() => World = new(World.CenterSector, World.Sectors, World.LoadingSectors);

    public void Update()
    {
        if (_disposed || _replicas.SessionVersion != _session) return;
        var revision = _replicas.SectorInterest.Revision;
        var centerId = CenterId();
        if (_pending is null && _revision == revision && _center == centerId) return;
        if (_pending is { IsCompleted: true } load)
        {
            _pending = null;
            try
            {
                var loaded = load.GetAwaiter().GetResult();
                if (_loadingRevision == _replicas.SectorInterest.Revision)
                {
                    Sectors = loaded.ToFrozenDictionary();
                    _revision = _loadingRevision;
                    Error = null;
                }
            }
            catch (Exception error)
            {
                if (_loadingRevision == _replicas.SectorInterest.Revision)
                {
                    Error = error.Message;
                    _revision = _loadingRevision;
                }
            }
        }

        var confirmed = _replicas.Sectors.Keys.ToHashSet();
        if (Sectors.Keys.Any(id => !confirmed.Contains(id)))
            Sectors = Sectors.Where(pair => confirmed.Contains(pair.Key)).ToFrozenDictionary();

        if (_pending is null && _revision != revision)
        {
            Error = null;
            if (confirmed.All(Sectors.ContainsKey)) _revision = revision;
            else
            {
                _loadingRevision = revision;
                _pending = LoadAsync(confirmed, Sectors);
            }
        }
        var missing = confirmed.Count(id => !Sectors.ContainsKey(id));
        if (_replicas.SectorInterest.HasRequest)
            missing += _replicas.SectorInterest.Requested.Count(id => !confirmed.Contains(id));
        _center = centerId;
        var center = Sectors.TryGetValue(centerId, out var sector) ? sector.Coord : World.CenterSector;
        if (World.CenterSector != center || World.LoadingSectors != missing || !ReferenceEquals(_published, Sectors))
        {
            World = new(center, Sectors.Values.ToArray(), missing);
            _published = Sectors;
        }
    }

    private ushort CenterId() => _replicas.SectorInterest.Center != 0 ? _replicas.SectorInterest.Center :
        _replicas.Objects[_replicas.LocalPlayerSlot].BaseState.Sector;

    private async Task<Dictionary<ushort, Sector>> LoadAsync(HashSet<ushort> confirmed, IReadOnlyDictionary<ushort, Sector> retained)
    {
        var tasks = confirmed.Select(async id =>
        {
            var sector = retained.TryGetValue(id, out var existing) ? existing :
                await _loadSector(id).ConfigureAwait(false)
                ?? throw new InvalidDataException($"Confirmed network sector {id} is absent from authored terrain.");
            return (Id: id, Sector: sector);
        }).ToArray();
        var loaded = await Task.WhenAll(tasks).ConfigureAwait(false);
        return loaded.ToDictionary(row => row.Id, row => row.Sector);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_pending is { } load)
            _ = load.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }
}
