using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Sacred.Engine.Platform;

internal static class Win32EventAwaiter
{
    public static Task WaitAsync(nint eventHandle, CancellationToken cancellationToken = default)
    {
        if (eventHandle == 0)
            throw new ArgumentException("A valid Win32 event handle is required.", nameof(eventHandle));

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);

        var waitHandle = new NativeWaitHandle(eventHandle);
        var state = new RegisteredWaitState(waitHandle, cancellationToken);
        state.Register();
        return state.Task;
    }

    private sealed class RegisteredWaitState(WaitHandle waitHandle, CancellationToken cancellationToken) : IDisposable
    {
        private readonly Lock _gate = new();
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private RegisteredWaitHandle? _registeredWait;
        private CancellationTokenRegistration _cancellationRegistration;
        private bool _completed;

        public Task Task => _completion.Task;

        public void Register()
        {
            lock (_gate)
            {
                _registeredWait = ThreadPool.RegisterWaitForSingleObject(
                    waitHandle,
                    static (state, timedOut) => ((RegisteredWaitState)state!).Complete(),
                    this,
                    Timeout.Infinite,
                    executeOnlyOnce: true);

                if (cancellationToken.CanBeCanceled)
                {
                    _cancellationRegistration = cancellationToken.Register(
                        static state => ((RegisteredWaitState)state!).Cancel(),
                        this);
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_completed) return;
                _completed = true;
                DisposeCore();
            }
        }

        private void Complete()
        {
            lock (_gate)
            {
                if (_completed) return;
                _completed = true;
                _completion.TrySetResult();
                DisposeCore();
            }
        }

        private void Cancel()
        {
            lock (_gate)
            {
                if (_completed) return;
                _completed = true;
                _completion.TrySetCanceled(cancellationToken);
                DisposeCore();
            }
        }

        private void DisposeCore()
        {
            _cancellationRegistration.Dispose();
            _registeredWait?.Unregister(null);
            waitHandle.Dispose();
        }
    }

    private sealed class NativeWaitHandle : WaitHandle
    {
        public NativeWaitHandle(nint handle, bool ownsHandle = false)
        {
            SafeWaitHandle = new SafeWaitHandle(handle, ownsHandle);
        }
    }
}
