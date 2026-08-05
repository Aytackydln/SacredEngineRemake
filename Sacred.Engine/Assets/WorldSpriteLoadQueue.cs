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
    private readonly ConcurrentQueue<Func<Task>> _requests = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _workAvailable = new(0);
    private readonly Task _worker;
    private bool _disposed;

    public WorldSpriteLoadQueue()
    {
        _worker = Task.Run(ProcessAsync);
    }

    public void Enqueue(Func<Task> request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_disposed)
            return;

        _requests.Enqueue(request);
        _workAvailable.Release();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _stop.Cancel();
        _workAvailable.Release();
        _worker.GetAwaiter().GetResult();
        _stop.Dispose();
        _workAvailable.Dispose();
    }

    private async Task ProcessAsync()
    {
        try
        {
            while (true)
            {
                await _workAvailable.WaitAsync(_stop.Token).ConfigureAwait(false);
                if (!_requests.TryDequeue(out var request))
                    continue;

                try
                {
                    await request().ConfigureAwait(false);
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
}
