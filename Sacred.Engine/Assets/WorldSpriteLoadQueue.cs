using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Sacred.Engine.Assets;

/// <summary>
/// Serializes background world-sprite construction so a newly visible sector
/// cannot flood the thread pool with one load per static object.
/// </summary>
internal sealed class WorldSpriteLoadQueue : IDisposable
{
    private static readonly AssetLoadPriority[] PrioritySchedule =
    [
        AssetLoadPriority.Critical,
        AssetLoadPriority.Critical,
        AssetLoadPriority.Visible,
        AssetLoadPriority.Visible,
        AssetLoadPriority.Background
    ];

    private readonly ConcurrentQueue<Func<Task>>[] _requests =
    [
        new(),
        new(),
        new()
    ];
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _workAvailable = new(0);
    private readonly Thread _worker;
    private int _accepting = 1;
    private int _scheduleIndex;

    public WorldSpriteLoadQueue()
    {
        _worker = new Thread(Process)
        {
            IsBackground = true,
            Name = "Sacred world sprite builder",
            Priority = ThreadPriority.BelowNormal
        };
        _worker.Start();
    }

    public void Enqueue(Func<Task> request, AssetLoadPriority priority = AssetLoadPriority.Visible)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Volatile.Read(ref _accepting) == 0)
            return;

        _requests[(int)priority].Enqueue(request);
        try
        {
            _workAvailable.Release();
        }
        catch (ObjectDisposedException)
        {
            // Disposal won the race; the request no longer owns resources.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _accepting, 0) == 0)
            return;

        _stop.Cancel();
        _workAvailable.Release();
        _worker.Join();
        _stop.Dispose();
        _workAvailable.Dispose();
    }

    private void Process()
    {
        try
        {
            while (true)
            {
                _workAvailable.Wait(_stop.Token);
                if (!TryDequeue(out var request))
                    continue;

                try
                {
                    request().GetAwaiter().GetResult();
                }
                catch
                {
                    // Individual loaders publish their own failure result.
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
    }

    private bool TryDequeue(out Func<Task> request)
    {
        for (var offset = 0; offset < PrioritySchedule.Length; offset++)
        {
            var scheduleIndex = (_scheduleIndex + offset) % PrioritySchedule.Length;
            var priority = PrioritySchedule[scheduleIndex];
            if (!_requests[(int)priority].TryDequeue(out request!))
                continue;

            _scheduleIndex = (scheduleIndex + 1) % PrioritySchedule.Length;
            return true;
        }

        request = null!;
        return false;
    }
}
