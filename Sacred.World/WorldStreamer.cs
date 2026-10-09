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
    private SectorLoadArea _visibleArea;
    private SectorLoadMode _appliedMode;
    private int _appliedRequestVersion = -1;
    private int _requestVersion;
    private int _wakeSignaled;
    private bool _disposed;

    public WorldStreamer(SacredWorldArchive worldArchive)
        : this(worldArchive, SectorLoadMode.Four, new Vector2(
            (worldArchive.StartSector.X + 0.5f) * SectorTileCount,
            (worldArchive.StartSector.Y + 0.5f) * SectorTileCount))
    {
    }

    public WorldStreamer(SacredWorldArchive worldArchive, SectorLoadMode mode, Vector2 initialPosition)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        _worldArchive = worldArchive;
        _requestedCenter = new StreamRequest(new SectorCoord(
            (int)MathF.Floor(initialPosition.X / SectorTileCount),
            (int)MathF.Floor(initialPosition.Y / SectorTileCount)), default,
            SectorLoadArea.Quarter(initialPosition), mode, 0);
        _streamingTask = Task.Run(RunStreamingLoopAsync);
        SignalWorker();
    }

    public VisibleWorld VisibleWorld => Volatile.Read(ref _visibleWorld);
    public SectorCoord StartSector => _worldArchive.StartSector;
    public SectorLoadMode SectorLoadMode => Volatile.Read(ref _requestedCenter).Mode;

    public void SetSectorLoadMode(SectorLoadMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var current = Volatile.Read(ref _requestedCenter);
        if (current.Mode == mode) return;
        RequestCenter(current.Center, current.Ahead, current.Quarter, mode);
    }

    public WorldZone GetZone(Vector2 worldPosition) =>
        _worldArchive.GetZone(worldPosition.X, worldPosition.Y);

    public void CenterOnSector(int sx, int sy)
    {
        RequestCenter(new SectorCoord(sx, sy), quarter: new SectorCoord(1, 1));
    }

    public void Update(Vector2 cameraWorldCenter, Vector2 movementDirection = default)
    {
        var center = new SectorCoord(
            (int)MathF.Floor(cameraWorldCenter.X / SectorTileCount),
            (int)MathF.Floor(cameraWorldCenter.Y / SectorTileCount));

        var current = Volatile.Read(ref _requestedCenter);
        var quarter = SectorLoadArea.Quarter(cameraWorldCenter);
        var ahead = current.Mode == SectorLoadMode.Nine
            ? SectorPreloadDirection.Ahead(cameraWorldCenter, movementDirection) : default;
        if (current.Center == center && current.Ahead == ahead && current.Quarter == quarter) return;
        RequestCenter(center, ahead, quarter);
    }

    private void RequestCenter(SectorCoord center, SectorCoord ahead = default,
        SectorCoord quarter = default, SectorLoadMode? mode = null)
    {
        if (_disposed)
            return;

        var selectedMode = mode ?? SectorLoadMode;
        Volatile.Write(ref _requestedCenter, new StreamRequest(center,
            selectedMode == SectorLoadMode.Nine ? ahead : default, quarter, selectedMode,
            Interlocked.Increment(ref _requestVersion)));
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
        _appliedMode = request.Mode;
        _visibleArea = SectorLoadArea.Create(request.Center, request.Quarter, request.Mode);
        EnsureLoaded(request, cancellationToken);
        return true;
    }

    private void EnsureLoaded(StreamRequest request, CancellationToken cancellationToken)
    {
        _sectorLoadTasks.RemoveAll(static task => task.IsCompleted);
        _needed.Clear();
        // Always request visible data first. The next strip is lower priority.
        RequestSector(request.Center, cancellationToken);
        RequestArea(_visibleArea, cancellationToken);
        if (request.Ahead != default)
            RequestArea(SectorLoadArea.Create(new SectorCoord(request.Center.X + request.Ahead.X,
                request.Center.Y + request.Ahead.Y), request.Quarter, request.Mode), cancellationToken);
        _toRemove.Clear();
        foreach (var key in _loaded.Keys)
            if (!IsInRetainedRange(key, request.Center)) _toRemove.Add(key);
        foreach (var key in _toRemove) _loaded.Remove(key);
        Console.WriteLine($"World streaming: {(int)request.Mode} sectors; center {request.Center.X},{request.Center.Y}; quarter {request.Quarter.X},{request.Quarter.Y}; ahead {request.Ahead.X},{request.Ahead.Y}; {_needed.Count} requested, {_loaded.Count} retained.");
    }

    private void RequestArea(SectorLoadArea area, CancellationToken cancellationToken)
    {
        for (var y = area.First.Y; y <= area.Last.Y; y++)
        for (var x = area.First.X; x <= area.Last.X; x++)
            RequestSector(new SectorCoord(x, y), cancellationToken);
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
        for (var y = _visibleArea.First.Y; y <= _visibleArea.Last.Y; y++)
        for (var x = _visibleArea.First.X; x <= _visibleArea.Last.X; x++)
            if (_loaded.TryGetValue(new SectorCoord(x, y), out var s))
                sectors.Add(s);

        var preloaded = new List<Sector>(3);
        foreach (var coord in _needed)
            if (!_visibleArea.Contains(coord) && _loaded.TryGetValue(coord, out var sector)) preloaded.Add(sector);
        var loadingPreloaded = 0;
        foreach (var coord in _loading)
            if (_needed.Contains(coord) && !_visibleArea.Contains(coord)) loadingPreloaded++;
        Volatile.Write(ref _visibleWorld, new VisibleWorld(center, sectors, CountLoadingSectors())
        {
            PreloadedSectors = preloaded,
            PrefetchCenterSector = _appliedAhead == default ? null : new SectorCoord(center.X + _appliedAhead.X, center.Y + _appliedAhead.Y),
            LoadingPreloadedSectors = loadingPreloaded
        });
    }

    private int CountLoadingSectors()
    {
        var count = 0;
        foreach (var coord in _loading)
            if (_visibleArea.Contains(coord))
                count++;

        return count;
    }

    private bool IsInRetainedRange(SectorCoord coord, SectorCoord center) =>
        _appliedMode == SectorLoadMode.Four ? _visibleArea.Contains(coord) :
            Math.Abs(coord.X - center.X) <= 2 && Math.Abs(coord.Y - center.Y) <= 2;

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

    private sealed record StreamRequest(SectorCoord Center, SectorCoord Ahead,
        SectorCoord Quarter, SectorLoadMode Mode, int Version);

    private readonly record struct SectorLoadResult(SectorCoord Coord, Sector? Sector);
}
