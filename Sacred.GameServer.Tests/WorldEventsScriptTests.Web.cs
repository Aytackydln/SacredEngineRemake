using System.Text.Json;
using Sacred.GameServer.Scripting;

namespace Sacred.GameServer.Tests;

/// <summary>world_events.lua: events added, changed, turned off and started at runtime through the mod's storage (what the web UI's
/// Events page does), and the mod surviving a hot reload.</summary>
public partial class WorldEventsScriptTests
{
    private static void Put(ScriptHost host, string json)
    {
        var answer = host.StoreRequest("world_events", null, json);
        Assert.AreEqual(200, answer.Status, answer.Body);
    }

    private static JsonElement Events(ScriptHost host)
    {
        var answer = host.HandleRequest("world_events", "events", [], false);
        Assert.AreEqual(200, answer.Status, answer.Body);
        return JsonDocument.Parse(answer.Body).RootElement;
    }

    private static JsonElement Find(JsonElement events, string id) => events.GetProperty("events").EnumerateArray().Single(e => e.GetProperty("id").GetString() == id);

    private const string Goblins = """{"mod":"world_events","set":{"events":{"goblins":{"title":"Goblin hunt","target":2,"duration":600,"names":["goblin"],"min_contribution":1,"created":1,"reward":{"gold":100,"gold_each":10}}}}}""";

    [TestMethod]
    public void AWebEventJoinsTheRotationWithoutAReloadAndCanBeStartedAtOnce()
    {
        using var folder = new Folder();
        folder.Write("world_events.lua", Source());
        var (host, server) = Start(folder);
        Put(host, Goblins);
        server.Tick(host);
        var entry = Find(Events(host), "goblins");
        Assert.AreEqual("web", entry.GetProperty("source").GetString());
        Assert.IsTrue(entry.GetProperty("enabled").GetBoolean());
        Assert.IsTrue(State(host).GetProperty("events").EnumerateArray().Any(e => e.GetProperty("id").GetString() == "goblins"));
        // the first tick already started the first event of the rotation; end it, then start the goblins by command
        Put(host, """{"mod":"world_events","set":{"control":{"cmd":"cancel"}}}""");
        server.Tick(host);
        Put(host, """{"mod":"world_events","set":{"control":{"cmd":"start","event":"goblins"}}}""");
        server.Tick(host);
        StringAssert.Contains(server.Said.Last(), "Goblin hunt begins");
        Assert.AreEqual("goblins", State(host).GetProperty("active").GetProperty("id").GetString());
        host.FireKill(server.Alice, new ScriptMonster(1, 5, 3, "Goblin Archer"));
        host.FireKill(server.Alice, Orc(2));
        host.FireKill(server.Alice, new ScriptMonster(3, 5, 3, "Goblin"));
        StringAssert.Contains(server.Said.Last(), "Goblin hunt is complete! 2/2");
        CollectionAssert.AreEqual(new[] { (1, 110u) }, server.Gold);
    }

    [TestMethod]
    public void ATurnedOffEventLeavesTheRotationAndARefusedOneSaysWhy()
    {
        using var folder = new Folder();
        folder.Write("world_events.lua", Source());
        var (host, server) = Start(folder);
        Put(host, """{"mod":"world_events","set":{"events_off":{"orc-hunt":true}}}""");
        server.Tick(host);
        Assert.IsFalse(Find(Events(host), "orc-hunt").GetProperty("enabled").GetBoolean(), "the seeded defaults are stored events like any other");
        Assert.IsFalse(State(host).GetProperty("events").EnumerateArray().Any(e => e.GetProperty("id").GetString() == "orc-hunt"));
        Put(host, """{"mod":"world_events","set":{"events":{"bad id":{"target":5,"duration":600},"ok":{"target":0,"duration":600}}}}""");
        server.Tick(host);
        var events = Events(host);
        StringAssert.Contains(Find(events, "bad id").GetProperty("invalid").GetString(), "id must be");
        StringAssert.Contains(Find(events, "ok").GetProperty("invalid").GetString(), "target");
        StringAssert.DoesNotMatch(server.Said.FirstOrDefault() ?? "", new System.Text.RegularExpressions.Regex("Orc hunt begins"));
    }

