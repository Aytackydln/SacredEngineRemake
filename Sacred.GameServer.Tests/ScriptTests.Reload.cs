using Sacred.GameServer.Scripting;

namespace Sacred.GameServer.Tests;

/// <summary>Hot reload: server.on("unload"), per-mod and full reload (Scripting/ScriptHost.Reload.cs).</summary>
public partial class ScriptTests
{
    private const string Counter = """
        local n = server.load("n", 0)
        server.on("unload", function(reason) server.save("n", n + 1); server.save("reason", reason); server.broadcast("unload a " .. reason) end)
        server.on("start", function() server.broadcast("start a " .. n) end)
        server.on("join", function(p) server.broadcast("join a") end)
        server.every(1, function() server.broadcast("tick a") end)
        server.endpoint("hello", function() return "a" end)
        """;

    [TestMethod]
    public void FullReloadRunsUnloadHooksAndTheNewScriptSeesWhatTheOldOneSaved()
    {
        using var folder = new Folder();
        folder.Write("a.lua", Counter);
        folder.Write("b.lua", "server.on('unload', function(r) server.broadcast('unload b ' .. r) end)");
        var (host, server, store) = Host(folder);
        server.Said.Clear();
        host.Load();
        CollectionAssert.AreEqual(new[] { "unload b reload", "unload a reload", "start a 1" }, server.Said, "the last loaded unloads first; then every start");
        Assert.AreEqual("reload", store.TryGet("reason", out var reason) ? reason : null);
        Assert.AreEqual(0, host.Errors);
    }

    [TestMethod]
    public void ReloadingOneModLeavesTheOthersAlone()
    {
        using var folder = new Folder();
        folder.Write("a.lua", Counter);
        folder.Write("b.lua", "server.on('unload', function(r) server.broadcast('unload b') end) server.on('start', function() server.broadcast('start b') end) server.on('join', function() server.broadcast('join b') end)");
        var (host, server, _) = Host(folder);
        server.Said.Clear();
        folder.Write("a.lua", Counter.Replace("join a", "join a2"));
        Assert.IsTrue(host.Reload("a"));
        CollectionAssert.AreEqual(new[] { "unload a reload", "start a 1" }, server.Said, "only a unloads and starts");
        server.Said.Clear();
        host.FirePlayer("join", server.All[0]);
        Assert.AreEqual("join a2,join b", string.Join(",", server.Said.Order()), "the old handler is gone, b stays (a now runs after b)");
        server.Said.Clear();
        server.Now += 1500; host.Pump(server.Now);
        CollectionAssert.AreEqual(new[] { "tick a" }, server.Said, "one timer, not two");
        Assert.AreEqual(1, host.Endpoints.Count(e => e.Mod == "a"), "the endpoint is registered once");
        Assert.AreEqual("a", host.HandleRequest("a", "hello", [], false).Body);
        Assert.IsFalse(host.Reload("nope"));
    }

    [TestMethod]
    public void AChangedFileReloadsOnlyItsMod()
    {
        using var folder = new Folder();
        folder.Write("a.lua", Counter);
        folder.Write("b.lua", "server.on('unload', function(r) server.broadcast('unload b') end)");
        var (host, server, _) = Host(folder);
        server.Said.Clear();
        host.RequestReload("a");
        host.Pump(0); host.Pump(300);
        Assert.AreEqual(0, server.Said.Count, "half a second of quiet first");
        host.Pump(600);
        CollectionAssert.AreEqual(new[] { "unload a reload", "start a 1" }, server.Said);
    }

    [TestMethod]
    public void ANewFileLoadsAndARemovedFileUnloads()
    {
        using var folder = new Folder();
        folder.Write("a.lua", Counter);
        var (host, server, _) = Host(folder);
        server.Said.Clear();
        folder.Write("c.lua", "server.on('start', function() server.broadcast('start c') end) server.endpoint('x', function() return 1 end)");
        Assert.IsTrue(host.Reload("c"));
        CollectionAssert.AreEqual(new[] { "start c" }, server.Said);
        CollectionAssert.AreEqual(new[] { "a", "c" }, host.Mods.Select(m => m.Mod).ToArray());
        server.Said.Clear();
        File.Delete(Path.Combine(folder.Scripts, "a.lua"));
        Assert.IsTrue(host.Reload("a"));
        CollectionAssert.AreEqual(new[] { "unload a reload" }, server.Said);
        CollectionAssert.AreEqual(new[] { "c" }, host.Mods.Select(m => m.Mod).ToArray());
        Assert.AreEqual(404, host.HandleRequest("a", "hello", [], false).Status);
        server.Said.Clear();
        host.FirePlayer("join", server.All[0]);
        host.Pump(server.Now + 5000);
        Assert.AreEqual(0, server.Said.Count, "nothing of a is left");
    }

    [TestMethod]
    public void ABrokenReloadShowsTheErrorAndAFixLoadsItAgain()
    {
        using var folder = new Folder();
        folder.Write("a.lua", Counter);
        var (host, server, _) = Host(folder);
        folder.Write("a.lua", "server.on('join', function() end) error('boom')");
        Assert.IsTrue(host.Reload("a"));
        StringAssert.Contains(host.Mods.Single().Error ?? "", "boom");
        folder.Write("a.lua", Counter);
        Assert.IsTrue(host.Reload("a"));
        Assert.IsNull(host.Mods.Single().Error);
    }

    [TestMethod]
    public void ShutdownRunsUnloadHooksAndAnswersTheirFlush()
    {
        using var folder = new Folder();
        folder.Write("a.lua", """
            server.on("unload", function(reason)
                server.save("bye", reason)
                server.flush(function(ok) server.broadcast("flushed " .. tostring(ok)) end)
            end)
            """);
        var (host, server, store) = Host(folder);
        host.Shutdown();
        CollectionAssert.AreEqual(new[] { "flushed True" }, server.Said.Select(s => s.Replace("true", "True")).ToArray());
        Assert.AreEqual("shutdown", store.TryGet("bye", out var bye) ? bye : null);
        Assert.IsTrue(new ModStore(Path.Combine(folder.Data, "a.json")).TryGet("bye", out _), "it reached the disk before the callback");
        server.Said.Clear();
        Assert.IsFalse(host.FirePlayer("join", server.All[0]));
        host.Shutdown();                                                    // twice is fine
        Assert.AreEqual(0, server.Said.Count);
    }

    [TestMethod]
    public void AFailingUnloadHandlerDoesNotStopTheReload()
    {
        using var folder = new Folder();
        folder.Write("a.lua", "server.on('unload', function() error('nope') end) server.on('start', function() server.broadcast('a up') end)");
        var (host, server, _) = Host(folder);
        server.Said.Clear();
        host.Load();
        CollectionAssert.AreEqual(new[] { "a up" }, server.Said);
        Assert.AreEqual(1, host.Errors);
    }
}
