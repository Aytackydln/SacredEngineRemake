using Sacred.Core.World.Sector;

namespace Sacred.World.Networking;

/// <summary>Native getSector (0x634340) lazily loads anchor/floor owners, including
/// sectors outside the visible neighborhood. Pending loads delay the entire tile pass.</summary>
internal sealed class SacredNetworkPatchTerrain : IDisposable
{
    private readonly Func<SectorCoord, Task<Sector?>>? _load;
    private readonly Func<ushort, SectorCoord?>? _coordinate;
    private readonly SacredNetworkSectorCopies? _copies;
    private readonly Dictionary<SectorCoord, Task<Sector?>> _pending = [];
    private readonly Dictionary<SectorCoord, Sector?> _loaded = [];
    private IReadOnlyDictionary<ushort, Sector> _visible = new Dictionary<ushort, Sector>();
    public SacredNetworkPatchTerrain(SacredWorldArchive archive, SacredNetworkSectorCopies copies)
        : this(archive.TryLoadSector, id => archive.TryGetSectorCoordinate(id, out var coord) ? coord : null, copies) { }
    public SacredNetworkPatchTerrain(Func<SectorCoord, Task<Sector?>> load, Func<ushort, SectorCoord?> coordinate, SacredNetworkSectorCopies copies)
        => (_load, _coordinate, _copies) = (load, coordinate, copies);
    public SacredNetworkPatchTerrain() { }
    public bool IsReady { get; private set; }
    public void Begin(IReadOnlyDictionary<ushort, Sector> visible) { _visible = visible; IsReady = true; }
    public Sector? At(int x, int y) => x < 0 || y < 0 ? null : Get(new(x / Sector.TileCount, y / Sector.TileCount));
    public Sector? ById(ushort id) => _visible.TryGetValue(id, out var sector) ? sector :
        _coordinate?.Invoke(id) is { } coord ? Get(coord) : null;
    private Sector? Get(SectorCoord coord)
    {
        var visible = _visible.Values.FirstOrDefault(sector => sector.Coord == coord);
        if (visible is not null) return visible;
        if (_loaded.TryGetValue(coord, out var loaded)) return loaded;
        if (_load is null) return null;
        if (!_pending.TryGetValue(coord, out var task)) _pending.Add(coord, task = _load(coord));
        if (!task.IsCompleted) { IsReady = false; return null; }
        _pending.Remove(coord);
        var source = task.GetAwaiter().GetResult();
        var copy = source is null ? null : _copies!.Create(source);
        if (copy is not null) _copies!.Synchronize(copy);
        _loaded.Add(coord, copy);
        return copy;
    }
    public void Synchronize()
    {
        if (_copies is null) return;
        foreach (var sector in _visible.Values.Concat(_loaded.Values.OfType<Sector>())) _copies.Synchronize(sector);
    }
    public async Task<Sector?> LoadVisible(ushort id)
    {
        if (_load is null || _coordinate?.Invoke(id) is not { } coord) return null;
        if (_loaded.TryGetValue(coord, out var existing)) return existing;
        var source = await _load(coord).ConfigureAwait(false);
        return source is null ? null : _copies!.Create(source);
    }
    public void Dispose()
    {
        foreach (var task in _pending.Values)
            _ = task.ContinueWith(failed => { _ = failed.Exception; }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }
}