    [TestMethod]
    public void ARunningEventKeepsItsDefinitionWhenTheWebChangesIt()
    {
        using var folder = new Folder();
        folder.Write("world_events.lua", Source());
        var (host, server) = Start(folder);
        server.Tick(host);
        Assert.AreEqual("orc-hunt", State(host).GetProperty("active").GetProperty("id").GetString());
        Put(host, """{"mod":"world_events","set":{"events":{"orc-hunt":{"title":"Changed","target":99,"duration":600}}}}""");
        server.Tick(host);
        Assert.AreEqual("web", Find(Events(host), "orc-hunt").GetProperty("source").GetString(), "the file only seeded the storage");
        Assert.AreEqual(4, State(host).GetProperty("active").GetProperty("target").GetInt32());
        Assert.AreEqual("Orc hunt", State(host).GetProperty("active").GetProperty("title").GetString());
    }

    [TestMethod]
    public void CancellingEndsTheEventWithoutRewards()
    {
        using var folder = new Folder();
        folder.Write("world_events.lua", Source());
        var (host, server) = Start(folder);
        server.Tick(host);
        host.FireKill(server.Alice, Orc(1));
        Put(host, """{"mod":"world_events","set":{"control":{"cmd":"cancel"}}}""");
        server.Tick(host);
        StringAssert.Contains(server.Said.Last(), "cancelled");
        Assert.AreEqual(0, server.Gold.Count + server.Items.Count);
        Assert.IsFalse(State(host).TryGetProperty("active", out _));
        Assert.IsTrue(State(host).GetProperty("last").GetProperty("cancelled").GetBoolean());
        Assert.IsTrue(Events(host).GetProperty("control").GetProperty("ok").GetBoolean());
    }

    [TestMethod]
    public void AReloadKeepsTheRunningEventAndTheCommandsRunOnce()
    {
        using var folder = new Folder();
        folder.Write("world_events.lua", Source(t => t.Replace("cooldown = 5,", "cooldown = 0,")));
        var (host, server) = Start(folder);
        server.Tick(host);
        host.FireKill(server.Alice, Orc(1));
        host.FireKill(server.Bob, Orc(2));
        Put(host, """{"mod":"world_events","set":{"events":{"goblins":{"title":"Goblin hunt","target":2,"duration":600,"created":1}}}}""");
        server.Tick(host);
        var said = server.Said.Count;
        Assert.IsTrue(host.Reload("world_events"));
        Assert.IsNull(host.Mods.Single().Error);
        var active = State(host).GetProperty("active");
        Assert.AreEqual(2, active.GetProperty("count").GetInt32(), "the contributions survived the reload");
        Assert.AreEqual(2, active.GetProperty("participants").GetInt32());
        Assert.AreEqual(said, server.Said.Count, "nothing is announced again");
        Assert.IsTrue(Events(host).GetProperty("events").EnumerateArray().Any(e => e.GetProperty("id").GetString() == "goblins"), "the web event is still there");
        // a command is run once: cancel, reload, the old command does not cancel the next event
        Put(host, """{"mod":"world_events","set":{"control":{"cmd":"cancel"}}}""");
        server.Tick(host);
        host.Reload("world_events");
        server.Now += 10_000; server.Tick(host);
        Assert.IsTrue(State(host).TryGetProperty("active", out var next), "the next event started after the cooldown");
        host.Reload("world_events");
        server.Tick(host);
        Assert.IsTrue(State(host).TryGetProperty("active", out _), "the old cancel command did not run again");
        Assert.AreEqual(0, host.Errors);
    }
}
