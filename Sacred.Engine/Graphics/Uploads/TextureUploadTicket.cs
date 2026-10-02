using System;
using System.Threading;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Uploads;

/// <summary>Transfers ownership to the render thread only after the copy fence completes.</summary>
internal sealed class TextureUploadTicket(string name, int width, int height, byte[] pixels, Action? releaseSource)
{
    private readonly object _lock = new();
    private ID3D12Resource? _resource;
    private Exception? _error;
    private bool _completed;
    private int _cancelled;
    public string Name { get; } = name;
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; private set; } = pixels;
    public bool IsCancelled => Volatile.Read(ref _cancelled) != 0;

    public void Cancel()
    {
        Interlocked.Exchange(ref _cancelled, 1);
        lock (_lock)
        {
            _resource?.Dispose();
            _resource = null;
        }
    }

    public bool TryTake(out ID3D12Resource? resource, out Exception? error)
    {
        lock (_lock)
        {
            resource = null;
            error = null;
            if (!_completed)
                return false;
            resource = _resource;
            error = _error;
            _resource = null;
            return true;
        }
    }

    public void ReleaseSource()
    {
        Pixels = [];
        releaseSource?.Invoke();
        releaseSource = null;
    }

    public void Complete(ID3D12Resource? resource, Exception? error)
    {
        lock (_lock)
        {
            if (IsCancelled)
                resource?.Dispose();
            else
                _resource = resource;
            _error = error;
            _completed = true;
        }
    }
}
