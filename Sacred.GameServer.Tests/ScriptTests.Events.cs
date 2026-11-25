using Sacred.GameServer.Scripting;

namespace Sacred.GameServer.Tests;

/// <summary>Hooks between mods: server.emit / server.on / server.events (Scripting/ScriptHost.Events.cs).</summary>
public partial class ScriptTests
{
    private const string Publisher = """
        server.events.declare("pub.done", { doc = "when done", args = "n" })
        server.on("join", function(p) server.emit("pub.done", { n = 7 }) end)
        """;
    private const string Subscriber = """
        server.on("pub.done", function(ev) server.broadcast("exact " .. ev.n) end)
        server.on("pub.*", function(name, ev) server.broadcast("wild " .. name .. " " .. ev.n) end)
        """;

    [TestMethod]
    public void EmittedEventsReachExactAndPrefixSubscribersInAnyLoadOrder()
    {
        using var folder = new Folder();
        folder.Write("a.lua", Subscriber);          // the subscriber loads before the publisher
        folder.Write("b.lua", Publisher);
        var (host, server, _) = Host(folder);
        server.Said.Clear();
        host.FirePlayer("join", server.All[0]);
        CollectionAssert.AreEqual(new[] { "exact 7", "wild pub.done 7" }, server.Said);
        Assert.AreEqual(0, host.Errors);
        var listed = host.DeclaredEvents("pub.");
        Assert.AreEqual(1, listed.Count);
        Assert.AreEqual(("pub.done", "b", "when done", "n", 1), listed[0]);
    }

    [TestMethod]
    public void UnloadingAModDropsItsSubscriptionsAndDeclarationsWithoutBreakingTheOthers()
    {
        using var folder = new Folder();
        folder.Write("a.lua", Subscriber);
        folder.Write("b.lua", Publisher);
        var (host, server, _) = Host(folder);
        // the provider goes: its declaration goes, the subscriber stays and just hears nothing
        File.Delete(Path.Combine(folder.Scripts, "b.lua"));
        Assert.IsTrue(host.Reload("b"));
        Assert.AreEqual(0, host.DeclaredEvents().Count);
        server.Said.Clear();
        host.FirePlayer("join", server.All[0]);
        Assert.AreEqual(0, server.Said.Count);
        // the provider returns: the subscriber hears it again
        folder.Write("b.lua", Publisher);
        Assert.IsTrue(host.Reload("b"));
        host.FirePlayer("join", server.All[0]);
        Assert.AreEqual(2, server.Said.Count);
        // the subscriber goes: its handlers go, the publisher emits to nobody
        File.Delete(Path.Combine(folder.Scripts, "a.lua"));
        Assert.IsTrue(host.Reload("a"));
        server.Said.Clear();
        host.FirePlayer("join", server.All[0]);
        Assert.AreEqual(0, server.Said.Count);
        Assert.AreEqual(1, host.DeclaredEvents().Count);
        Assert.AreEqual(0, host.DeclaredEvents()[0].Listeners);
        Assert.AreEqual(0, host.Errors);
        host.Load();                                   // a full reload starts clean
        Assert.AreEqual(1, host.DeclaredEvents().Count);
    }

    [TestMethod]
    public void AFailingSubscriberDoesNotStopThePublisherOrTheOtherSubscribers()
    {
        using var folder = new Folder();
        folder.Write("a.lua", "server.on('pub.done', function() error('boom') end)");
        folder.Write("b.lua", Subscriber + "\nserver.on('join', function() server.emit('pub.done', { n = 1 }) server.broadcast('after') end)");
        var (host, server, _) = Host(folder);
        server.Said.Clear();
        host.FirePlayer("join", server.All[0]);
        CollectionAssert.AreEqual(new[] { "exact 1", "wild pub.done 1", "after" }, server.Said);
    }

    [TestMethod]
    public void EventNamesNeedADotAndDeclarationsBelongToOneMod()
    {
        using var folder = new Folder();
        folder.Write("a.lua", """
            local ok1 = pcall(server.emit, "join", {})
            local ok2 = pcall(server.emit, "pub.*", {})
            server.broadcast("emit " .. tostring(ok1) .. " " .. tostring(ok2))
            server.events.declare("x.y", "text")
            """);
        folder.Write("b.lua", "server.broadcast('clash ' .. tostring(pcall(server.events.declare, 'x.y', 'mine')))");
        var (host, server, _) = Host(folder);
        CollectionAssert.AreEqual(new[] { "emit false false", "clash false" }, server.Said.Where(s => s.StartsWith("emit") || s.StartsWith("clash")).ToArray());
    }

    [TestMethod]
    public void AnEmitLoopStillRunsOutOfBudgetAndNestingIsLimited()
    {
        using var folder = new Folder();
        folder.Write("a.lua", "server.on('start', function() while true do server.emit('loop.x', {}) end end) server.on('loop.x', function() end)");
        var (host, _, _) = Host(folder, budget: 100_000);
        Assert.IsTrue(host.Errors > 0, "the endless emitter is stopped by the budget");
        using var folder2 = new Folder();
        folder2.Write("a.lua", "server.on('deep.x', function() server.emit('deep.x', {}) end) server.on('start', function() server.emit('deep.x', {}) end)");
        var (host2, _, _) = Host(folder2);
        Assert.IsTrue(host2.Errors > 0, "unbounded nesting is an error, not a stack overflow");
    }
}
