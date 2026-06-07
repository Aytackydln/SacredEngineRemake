using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Core.World;

namespace Sacred.Engine.World;

public sealed class WorldStreamer : IDisposable
{
    public const int SectorTileCount = Sector.TileCount;

    private readonly SacredWorldArchive _worldArchive;
    private readonly ConcurrentQueue<SectorLoadResult> _completedLoads = new();
    private readonly Dictionary<SectorCoord, Sector> _loaded = new();
    private readonly HashSet<SectorCoord> _loading = [];
    private readonly HashSet<SectorCoord> _needed = [];
    private readonly List<SectorCoord> _toRemove = new(9);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _wakeSignal = new(0);
    private readonly Task _streamingTask;

    private StreamRequest _requestedCenter;
    private VisibleWorld _visibleWorld = VisibleWorld.Empty;
    private SectorCoord? _centerSector;
    private int _appliedRequestVersion = -1;
    private int _requestVersion;
    private int _wakeSignaled;
    private bool _disposed;

    public WorldStreamer(SacredWorldArchive worldArchive)
    {
        _worldArchive = worldArchive;
        _requestedCenter = new StreamRequest(worldArchive.StartSector, 0);
        _streamingTask = Task.Run(RunStreamingLoopAsync);
        SignalWorker();
    }

    public VisibleWorld VisibleWorld => Volatile.Read(ref _visibleWorld);
    public SectorCoord StartSector => _worldArchive.StartSector;

    public void CenterOnSector(int sx, int sy)
    {
        RequestCenter(new SectorCoord(sx, sy));
    }

    public void Update(Vector2 cameraWorldCenter)
    {
        var center = new SectorCoord(
            (int)MathF.Floor(cameraWorldCenter.X / SectorTileCount),
            (int)MathF.Floor(cameraWorldCenter.Y / SectorTileCount));

        if (Volatile.Read(ref _requestedCenter).Center == center)
            return;

        RequestCenter(center);
    }

    private void RequestCenter(SectorCoord center)
    {
        if (_disposed)
            return;

        Volatile.Write(ref _requestedCenter, new StreamRequest(center, Interlocked.Increment(ref _requestVersion)));
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

            if (_centerSector is not { } center || !IsInVisibleRange(load.Coord, center))
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
        Ensure3x3Loaded(request.Center, cancellationToken);
        return true;
    }

    private void Ensure3x3Loaded(SectorCoord center, CancellationToken cancellationToken)
    {
        _needed.Clear();
        var coords = new int[][]
        {
            [0, 0], [0, -1],  [1, 1], [-1, 0], [1, 0], [0, 1], [1, -1], [-1, 1], [-1, -1]
        };
        foreach (var coord in coords)
        {
            var x = coord[0];
            var y = coord[1];
            
            var c = new SectorCoord(center.X + x, center.Y + y);
            _needed.Add(c);
            if (!_loaded.ContainsKey(c) && _loading.Add(c))
                _ = Task.Run(() => LoadSectorAsync(c, cancellationToken), CancellationToken.None);
        }

        _toRemove.Clear();
        foreach (var key in _loaded.Keys)
        {
            if (!_needed.Contains(key))
                _toRemove.Add(key);
        }

        foreach (var key in _toRemove)
            _loaded.Remove(key);
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

        Volatile.Write(ref _visibleWorld, new VisibleWorld(center, sectors, CountLoadingSectors(center)));
    }

    private int CountLoadingSectors(SectorCoord center)
    {
        var count = 0;
        foreach (var coord in _loading)
            if (IsInVisibleRange(coord, center))
                count++;

        return count;
    }

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
        _worldArchive.Dispose();

        _streamingTask.Wait();
    }

    private sealed record StreamRequest(SectorCoord Center, int Version);

    private readonly record struct SectorLoadResult(SectorCoord Coord, Sector? Sector);
}
