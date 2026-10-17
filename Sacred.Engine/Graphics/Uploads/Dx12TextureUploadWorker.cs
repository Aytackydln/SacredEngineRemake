using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Sacred.Engine.Extern;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Uploads;

/// <summary>Prepares and submits bounded texture copies away from the render thread.</summary>
internal sealed class Dx12TextureUploadWorker : IDisposable
{
    private const int MaximumPendingUploads = 128;
    private readonly ConcurrentQueue<TextureUploadTicket> _requests = new();
    private readonly List<TextureUploadOperation> _finished = [];
    private readonly ID3D12CommandQueue _queue;
    private readonly ID3D12Fence _fence;
    private readonly Dx12TextureCopyRecorder _recorder;
    private readonly Dx12CopyUploadContext[] _contexts;
    private readonly AutoResetEvent _opportunity = new(false);
    private readonly AutoResetEvent _copyCompleted = new(false);
    private readonly WaitHandle[] _wakeEvents;
    private readonly object _admissionLock = new();
    private readonly object _stopLock = new();
    private readonly Thread _thread;
    private TextureUploadOperation? _active;
    private ulong _fenceValue;
    private volatile bool _stopped;
    private int _stopJoined;
    private int _pending;
    private double _timeBudgetMilliseconds = 2;

    public Dx12TextureUploadWorker(ID3D12Device device)
    {
        _queue = device.CreateCommandQueue(CommandListType.Copy);
        _fence = device.CreateFence(0, FenceFlags.None);
        _recorder = new Dx12TextureCopyRecorder(device);
        _contexts = [new(device), new(device)];
        _wakeEvents = [_opportunity, _copyCompleted];
        _thread = new Thread(WorkerLoop) { IsBackground = true, Name = "Sacred texture copy worker", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
        EngineLog.WriteLine("Texture copy worker started: frame-independent, 16 MiB / 2 ms batches; two fence-tracked staging pages (32 MiB maximum).");
    }

    public int PendingCount => Volatile.Read(ref _pending);
    public long SubmittedBytes { get; private set; }
    public long SubmissionCount { get; private set; }
    public int LastSubmissionBytes { get; private set; }
    public double LastRecordingMilliseconds { get; private set; }

    public void EnableBulkUploads()
    {
        Volatile.Write(ref _timeBudgetMilliseconds, 4);
    }

    public bool TryEnqueue(string name, int width, int height, byte[] pixels, Action? releaseSource,
        out TextureUploadTicket ticket)
    {
        ticket = null!;
        if (width <= 0 || height <= 0 || pixels.LongLength < checked((long)width * height * 4))
            throw new ArgumentException($"Invalid source pixels for texture {name} ({width}x{height}).");
        // Admission and stop share a boundary: no producer can enqueue after shutdown drained the queue.
        lock (_admissionLock)
        {
            if (_stopped || PendingCount >= MaximumPendingUploads) return false;
            ticket = new TextureUploadTicket(name, width, height, pixels, releaseSource);
            Interlocked.Increment(ref _pending);
            _requests.Enqueue(ticket);
            _opportunity.Set();
        }
        return true;
    }

    public void OnForegroundFrameSubmitted() { if (PendingCount != 0) _opportunity.Set(); }

    public void Stop()
    {
        lock (_stopLock)
        {
            if (_stopJoined != 0) return;
            lock (_admissionLock) { _stopped = true; _opportunity.Set(); }
            _thread.Join();
            _stopJoined = 1;
        }
    }

    private void WorkerLoop()
    {
        try
        {
            while (true)
            {
                CollectCompleted();
                if (_stopped) break;
                Dx12CopyUploadContext? available = null;
                var nextFence = ulong.MaxValue;
                foreach (var context in _contexts)
                {
                    if (context.FenceValue == 0) available ??= context;
                    else nextFence = Math.Min(nextFence, context.FenceValue);
                }
                if (available is not null && (_active is not null || !_requests.IsEmpty))
                {
                    SubmitBudget(available);
                    continue;
                }
                // Copy retirement drives progress even while rendering is paused or frame-capped.
                if (nextFence != ulong.MaxValue)
                    _fence.SetEventOnCompletion(nextFence, _copyCompleted.SafeWaitHandle.DangerousGetHandle()).CheckError();
                WaitHandle.WaitAny(_wakeEvents);
            }
        }
        catch (Exception exception)
        {
            EngineLog.WriteLine($"Texture copy worker failed: {exception}");
            lock (_admissionLock) _stopped = true;
        }
        finally
        {
            WaitForOutstandingCopies();
            if (_active is { } active)
            {
                active.Error = new OperationCanceledException("Texture copy worker stopped.");
                Finish(active);
                _active = null;
            }
            while (_requests.TryDequeue(out var ticket))
                Finish(new TextureUploadOperation(ticket) { Error = new OperationCanceledException("Texture copy worker stopped.") });
            CollectCompleted();
        }
    }

    private void SubmitBudget(Dx12CopyUploadContext context)
    {
        if (_active is null && _requests.IsEmpty) return;
        context.Begin();
        var timer = Stopwatch.StartNew();
        var bytes = 0;
        const int budget = Dx12UploadPage.Capacity;
        while (bytes < budget && (bytes == 0 || timer.Elapsed.TotalMilliseconds < Volatile.Read(ref _timeBudgetMilliseconds)))
        {
            if (_active is null)
            {
                if (!_requests.TryDequeue(out var ticket)) break;
                _active = new TextureUploadOperation(ticket);
            }
            var active = _active;
            if (active.Ticket.IsCancelled)
            {
                active.Error = new OperationCanceledException("Texture is no longer wanted.");
                Finish(active);
                _active = null;
                continue;
            }
            try
            {
                active.Resource ??= _recorder.CreateTexture(active.Ticket);
                // Limit individual CPU copies too; the elapsed-time budget is checked between chunks.
                var copied = _recorder.RecordRows(context, active, Math.Min(budget - bytes, 1024 * 1024));
                if (copied == 0) break;
                bytes += copied;
                if (active.NextRow == active.Ticket.Height)
                {
                    Finish(active);
                    _active = null;
                }
            }
            catch (Exception exception)
            {
                active.Error = exception;
                Finish(active);
                _active = null;
            }
        }
        context.Commands.Close();
        if (bytes != 0)
        {
            _queue.ExecuteCommandLists([context.Commands]);
            context.FenceValue = ++_fenceValue;
            _queue.Signal(_fence, _fenceValue).CheckError();
            foreach (var operation in context.Operations)
                operation.FenceValue = _fenceValue;
            SubmittedBytes += bytes;
            SubmissionCount++;
            LastSubmissionBytes = bytes;
            LastRecordingMilliseconds = timer.Elapsed.TotalMilliseconds;
        }
        else context.Release();
        CollectCompleted();
    }

    private void Finish(TextureUploadOperation operation)
    {
        try { operation.Ticket.ReleaseSource(); }
        catch (Exception exception) { operation.Error ??= exception; }
        _finished.Add(operation);
    }

    private void CollectCompleted()
    {
        var completedFence = _fence.CompletedValue;
        foreach (var context in _contexts)
            if (context.FenceValue != 0 && context.FenceValue <= completedFence) context.Release();
        for (var index = _finished.Count - 1; index >= 0; index--)
        {
            var operation = _finished[index];
            if (operation.FenceValue > completedFence) continue;
            if (operation.Error is not null)
            {
                operation.Resource?.Dispose();
                operation.Resource = null;
            }
            operation.Ticket.Complete(operation.Resource, operation.Error);
            Interlocked.Decrement(ref _pending);
            _finished.RemoveAt(index);
        }
    }

    private void WaitForOutstandingCopies()
    {
        if (_fence.CompletedValue >= _fenceValue) return;
        var fenceEvent = Kernel32.CreateEventA(0, false, false, null);
        if (fenceEvent == 0) throw new InvalidOperationException("Failed to create texture-copy shutdown fence event.");
        try
        {
            _fence.SetEventOnCompletion(_fenceValue, fenceEvent).CheckError();
            Kernel32.WaitForSingleObject(fenceEvent, uint.MaxValue);
        }
        finally { Kernel32.CloseHandle(fenceEvent); }
    }

    public void Dispose()
    {
        Stop();
        foreach (var context in _contexts) context.Dispose();
        _opportunity.Dispose();
        _copyCompleted.Dispose();
        _fence.Dispose();
        _queue.Dispose();
    }
}
