using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Engine.Extern;

namespace Sacred.Engine.Platform;

internal sealed class Win32AsyncPump : SynchronizationContext, IDisposable
{
    private const uint Infinite = 0xFFFFFFFF;
    private const uint WaitFailed = 0xFFFFFFFF;
    private const uint QsAllInput = 0x04FF;
    private const uint MwmoInputAvailable = 0x0004;

    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _workItems = new();
    private readonly Func<bool> _processMessages;
    private readonly nint _wakeEvent;
    private bool _disposed;

    private Win32AsyncPump(Func<bool> processMessages)
    {
        _processMessages = processMessages;
        _wakeEvent = Kernel32.CreateEventA(IntPtr.Zero, false, false, null);
        if (_wakeEvent == 0)
            throw new InvalidOperationException("Failed to create Win32 async pump event.");
    }

    public static Task RunAsync(Func<Task> action, Func<bool> processMessages)
    {
        var previousContext = Current;

        using var pump = new Win32AsyncPump(processMessages);
        SetSynchronizationContext(pump);

        try
        {
            var task = action();
            pump.RunUntilCompleted(task);
            return task;
        }
        finally
        {
            SetSynchronizationContext(previousContext);
        }
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        _workItems.Enqueue((d, state));
        Kernel32.SetEvent(_wakeEvent);
    }

    private void RunUntilCompleted(Task task)
    {
        while (!task.IsCompleted)
        {
            DispatchQueuedWork();
            if (task.IsCompleted) break;

            _processMessages();
            if (task.IsCompleted) break;

            if (_workItems.IsEmpty)
                WaitForWorkOrMessage();
        }
    }

    private void DispatchQueuedWork()
    {
        while (_workItems.TryDequeue(out var workItem))
            workItem.Callback(workItem.State);
    }

    private unsafe void WaitForWorkOrMessage()
    {
        var handles = stackalloc nint[1];
        handles[0] = _wakeEvent;

        var result = User32.MsgWaitForMultipleObjectsEx(
            1,
            handles,
            Infinite,
            QsAllInput,
            MwmoInputAvailable);

        if (result == WaitFailed)
            throw new InvalidOperationException("MsgWaitForMultipleObjectsEx failed while pumping Win32 events.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Kernel32.CloseHandle(_wakeEvent);
    }
}
