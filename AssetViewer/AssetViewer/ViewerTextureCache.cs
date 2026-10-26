using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;

namespace AssetViewer.AssetViewer;

/// <summary>Session-owned decoded pixels. Caller cancellation only cancels that caller's wait;
/// other previews share the pending decode. Desktop can bypass retention to preserve unpacked-file refresh.</summary>
internal sealed class ViewerTextureCache(ITextureSource source, bool enabled, long capacityBytes = 128L * 1024 * 1024) : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _recent = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _loads = new(4, 4);
    private long _bytes, _hits, _misses;
    private bool _disposed;

    internal (long Bytes, long Hits, long Misses, int Entries) Statistics
    { get { lock (_gate) return (_bytes, _hits, _misses, _entries.Count); } }

    internal Task<TextureAsset> LoadTextureAsync(string name, CancellationToken token = default)
    {
        var canonical = source.TryResolveTextureName(name, out var resolved) ? resolved : name;
        return GetAsync("name:" + canonical, cancellation => source.LoadTextureAsync(name, cancellation), token);
    }

    internal Task<TextureAsset> LoadTextureAsync(uint id, CancellationToken token = default) =>
        GetAsync("id:" + id, cancellation => source.LoadTextureAsync(id, cancellation), token);

    internal Task<TextureAsset> LoadTextureAsync(TexturePakEntry entry, CancellationToken token = default) =>
        GetAsync($"entry:{entry.ArchiveName}:{entry.EntryId}:{entry.Record}", cancellation =>
            ((TexturePakArchive)source).LoadTextureAsync(entry, cancellation), token);

    private Task<TextureAsset> GetAsync(string key, Func<CancellationToken, Task<TextureAsset>> load, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!enabled) return load(token);
        Entry entry;
        var start = false;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entries.TryGetValue(key, out entry!))
            {
                _hits++;
                if (entry.Recent is { } recent) { _recent.Remove(recent); _recent.AddFirst(recent); }
            }
            else
            {
                _misses++; entry = new(_lifetime.Token); _entries.Add(key, entry); start = true;
            }
            entry.Waiters++;
        }
        if (start) _ = LoadAsync(key, entry, load);
        return WaitAsync(key, entry, token);
    }

    private async Task<TextureAsset> WaitAsync(string key, Entry entry, CancellationToken token)
    {
        try { return await entry.Completion.Task.WaitAsync(token).ConfigureAwait(false); }
        finally
        {
            lock (_gate)
            {
                entry.Waiters--;
                if (entry.Waiters == 0 && !entry.Completion.Task.IsCompleted)
                {
                    if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry)) _entries.Remove(key);
                    entry.Cancellation.Cancel();
                }
            }
        }
    }

    private async Task LoadAsync(string key, Entry entry, Func<CancellationToken, Task<TextureAsset>> load)
    {
        try
        {
            var token = entry.Cancellation.Token;
            await _loads.WaitAsync(token).ConfigureAwait(false);
            TextureAsset asset;
            try { asset = await load(token).ConfigureAwait(false); token.ThrowIfCancellationRequested(); }
            finally { _loads.Release(); }
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                if (!_disposed && asset.Rgba8.LongLength <= capacityBytes)
                {
                    entry.Bytes = asset.Rgba8.LongLength;
                    entry.Recent = _recent.AddFirst(key); _bytes += entry.Bytes;
                    while (_bytes > capacityBytes && _recent.Last is { } oldest)
                    {
                        var removed = _entries[oldest.Value];
                        _bytes -= removed.Bytes; _entries.Remove(oldest.Value); _recent.RemoveLast();
                    }
                }
                else _entries.Remove(key);
                entry.Completion.TrySetResult(asset);
            }
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry)) _entries.Remove(key);
                entry.Completion.TrySetException(error);
            }
            _ = entry.Completion.Task.Exception; // All waiters may have cancelled already.
        }
        finally { entry.Cancellation.Dispose(); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; _entries.Clear(); _recent.Clear(); _bytes = 0;
        }
        _lifetime.Cancel();
    }

    private sealed class Entry(CancellationToken lifetime)
    {
        internal readonly TaskCompletionSource<TextureAsset> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly CancellationTokenSource Cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        internal LinkedListNode<string>? Recent;
        internal long Bytes;
        internal int Waiters;
    }
}
