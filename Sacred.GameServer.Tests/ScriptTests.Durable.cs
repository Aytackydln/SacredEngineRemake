using Sacred.GameServer.Scripting;

namespace Sacred.GameServer.Tests;

/// <summary>server.flush(fn): durable writes of a mod's storage (Scripting/ScriptHost.Durable.cs, ModStore.Durable.cs).</summary>
public partial class ScriptTests
{
    [TestMethod]
    public void FlushWritesTheStoreBeforeItsCallbackRuns()
    {
        using var folder = new Folder();
        folder.Write("a.lua", """
            server.save("v", 1)
            server.on("chat", function(p, text)
                server.save("v", tonumber(text))
                server.flush(function(ok, err) server.save("answered", text); server.save("ok", ok) end)
                return true
            end)
            """);
        var (host, server, store) = Host(folder);
        var file = Path.Combine(folder.Data, "a.json");
        host.FireChat(server.All[0], "7");
        Assert.AreEqual(1, host.DurableWritesPending);
        Assert.IsFalse(store.TryGet("answered", out _), "the callback waits for the write");
        host.WaitDurable();
        Assert.AreEqual(0, host.DurableWritesPending);
        Assert.IsTrue(store.TryGet("ok", out var ok) && ok is true);
        Assert.AreEqual("7", store.TryGet("answered", out var a) ? a : null);
        Assert.IsTrue(File.Exists(file) && !File.Exists(file + ".tmp"));
        Assert.IsTrue(new ModStore(file).TryGet("v", out var v) && v is 7.0, "the value was on disk when the callback ran");
    }

    [TestMethod]
    public void FailedFlushKeepsTheOldFileAndTheRegularFlushWaitsForTheScript()
    {
        using var folder = new Folder();
        folder.Write("a.lua", """
            server.save("v", 1)
            server.on("chat", function(p, text)
                local before = server.load("v")
                server.save("v", tonumber(text))
                server.flush(function(ok, err)
                    if not ok then server.save("v", before); server.save("error", err) end
                end)
                return true
            end)
            """);
        var (host, server, store) = Host(folder);
        var file = Path.Combine(folder.Data, "a.json");
        host.FlushStores(wait: true);                                       // v = 1 on disk
        using var gate = new ManualResetEventSlim(false);
        store.FailDurableWrites = () => { gate.Wait(5000); return "disk full"; };
        host.FireChat(server.All[0], "9");
        host.FlushStores();                                                 // the regular flush must not write v = 9 meanwhile
        Thread.Sleep(50);
        host.FlushStores();
        Assert.IsTrue(new ModStore(file).TryGet("v", out var onDisk) && onDisk is 1.0, "nothing but the durable write touches the file");
        gate.Set();
        host.WaitDurable();
        Assert.AreEqual("disk full", store.TryGet("error", out var e) ? e : null);
        Assert.IsTrue(store.TryGet("v", out var v) && v is 1.0, "the script undid its change");
        store.FailDurableWrites = null;
        host.FlushStores(wait: true);                                       // the undo is written by the next flush
        var reloaded = new ModStore(file);
        Assert.IsTrue(reloaded.TryGet("v", out var after) && after is 1.0);
        Assert.IsTrue(reloaded.TryGet("error", out _));
    }

    [TestMethod]
    public void FlushCallbacksRunInOrderOnThePump()
    {
        using var folder = new Folder();
        folder.Write("a.lua", """
            local order = {}
            server.on("chat", function(p, text)
                server.save("last", text)
                server.flush(function(ok) order[#order + 1] = text; server.save("order", table.concat(order, ",")) end)
                return true
            end)
            """);
        var (host, server, store) = Host(folder);
        for (var i = 1; i <= 5; i++) host.FireChat(server.All[0], i.ToString());
        Assert.AreEqual(5, host.DurableWritesPending);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (host.DurableWritesPending > 0 && DateTime.UtcNow < deadline) { host.Pump(server.Now += 10); Thread.Sleep(5); }
        Assert.AreEqual("1,2,3,4,5", store.TryGet("order", out var order) ? order : null);
        Assert.IsTrue(new ModStore(Path.Combine(folder.Data, "a.json")).TryGet("last", out var last) && last is "5");
    }

    [TestMethod]
    public void FlushWithoutCallbackAndAfterAReload()
    {
        using var folder = new Folder();
        folder.Write("a.lua", """
            server.on("chat", function(p, text) server.save("t", text); server.flush(function() server.save("called", true) end); return true end)
            server.on("start", function() server.save("loaded", (server.load("loaded") or 0) + 1); server.flush() end)
            """);
        var (host, server, store) = Host(folder);
        host.FireChat(server.All[0], "x");
        host.Load();                                                        // a reload first drains the writes in flight: the old script's callback is answered, then it is replaced
        host.WaitDurable();
        Assert.IsTrue(store.TryGet("called", out var called) && called is true);
        Assert.IsFalse(store.DurablePending);
        var reloaded = new ModStore(Path.Combine(folder.Data, "a.json"));
        Assert.IsTrue(reloaded.TryGet("t", out var t) && t is "x");
        Assert.IsTrue(reloaded.TryGet("loaded", out var n) && n is 2.0);
    }
}
