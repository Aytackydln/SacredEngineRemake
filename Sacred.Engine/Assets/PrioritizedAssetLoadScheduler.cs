using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Sacred.Engine.Assets;

/// <summary>
/// Runs a bounded number of asset pipelines away from the shared thread-pool queue.
/// The weighted priority order keeps visible work responsive while guaranteeing
/// that background streaming continues to make progress.
/// </summary>
internal sealed class PrioritizedAssetLoadScheduler : IDisposable
{
    private static readonly AssetLoadPriority[] PrioritySchedule =
    [
        AssetLoadPriority.Critical,
        AssetLoadPriority.Critical,
        AssetLoadPriority.Critical,
        AssetLoadPriority.Critical,
        AssetLoadPriority.Visible,
        AssetLoadPriority.Visible,
        AssetLoadPriority.Background
    ];

    private readonly ConcurrentQueue<IWorkItem>[] _queues =
    [
        new(),
        new(),
        new()
    ];
    private readonly SemaphoreSlim _workAvailable = new(0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _worker;
    private int _accepting = 1;
    private int _scheduleIndex;

    public PrioritizedAssetLoadScheduler(string threadName = "Sacred asset loader")
    {
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = threadName,
            Priority = ThreadPriority.BelowNormal
        };
        _worker.Start();
    }

    public Task<T> Schedule<T>(AssetLoadPriority priority, Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (Volatile.Read(ref _accepting) == 0)
            return Task.FromException<T>(new ObjectDisposedException(nameof(PrioritizedAssetLoadScheduler)));

        var request = new WorkItem<T>(operation);
        _queues[(int)priority].Enqueue(request);
        if (Volatile.Read(ref _accepting) == 0)
            request.Cancel();
        try
        {
            _workAvailable.Release();
        }
        catch (ObjectDisposedException)
        {
            request.Cancel();
        }
        return request.Task;
    }

    public Task<T> Schedule<T>(AssetLoadPriority priority, Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return Schedule(priority, () => Task.FromResult(operation()));
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

    private void WorkerLoop()
    {
        try
        {
            while (true)
            {
                _workAvailable.Wait(_stop.Token);
                if (TryDequeue(out var request))
                    request.Execute();
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        finally
        {
            while (TryDequeue(out var request))
                request.Cancel();
        }
    }

    private bool TryDequeue(out IWorkItem request)
    {
        for (var offset = 0; offset < PrioritySchedule.Length; offset++)
        {
            var scheduleIndex = (_scheduleIndex + offset) % PrioritySchedule.Length;
            var priority = PrioritySchedule[scheduleIndex];
            if (!_queues[(int)priority].TryDequeue(out request!))
                continue;

            _scheduleIndex = (scheduleIndex + 1) % PrioritySchedule.Length;
            return true;
        }

        request = null!;
        return false;
    }

    private interface IWorkItem
    {
        void Execute();
        void Cancel();
    }

    private sealed class WorkItem<T>(Func<Task<T>> operation) : IWorkItem
    {
        private readonly TaskCompletionSource<T> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<T> Task => _completion.Task;

        public void Execute()
        {
            if (_completion.Task.IsCompleted)
                return;

            try
            {
                _completion.TrySetResult(operation().GetAwaiter().GetResult());
            }
            catch (OperationCanceledException exception)
            {
                _completion.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                _completion.TrySetException(exception);
            }
        }

        public void Cancel() =>
            _completion.TrySetException(new ObjectDisposedException(nameof(PrioritizedAssetLoadScheduler)));
    }
}
