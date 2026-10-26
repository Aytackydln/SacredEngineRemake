using AssetViewer.AssetViewer;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Assets.Paks.Texture;

namespace AssetViewer.Tests;

[TestClass]
public sealed class ViewerTextureCacheTests
{
    [TestMethod]
    public async Task AliasesAndConcurrentWaitersShareOneDecodeWithoutSharingCancellation()
    {
        var pending = new TaskCompletionSource<TextureAsset>();
        var source = new Source { Load = _ => pending.Task };
        using var cache = new ViewerTextureCache(source, enabled: true);
        using var cancellation = new CancellationTokenSource();
        var first = cache.LoadTextureAsync("alias", cancellation.Token);
        var second = cache.LoadTextureAsync("CANONICAL.TGA");
        Assert.AreEqual(1, source.Loads);
        cancellation.Cancel();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () => await first);
        var texture = Asset(4);
        pending.SetResult(texture);
        Assert.AreSame(texture, await second);
        Assert.AreSame(texture, await cache.LoadTextureAsync("alias"));
        Assert.AreEqual(1, source.Loads);
        Assert.AreEqual(2L, cache.Statistics.Hits);
    }

    [TestMethod]
    public async Task LeastRecentlyUsedPixelsAreEvictedAndLargeTexturesAreNotRetained()
    {
        var source = new Source { Load = _ => Task.FromResult(Asset(16)) };
        using var cache = new ViewerTextureCache(source, true, capacityBytes: 32);
        var first = await cache.LoadTextureAsync("first");
        await cache.LoadTextureAsync("second");
        Assert.AreSame(first, await cache.LoadTextureAsync("first"));
        await cache.LoadTextureAsync("third");
        Assert.AreEqual(32L, cache.Statistics.Bytes);
        await cache.LoadTextureAsync("second");
        Assert.AreEqual(4, source.Loads);
        using var small = new ViewerTextureCache(source, true, capacityBytes: 1);
        await small.LoadTextureAsync("first"); await small.LoadTextureAsync("first");
        Assert.AreEqual(0L, small.Statistics.Bytes);
        Assert.AreEqual(0, small.Statistics.Entries);
    }

    [TestMethod]
    public async Task FailedLoadsAreRetriedAndEntryIdsCannotCollideWithNames()
    {
        var source = new Source { Load = name => name == "bad" ? Task.FromException<TextureAsset>(new IOException("bad")) : Task.FromResult(Asset(4)) };
        using var cache = new ViewerTextureCache(source, true);
        await Assert.ThrowsExceptionAsync<IOException>(async () => await cache.LoadTextureAsync("bad"));
        source.Load = _ => Task.FromResult(Asset(4));
        await cache.LoadTextureAsync("bad");
        await cache.LoadTextureAsync("1"); await cache.LoadTextureAsync(1);
        Assert.AreEqual(4, source.Loads);
    }

    [TestMethod]
    public async Task AbandonedLoadsReleaseTheirSlotsAndCannotPoisonAReplacementRequest()
    {
        var began = new TaskCompletionSource();
        var source = new Source { LoadWithToken = async token => { began.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return Asset(4); } };
        using var cache = new ViewerTextureCache(source, true);
        using var cancellation = new CancellationTokenSource();
        var abandoned = cache.LoadTextureAsync("first", cancellation.Token);
        await began.Task; cancellation.Cancel();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () => await abandoned);
        source.LoadWithToken = null;
        var replacement = await cache.LoadTextureAsync("first").WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreSame(replacement, await cache.LoadTextureAsync("first"));
        Assert.AreEqual(2, source.Loads);
    }

    [TestMethod]
    public async Task DistinctPayloadReadsHaveBoundedConcurrency()
    {
        var release = new TaskCompletionSource<TextureAsset>();
        var source = new Source { Load = _ => release.Task };
        using var cache = new ViewerTextureCache(source, true);
        var requests = Enumerable.Range(0, 12).Select(index => cache.LoadTextureAsync(index.ToString())).ToArray();
        Assert.AreEqual(4, source.Loads);
        release.SetResult(Asset(4));
        await Task.WhenAll(requests);
        Assert.AreEqual(12, source.Loads);
    }

    [TestMethod]
    public async Task BypassPreservesDesktopFileRefreshAndDisposalCancelsPendingReads()
    {
        var source = new Source { Load = _ => Task.FromResult(Asset(4)) };
        using (var bypass = new ViewerTextureCache(source, false))
        {
            await bypass.LoadTextureAsync("first"); await bypass.LoadTextureAsync("first");
            Assert.AreEqual(2, source.Loads);
            Assert.AreEqual(0L, bypass.Statistics.Bytes);
        }
        var started = new TaskCompletionSource();
        source.LoadWithToken = async token => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); return Asset(4); };
        var cache = new ViewerTextureCache(source, true);
        var loading = cache.LoadTextureAsync("pending");
        await started.Task; cache.Dispose();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () => await loading);
        Assert.AreEqual(0L, cache.Statistics.Bytes);
        Assert.ThrowsException<ObjectDisposedException>(() => cache.LoadTextureAsync("again"));
    }

    private static TextureAsset Asset(int bytes) => new("canonical.tga", 1, bytes / 4, new byte[bytes]);
    private sealed class Source : ITextureSource
    {
        internal Func<string, Task<TextureAsset>> Load = _ => Task.FromResult(Asset(4));
        internal Func<CancellationToken, Task<TextureAsset>>? LoadWithToken;
        internal int Loads;
        public Task<TextureAsset> LoadTextureAsync(string name, CancellationToken token = default)
        { Interlocked.Increment(ref Loads); return LoadWithToken?.Invoke(token) ?? Load(name); }
        public Task<TextureAsset> LoadTextureAsync(uint id, CancellationToken token = default) => LoadTextureAsync(id.ToString(), token);
        public bool TryResolveTextureName(string name, out string resolved)
        { resolved = name.Equals("alias", StringComparison.OrdinalIgnoreCase) ? "canonical.tga" : name; return true; }
        public bool TryGetTextureName(uint id, out string name) { name = id.ToString(); return true; }
        public bool TryResolveTextureRecord(string name, out TexturePakRecord record) { record = default; return false; }
    }
}
