using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Terrain;

/// <summary>Reuses large sector targets after both composition and foreground frame fences retire.</summary>
internal sealed class Dx12SectorTargetPool : IDisposable
{
    private const int MaximumPairs = 2;
    private const long MaximumBytes = 320L * 1024 * 1024;
    private readonly ConcurrentQueue<Dx12ComposedSector> _returned = new();
    // Only the compositor thread accesses available targets until it has been joined.
    private readonly List<Dx12ComposedSector> _available = new(MaximumPairs);
    private long _bytes;

    public void Return(Dx12ComposedSector targets) => _returned.Enqueue(targets);

    public void CollectReturned()
    {
        while (_returned.TryDequeue(out var targets))
        {
            var bytes = Bytes(targets.BaseTexture.Description);
            if (_available.Count >= MaximumPairs || _bytes + bytes > MaximumBytes)
            {
                targets.Dispose();
                continue;
            }
            _available.Add(targets);
            _bytes += bytes;
        }
    }

    public Dx12ComposedSector? TryRent(int width, int height)
    {
        CollectReturned();
        for (var index = _available.Count - 1; index >= 0; index--)
        {
            var targets = _available[index];
            var description = targets.BaseTexture.Description;
            if (description.Width != (ulong)width || description.Height != (uint)height) continue;
            _available.RemoveAt(index);
            _bytes -= Bytes(description);
            EngineLog.WriteLine($"Sector GPU targets reused: {width}x{height}; {Bytes(description) / (1024.0 * 1024):F1} MiB allocation avoided.");
            return targets;
        }
        return null;
    }

    public void Dispose()
    {
        while (_returned.TryDequeue(out var targets)) targets.Dispose();
        foreach (var targets in _available) targets.Dispose();
        _available.Clear();
        _bytes = 0;
    }

    private static long Bytes(ResourceDescription description) =>
        checked((long)description.Width * description.Height * 4 * Dx12SectorTextureCache.TexturesPerSector);
}
