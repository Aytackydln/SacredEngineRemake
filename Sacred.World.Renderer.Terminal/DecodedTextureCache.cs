using Sacred.Assets.Paks.Texture;

namespace Sacred.World.Renderer.Terminal;

/// <summary>Single-flight decoded texture loads with a byte-limited least-recently-used cache.</summary>
internal sealed class DecodedTextureCache(long capacityBytes) : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Task<TextureAsset>> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LinkedListNode<(string Key, TextureAsset Asset)>> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<(string Key, TextureAsset Asset)> _lru = new();
    private long _bytes;
    private bool _disposed;
    public long Hits { get; private set; }
    public long Misses { get; private set; }

    public Task<TextureAsset> LoadAsync(string key, Func<Task<TextureAsset>> load)
    {
        TaskCompletionSource<TextureAsset> completion;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entries.TryGetValue(key, out var entry))
            {
                Hits++;
                _lru.Remove(entry);
                _lru.AddFirst(entry);
                return Task.FromResult(entry.Value.Asset);
            }
            if (_pending.TryGetValue(key, out var pending))
            {
                Hits++;
                return pending;
            }
            Misses++;
            // Register before starting: archive loads can complete synchronously.
            completion = new TaskCompletionSource<TextureAsset>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Add(key, completion.Task);
        }

        // Starting a load can synchronously read or decode pixels. Keep that work
        // outside the cache lock so unrelated hits and loads can proceed.
        _ = CompleteAsync(key, load, completion);
        return completion.Task;
    }

    private async Task CompleteAsync(string key, Func<Task<TextureAsset>> load, TaskCompletionSource<TextureAsset> completion)
    {
        try
        {
            var asset = await load().ConfigureAwait(false);
            lock (_sync)
            {
                _pending.Remove(key);
                if (!_disposed && asset.Rgba8.LongLength <= capacityBytes)
                {
                    while (_bytes + asset.Rgba8.LongLength > capacityBytes && _lru.Last is { } last)
                    {
                        _bytes -= last.Value.Asset.Rgba8.LongLength;
                        _entries.Remove(last.Value.Key);
                        _lru.RemoveLast();
                    }
                    _entries.Add(key, _lru.AddFirst((key, asset)));
                    _bytes += asset.Rgba8.LongLength;
                }
            }
            completion.SetResult(asset);
        }
        catch (Exception exception)
        {
            lock (_sync) _pending.Remove(key);
            completion.SetException(exception);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _entries.Clear();
            _lru.Clear();
            _bytes = 0;
        }
    }
}
