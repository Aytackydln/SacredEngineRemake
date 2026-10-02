using System.Collections.Concurrent;
using System.Numerics;
using Sacred.Core.World.Sector;

namespace Sacred.World;

public sealed class WorldStreamer : IDisposable
{
    public const int SectorTileCount = Sector.TileCount;

    private readonly SacredWorldArchive _worldArchive;
    private readonly ConcurrentQueue<SectorLoadResult> _completedLoads = new();
    private readonly Dictionary<SectorCoord, Sector> _loaded = new();
    private readonly HashSet<SectorCoord> _loading = [];
    private readonly HashSet<SectorCoord> _needed = [];
    private readonly List<SectorCoord> _toRemove = new(9);
    private readonly List<Task> _sectorLoadTasks = new(16);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _wakeSignal = new(0);
    private readonly Task _streamingTask;

    private StreamRequest _requestedCenter;
    private VisibleWorld _visibleWorld = VisibleWorld.Empty;
    private SectorCoord? _centerSector;
    private SectorCoord _appliedAhead;
    private int _appliedRequestVersion = -1;
    private int _requestVersion;
    private int _wakeSignaled;
    private bool _disposed;

    public WorldStreamer(SacredWorldArchive worldArchive)
    {
        _worldArchive = worldArchive;
        _requestedCenter = new StreamRequest(worldArchive.StartSector, default, 0);
        _streamingTask = Task.Run(RunStreamingLoopAsync);
        SignalWorker();
    }

    public VisibleWorld VisibleWorld => Volatile.Read(ref _visibleWorld);
    public SectorCoord StartSector => _worldArchive.StartSector;

    public WorldZone GetZone(Vector2 worldPosition) =>
        _worldArchive.GetZone(worldPosition.X, worldPosition.Y);

    public void CenterOnSector(int sx, int sy)
    {
        RequestCenter(new SectorCoord(sx, sy));
    }

    public void Update(Vector2 cameraWorldCenter, Vector2 movementDirection = default)
    {
        var center = new SectorCoord(
            (int)MathF.Floor(cameraWorldCenter.X / SectorTileCount),
            (int)MathF.Floor(cameraWorldCenter.Y / SectorTileCount));

        var ahead = SectorPreloadDirection.Ahead(cameraWorldCenter, movementDirection);
        var current = Volatile.Read(ref _requestedCenter);
        if (current.Center == center && current.Ahead == ahead) return;
        RequestCenter(center, ahead);
    }

    private void RequestCenter(SectorCoord center, SectorCoord ahead = default)
    {
        if (_disposed)
            return;

        Volatile.Write(ref _requestedCenter, new StreamRequest(center, ahead, Interlocked.Increment(ref _requestVersion)));
        SignalWorker();
    }

    private async Task RunStreamingLoopAsync()
    {
        var cancellationToken = _shutdown.Token;
        try
        {
            while (true)
            {
                await _wakeSignal.WaitAsync(cancellationToken);
                Interlocked.Exchange(ref _wakeSignaled, 0);
                ProcessStreamingWork(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void ProcessStreamingWork(CancellationToken cancellationToken)
    {
        var visibleWorldChanged = ApplyRequestedCenter(cancellationToken);

        while (_completedLoads.TryDequeue(out var load))
        {
            _loading.Remove(load.Coord);

            if (_centerSector is not { } center || !IsInRetainedRange(load.Coord, center))
                continue;

            if (load.Sector is not null)
                _loaded[load.Coord] = load.Sector;

            visibleWorldChanged = true;
        }

        if (visibleWorldChanged && _centerSector is { } currentCenter)
            PublishVisibleWorld(currentCenter);
    }

    private bool ApplyRequestedCenter(CancellationToken cancellationToken)
    {
        var request = Volatile.Read(ref _requestedCenter);
        if (_appliedRequestVersion == request.Version)
            return false;

        _appliedRequestVersion = request.Version;
        _centerSector = request.Center;
        _appliedAhead = request.Ahead;
        EnsureLoaded(request, cancellationToken);
        return true;
    }

    private void EnsureLoaded(StreamRequest request, CancellationToken cancellationToken)
    {
        _sectorLoadTasks.RemoveAll(static task => task.IsCompleted);
        _needed.Clear();
        // Always request visible data first. The next strip is lower priority.
        RequestArea(request.Center, cancellationToken);
        if (request.Ahead != default)
            RequestArea(new SectorCoord(request.Center.X + request.Ahead.X, request.Center.Y + request.Ahead.Y), cancellationToken);
        _toRemove.Clear();
        foreach (var key in _loaded.Keys)
            if (!IsInRetainedRange(key, request.Center)) _toRemove.Add(key);
        foreach (var key in _toRemove) _loaded.Remove(key);
        Console.WriteLine($"World streaming: center {request.Center.X},{request.Center.Y}; ahead {request.Ahead.X},{request.Ahead.Y}; {_needed.Count} requested, {_loaded.Count} retained.");
    }

    private void RequestArea(SectorCoord center, CancellationToken cancellationToken)
    {
        RequestSector(center, cancellationToken);
        for (var y = -1; y <= 1; y++)
        for (var x = -1; x <= 1; x++)
            RequestSector(new SectorCoord(center.X + x, center.Y + y), cancellationToken);
    }

    private void RequestSector(SectorCoord coord, CancellationToken cancellationToken)
    {
        _needed.Add(coord);
        if (!_loaded.ContainsKey(coord) && _loading.Add(coord))
            _sectorLoadTasks.Add(Task.Run(() => LoadSectorAsync(coord, cancellationToken), CancellationToken.None));
    }

    private async Task LoadSectorAsync(SectorCoord coord, CancellationToken cancellationToken)
    {
        Sector? sector = null;
        try
        {
            sector = await _worldArchive.TryLoadSector(coord);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch
        { }

        if (cancellationToken.IsCancellationRequested)
            return;

        _completedLoads.Enqueue(new SectorLoadResult(coord, sector));
        SignalWorker();
    }

    private void PublishVisibleWorld(SectorCoord center)
    {
        var sectors = new List<Sector>(9);
        for (var y = -1; y <= 1; y++)
        for (var x = -1; x <= 1; x++)
            if (_loaded.TryGetValue(new SectorCoord(center.X + x, center.Y + y), out var s))
                sectors.Add(s);

        var preloaded = new List<Sector>(3);
        foreach (var coord in _needed)
            if (!IsInVisibleRange(coord, center) && _loaded.TryGetValue(coord, out var sector)) preloaded.Add(sector);
        var loadingPreloaded = 0;
        foreach (var coord in _loading)
            if (_needed.Contains(coord) && !IsInVisibleRange(coord, center)) loadingPreloaded++;
        Volatile.Write(ref _visibleWorld, new VisibleWorld(center, sectors, CountLoadingSectors(center))
        {
            PreloadedSectors = preloaded,
            PrefetchCenterSector = _appliedAhead == default ? null : new SectorCoord(center.X + _appliedAhead.X, center.Y + _appliedAhead.Y),
            LoadingPreloadedSectors = loadingPreloaded
        });
    }

    private int CountLoadingSectors(SectorCoord center)
    {
        var count = 0;
        foreach (var coord in _loading)
            if (IsInVisibleRange(coord, center))
                count++;

        return count;
    }

    private static bool IsInRetainedRange(SectorCoord coord, SectorCoord center) =>
        Math.Abs(coord.X - center.X) <= 2 && Math.Abs(coord.Y - center.Y) <= 2;

    private static bool IsInVisibleRange(SectorCoord coord, SectorCoord center) =>
        Math.Abs(coord.X - center.X) <= 1 && Math.Abs(coord.Y - center.Y) <= 1;

    private void SignalWorker()
    {
        if (_disposed)
            return;

        if (Interlocked.Exchange(ref _wakeSignaled, 1) == 0)
            _wakeSignal.Release();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _shutdown.Cancel();
        _streamingTask.Wait();
        Task.WhenAll(_sectorLoadTasks).GetAwaiter().GetResult();
        _worldArchive.Dispose();
        _wakeSignal.Dispose();
        _shutdown.Dispose();
    }

    private sealed record StreamRequest(SectorCoord Center, SectorCoord Ahead, int Version);

    private readonly record struct SectorLoadResult(SectorCoord Coord, Sector? Sector);
}
