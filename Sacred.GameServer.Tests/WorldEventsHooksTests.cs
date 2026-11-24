using System.Text.Json;
using System.Text.RegularExpressions;
using Sacred.GameServer.Scripting;

namespace Sacred.GameServer.Tests;

/// <summary>world_events.lua hooks: triggers (percent, time, start / success / fail, kills of a creature), the targets, the caps, what
/// persists, the validation and the transparency (JSON, page, !event hooks, the pushed state).</summary>
[TestClass]
public class WorldEventsHooksTests
{
    private sealed class Folder : IDisposable
    {
        public readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sacred-hk-" + Guid.NewGuid().ToString("N"));
        public string Scripts => System.IO.Path.Combine(Path, "scripts");
        public string Data => System.IO.Path.Combine(Path, "data");
        public Folder() { Directory.CreateDirectory(Scripts); }
        public void Write(string name, string source) => File.WriteAllText(System.IO.Path.Combine(Scripts, name), source);
        public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
    }

    private sealed class Fake : IScriptServer
    {
        public long Now;
        public readonly List<string> Said = [];
        public readonly List<(int Slot, string Text)> Told = [];
        public readonly List<(int Slot, uint? Quality)> Items = [];
        public readonly List<(int Slot, uint Amount)> Gold = [];
        public readonly List<(int Slot, string Channel, JsonElement Payload)> Sent = [];
        public readonly Dictionary<string, uint> Channels = [];
        public readonly HashSet<int> NetPlayers = [1, 2, 3];
        public readonly Dictionary<int, int> Owners = [];
        public readonly List<ScriptPlayer> All =
        [
            new ScriptPlayer(1, "Alice", "name:Alice", 2, 10, true, true, 0, Sector: 2909),
            new ScriptPlayer(2, "Bob", "name:Bob", 1, 12, true, false, 0, Sector: 2909),
            new ScriptPlayer(3, "Cara", "name:Cara", 1, 12, true, false, 0, Sector: 5000),
        ];
        public long NowMs => Now;
        public IReadOnlyList<ScriptPlayer> Players() => All;
        public ScriptPlayer? Player(int slot) => All.FirstOrDefault(p => p.Slot == slot);
        public void Broadcast(string text) => Said.Add(text);
        public void Message(int slot, string text) => Told.Add((slot, text));
        public void Kick(int slot, string reason) { }
        public ScriptDrop? DropItem(int slot, in ScriptLootRequest request, out string? error) { Items.Add((slot, request.Quality)); error = null; return new ScriptDrop(1, 2, request.Quality ?? 0, "Test ring", "Ring"); }
        public ScriptDrop? DropGold(int slot, uint amount, out string? error) { Gold.Add((slot, amount)); error = null; return new ScriptDrop(3, GoldPile.ResourceFor(amount), 0, "Gold", "Gold"); }
        public uint NetRegister(string name, bool serverOnly) { if (!Channels.TryGetValue(name, out var id)) Channels[name] = id = (uint)(0x2000 + Channels.Count); return id; }
        public bool NetSupports(int slot) => NetPlayers.Contains(slot);
        public int NetSend(int slot, uint channel, bool json, byte[] data, out string? error)
        {
            error = null;
            Sent.Add((slot, Channels.First(c => c.Value == channel).Key, JsonDocument.Parse(data).RootElement.Clone()));
            return 1;
        }
        public int SectorOwner(int sector) => Owners.GetValueOrDefault(sector);
        public readonly Dictionary<int, int> RegionOf = [];
        public int SectorRegion(int sector) => RegionOf.GetValueOrDefault(sector);
        public IReadOnlyList<ScriptRegion> Regions() => [new(1, "UI_REGION_NORTH", "Northkern", RegionOf.Count(kv => kv.Value == 1), 500, false), new(2, "UI_REGION_SOUTH", "Southkern", RegionOf.Count(kv => kv.Value == 2), 300, true)];
        public IReadOnlyList<uint> RegionSectors(int region) => RegionOf.Where(kv => kv.Value == region).Select(kv => (uint)kv.Key).Order().ToArray();
        public ScriptPlayer Alice => All[0];
        public ScriptPlayer Bob => All[1];
        public ScriptPlayer Cara => All[2];
        public IEnumerable<(int Slot, JsonElement Payload)> Spawns => Sent.Where(s => s.Channel == "worldevents:spawn").Select(s => (s.Slot, s.Payload));
        public void Tick(ScriptHost host) { Now += 2000; host.Pump(Now); }
    }

    private static readonly Regex EventsBlock = new(@"local EVENTS = \{.*?\r?\n\}\r?\n", RegexOptions.Singleline);

    private static string Script(string events, Func<string, string>? more = null)
    {
        string? text = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null && text is null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Sacred.GameServer", "scripts-examples", "world_events.lua");
            if (File.Exists(candidate)) text = File.ReadAllText(candidate);
        }
        text = EventsBlock.Replace(text ?? throw new FileNotFoundException("world_events.lua not found above " + AppContext.BaseDirectory), m => "local EVENTS = {\n" + events + "\n}\n");
        text = text.Replace("first_delay = 60,", "first_delay = 0,").Replace("cooldown = 900,", "cooldown = 5,").Replace("announce_hooks = true,", "announce_hooks = false,");
        return more is null ? text : more(text);
    }

    private static (ScriptHost Host, Fake Server) Start(Folder folder, string script, Fake? server = null)
    {
        folder.Write("world_events.lua", script);
        server ??= new Fake();
        var host = new ScriptHost(new ScriptOptions { Directory = folder.Scripts, DataDirectory = folder.Data }, server);
        host.Load();
        Assert.IsNull(host.Files.Single().Error);
        return (host, server);
    }

    private static string Event(string hooks, int target = 100, string extra = "", int duration = 3600) =>
        $$"""{ id = "wave", title = "Wave event", goal = "kill", target = {{target}}, duration = {{duration}}, min_contribution = 1, {{extra}} hooks = {{hooks}} },""";

    private static ScriptMonster Orc(uint reference, string name = "Orc Warrior", uint type = 38) => new(reference, type, 5, name, 2909, 1, 1);

    private static void Kills(ScriptHost host, ScriptPlayer by, int count, uint start = 1000, string name = "Orc Warrior")
    {
        for (var i = 0; i < count; i++) host.FireKill(by, Orc(start + (uint)i, name));
    }

    private static JsonElement State(ScriptHost host)
    {
        var answer = host.HandleRequest("world_events", "state", [], publicOnly: true);
        Assert.AreEqual(200, answer.Status);
        return JsonDocument.Parse(answer.Body).RootElement;
    }

    // ---- triggers -----------------------------------------------------------------------------------------------------------------

    [TestMethod]
    public void TheEventPublishesWorldEventHooksForOtherMods()
    {
        using var folder = new Folder();
        folder.Write("listener.lua", "server.on('worldevent.*', function(name, ev) server.broadcast('EV ' .. name .. ' ' .. ev.count .. '/' .. ev.target .. ' ' .. ev.source .. ' ' .. (ev.action or '-')) end)");
        folder.Write("world_events.lua", Script(Event("""{ { at = 50, actions = { { action = "message", text = "half", target = "global" } } } }""", target: 3)));
        var server = new Fake();
        var host = new ScriptHost(new ScriptOptions { Directory = folder.Scripts, DataDirectory = folder.Data }, server);
        host.Load();
        server.Tick(host);
        Kills(host, server.Alice, 3);
        CollectionAssert.AreEqual(new[]
        {
            "EV worldevent.started 0/3 server -", "EV worldevent.progress 1/3 server -", "EV worldevent.progress 2/3 server -",
            "EV worldevent.hook 2/3 server message", "EV worldevent.progress 3/3 server -", "EV worldevent.completed 3/3 server -",
        }, server.Said.Where(x => x.StartsWith("EV ")).ToArray());
        Assert.AreEqual(7, host.DeclaredEvents("worldevent.").Count);
        Assert.AreEqual(0, host.Errors);
    }

    [TestMethod]
    public void APercentHookFiresOnceForTheHeroWhoMadeTheKill()
    {
        using var folder = new Folder();
        var (host, server) = Start(folder, Script(Event("""{ { at = 30, actions = { { action = "spawn", n = 3, target = "killer", kinds = { { type = "Orc Warrior", weight = 3 }, { type = "Orc Shaman" } }, radius = 600 } } } }""")));
        server.Tick(host);
        Kills(host, server.Alice, 29);
        Assert.AreEqual(0, server.Spawns.Count());
        host.FireKill(server.Bob, Orc(5000));                                     // the 30th kill: 30 %
        var wave = server.Spawns.Single();
        Assert.AreEqual(2, wave.Slot, "the killer's own game");
        Assert.AreEqual(3, wave.Payload.GetProperty("n").GetInt32());
        Assert.AreEqual(600, wave.Payload.GetProperty("radius").GetInt32());
        Assert.AreEqual("Orc Warrior", wave.Payload.GetProperty("kinds")[0].GetProperty("type").GetString());
        Assert.AreEqual(3, wave.Payload.GetProperty("kinds")[0].GetProperty("weight").GetInt32());
        StringAssert.StartsWith(wave.Payload.GetProperty("id").GetString(), "1:h1:1:");
        Kills(host, server.Alice, 20, 6000);
        Assert.AreEqual(1, server.Spawns.Count(), "a hook fires once");
        Assert.AreEqual(0, host.Errors);
    }

    [TestMethod]
    public void ProgressThatPassesSeveralHooksFiresThemAllInOrderAndNotAgainAfterARestart()
    {
        using var folder = new Folder();
        var script = Script(Event("""
            { { at = 10, actions = { { action = "message", text = "ten", target = "global" } } },
              { at = 20, actions = { { action = "message", text = "twenty", target = "global" } } },
              { at = 90, actions = { { action = "message", text = "ninety", target = "global" } } },
              { at = 30, actions = { { action = "message", text = "thirty", target = "global" } } } }
            """, target: 4));
        var (host, server) = Start(folder, script);
        server.Tick(host);
        Kills(host, server.Alice, 1);                                              // 25 %: ten and twenty at once
        CollectionAssert.AreEqual(new[] { "[World event] ten", "[World event] twenty" }, server.Said.Where(s => s.EndsWith("ten") || s.EndsWith("twenty")).ToArray());
        server.Tick(host);                                                         // saves the progress and what has fired
        host.FlushStores(wait: true);
        host.Dispose();

        var (again, server2) = Start(folder, script);                              // a restart in the middle of the event
        server2.Tick(again);
        Kills(again, server2.Alice, 1, 2000);                                      // 50 %: thirty, never ten or twenty again
        Assert.IsTrue(server2.Said.Any(s => s.EndsWith("thirty")));
        Assert.IsFalse(server2.Said.Any(s => s.EndsWith("ten") || s.EndsWith("twenty")), "no refire after the restart");
        Assert.AreEqual(0, again.Errors);
    }

    [TestMethod]
    public void ATimeHookFiresAfterTheEventHasRunThatLong()
    {
        using var folder = new Folder();
        var (host, server) = Start(folder, Script(Event("""{ { at_time = 1, actions = { { action = "message", text = "late", target = "global" } } } }""")));
        server.Tick(host);
        Assert.IsFalse(server.Said.Any(s => s.EndsWith("late")));
        Thread.Sleep(1200);
        server.Tick(host);
        Assert.AreEqual(1, server.Said.Count(s => s.EndsWith("late")));
        server.Tick(host);
        Assert.AreEqual(1, server.Said.Count(s => s.EndsWith("late")), "once");
    }

    [TestMethod]
    public void StartSuccessAndFailHooksRunAtThoseMoments()
    {
        using var folder = new Folder();
        var events = Event("""
            { { on = "start", actions = { { action = "message", text = "go", target = "global" } } },
              { on = "success", actions = { { action = "message", text = "won", target = "global" } } },
              { on = "fail", actions = { { action = "message", text = "lost", target = "global" } } } }
            """, target: 2);
        using (var folder1 = new Folder())
        {
            var (host, server) = Start(folder1, Script(events));
            server.Tick(host);
            Assert.IsTrue(server.Said.Any(s => s.EndsWith("go")));
            Kills(host, server.Alice, 2);
            Assert.IsTrue(server.Said.Any(s => s.EndsWith("won")));
            Assert.IsFalse(server.Said.Any(s => s.EndsWith("lost")));
        }
        var (host2, server2) = Start(folder, Script(events.Replace("duration = 3600", "duration = 1")));
        server2.Tick(host2);
        Thread.Sleep(1200);
        server2.Tick(host2);
        Assert.IsTrue(server2.Said.Any(s => s.EndsWith("lost")));
        Assert.IsFalse(server2.Said.Any(s => s.EndsWith("won")));
    }

    [TestMethod]
    public void AKillsTriggerCountsACreatureEventWideOrPerHeroAndTheKillerOfTheNthIsTheTarget()
    {
        using var folder = new Folder();
        var hooks = """
            { { kills = { creature = "orc shaman", count = 3 }, actions = { { action = "gold", amount = 500, target = "killer" } } },
              { kills = { creature = 77, count = 2, per_player = true }, actions = { { action = "item", spec = { quality = 9 }, n = 2, target = "killer" } } } }
            """;
        var (host, server) = Start(folder, Script(Event(hooks)));
        server.Tick(host);
        host.FireKill(server.Alice, Orc(1, "Orc Shaman")); host.FireKill(server.Alice, Orc(2, "Orc Warrior")); host.FireKill(server.Bob, Orc(3, "Orc Shaman"));
        Assert.AreEqual(0, server.Gold.Count);
        host.FireKill(server.Bob, Orc(4, "ORC SHAMAN"));                           // the third shaman, any case: Bob made it
        CollectionAssert.AreEqual(new[] { (2, 500u) }, server.Gold);
        host.FireKill(server.Alice, Orc(5, "Orc Shaman"));
        Assert.AreEqual(1, server.Gold.Count, "once");
        // per hero, by type number: Alice 1, Bob 1 -> nothing; Alice's 2nd -> fires for Alice
        host.FireKill(server.Alice, Orc(6, "Wolf", 77)); host.FireKill(server.Bob, Orc(7, "Wolf", 77));
        Assert.AreEqual(0, server.Items.Count);
        host.FireKill(server.Alice, Orc(8, "Wolf", 77));
        CollectionAssert.AreEqual(new (int, uint?)[] { (1, 9), (1, 9) }, server.Items);
        Assert.AreEqual(0, host.Errors);
    }

    // ---- targets, caps, validation ---------------------------------------------------------------------------------------------

    private static string Recipients(Fake server) => string.Join(",", server.Spawns.Select(s => s.Slot).OrderBy(x => x));

    private static string StartWave(string target, Fake server, Folder folder, string extra = "", string action = "n = 1")
    {
        var (host, _) = Start(folder, Script(Event($$"""{ { on = "start", actions = { { action = "spawn", {{action}}, target = "{{target}}", kinds = { { type = "Orc" } } } } } }""",
            extra: "sectors = { 2909 },"), more: null), server);
        server.Tick(host);
        return Recipients(server);
    }

    [TestMethod]
    public void TargetsAreChosenInTheEventsAreaOrAmongEverybodyOnline()
    {
        foreach (var (target, expected) in new[] { ("players", "1,2"), ("global", "1,2,3"), ("host", "1"), ("killer", "") })
        {
            using var folder = new Folder();
            Assert.AreEqual(expected, StartWave(target, new Fake(), folder), target);
        }
        // owner: the hero whose game owns the sector
        using (var folder = new Folder())
        {
            var server = new Fake();
            server.Owners[2909] = 2;
            Assert.AreEqual("2", StartWave("owner", server, folder), "the sector's owner");
        }
        // owner with nobody owning it: the hero with the most kills; alone: the only one
        using (var folder = new Folder())
            Assert.AreEqual("1", StartWave("owner", new Fake(), folder), "a tie goes to the lowest slot");
        using (var folder = new Folder())
        {
            var server = new Fake();
            server.All[1] = server.Bob with { Sector = 1 };                         // Bob left the area: Alice is alone there
            Assert.AreEqual("1", StartWave("owner", server, folder));
        }
        // random: one hero of the area, never Cara
        for (var i = 0; i < 12; i++)
        {
            using var folder = new Folder();
            Assert.IsTrue(StartWave("random", new Fake(), folder) is "1" or "2");
        }
        // nobody in the area: nobody gets a wave, nothing breaks
        using (var folder = new Folder())
        {
            var server = new Fake();
            server.All[0] = server.Alice with { Sector = 9 }; server.All[1] = server.Bob with { Sector = 9 };
            Assert.AreEqual("", StartWave("players", server, folder));
        }
    }

    [TestMethod]
    public void AWaveIsPerHeroOrSharedOutAndCapped()
    {
        using (var folder = new Folder())
        {
            var server = new Fake();
            StartWave("players", server, folder, action: "n = 4, per_player = true");
            CollectionAssert.AreEqual(new[] { 4, 4 }, server.Spawns.Select(s => s.Payload.GetProperty("n").GetInt32()).ToArray());
        }
        using (var folder = new Folder())
        {
            var server = new Fake();
            StartWave("players", server, folder, action: "n = 5, per_player = false");
            CollectionAssert.AreEqual(new[] { 3, 2 }, server.Spawns.Select(s => s.Payload.GetProperty("n").GetInt32()).ToArray(), "shared out");
        }
        using (var folder = new Folder())
        {
            var server = new Fake();
            StartWave("global", server, folder, action: "n = 100000");
            Assert.IsTrue(server.Spawns.All(s => s.Payload.GetProperty("n").GetInt32() == 40), "one action asks for at most max_spawn_n creatures");
        }
        // the per-minute budget: three waves of 8 with a budget of 10 -> 8, 2, 0
        using (var folder = new Folder())
        {
            var server = new Fake();
            var hooks = """
                { { on = "start", actions = { { action = "spawn", n = 8, target = "host", kinds = { { type = "Orc" } } },
                                              { action = "spawn", n = 8, target = "host", kinds = { { type = "Orc" } } },
                                              { action = "spawn", n = 8, target = "host", kinds = { { type = "Orc" } } } } } }
                """;
            var (host, _) = Start(folder, Script(Event(hooks), t => t.Replace("max_spawn_per_minute = 120,", "max_spawn_per_minute = 10,")), server);
            server.Tick(host);
            CollectionAssert.AreEqual(new[] { 8, 2 }, server.Spawns.Select(s => s.Payload.GetProperty("n").GetInt32()).ToArray());
            Assert.AreEqual(0, host.Errors);
        }
        // a game without the mod gets nothing (and the server goes on)
        using (var folder = new Folder())
        {
            var server = new Fake();
            server.NetPlayers.Clear();
            Assert.AreEqual("", StartWave("host", server, folder));
        }
    }

    [TestMethod]
    public void ItemsGoldAndMessagesReachTheirTargets()
    {
        using var folder = new Folder();
        var hooks = """
            { { on = "start", actions = {
                { action = "item", spec = { quality = 12, category = "ring" }, n = 2, target = "host" },
                { action = "item", spec = { type = "Healing Potion" }, target = "host" },
                { action = "gold", amount = 750, target = "players" },
                { action = "message", text = "careful", target = "players" },
                { action = "message", text = "hear ye", target = "global" } } } }
            """;
        var (host, server) = Start(folder, Script(Event(hooks, extra: "sectors = { 2909 },")));
        server.Tick(host);
        CollectionAssert.AreEqual(new (int, uint?)[] { (1, 12), (1, 12) }, server.Items, "the type-name item is for the client mod, not the server");
        CollectionAssert.AreEquivalent(new[] { (1, 750u), (2, 750u) }, server.Gold);
        Assert.IsTrue(server.Told.Any(t => t.Slot == 2 && t.Text.EndsWith("careful")) && !server.Told.Any(t => t.Slot == 3 && t.Text.EndsWith("careful")));
        Assert.IsTrue(server.Said.Any(s => s.EndsWith("hear ye")));
    }

    [TestMethod]
    public void BadHooksAreRefusedWithoutHurtingTheEventAndLimitsAreClamped()
    {
        using var folder = new Folder();
        var hooks = """
            { { actions = { { action = "message", text = "no trigger", target = "global" } } },
              { at = 150, actions = { { action = "message", text = "too far", target = "global" } } },
              { at = 10, actions = { { action = "explode" } } },
              { at = 10, actions = { { action = "spawn", n = 3, target = "owner", kinds = {} } } },
              { at = 10, actions = { { action = "gold", target = "killer" } } },
              { kills = { creature = "", count = 0 }, actions = { { action = "message", text = "x" } } },
              "not a table",
              { at = 10, actions = { { action = "message", text = "fine", target = "nowhere" } } } }
            """;
        var (host, server) = Start(folder, Script(Event(hooks, target: 10)));
        server.Tick(host);
        Kills(host, server.Alice, 1);                                              // 10 %: only the valid hook fires
        Assert.AreEqual(1, server.Said.Count(s => s.EndsWith("fine")), string.Join(" | ", server.Said));
        Assert.IsFalse(server.Said.Any(s => s.EndsWith("no trigger") || s.EndsWith("too far")));
        Assert.AreEqual(0, host.Errors);
        Assert.AreEqual(1, State(host).GetProperty("active").GetProperty("hooks").GetArrayLength(), "only what is valid is shown");
    }

    // ---- transparency ------------------------------------------------------------------------------------------------------------

    [TestMethod]
    public void HooksAreShownOnThePageInTheJsonInChatAndInThePushedState()
    {
        using var folder = new Folder();
        var hooks = """
            { { at = 30, actions = { { action = "spawn", n = 12, target = "owner", kinds = { { type = "Orc Warrior", weight = 3 }, { type = "Orc Shaman" } }, radius = 600 } } },
              { kills = { creature = "Orc Shaman", count = 5 }, actions = { { action = "item", spec = { quality = 9 }, target = "killer" } } },
              { at_time = 1200, actions = { { action = "message", text = "Hurry", target = "global" } } } }
            """;
        var server = new Fake();
        var (host, _) = Start(folder, Script(Event(hooks, extra: "sectors = { 2909 },")), server);
        server.Tick(host);
        var json = State(host).GetProperty("active").GetProperty("hooks");
        Assert.AreEqual(3, json.GetArrayLength());
        StringAssert.Contains(json[0].GetProperty("text").GetString(), "At 30% of the goal: 12 of Orc Warrior x3 / Orc Shaman appear around the owner of the area (or the only hero there) (that many for each)");
        StringAssert.Contains(json[1].GetProperty("text").GetString(), "When the realm has defeated 5 x Orc Shaman");
        StringAssert.Contains(json[2].GetProperty("text").GetString(), "After 20m 00s");
        Assert.IsFalse(json[0].GetProperty("fired").GetBoolean());
        var page = host.HandleRequest("world_events", "", [], publicOnly: true).Body;
        StringAssert.Contains(page, "Hooks: what happens on the way");
        StringAssert.Contains(page, "appear around the owner");
        Assert.IsTrue(host.FireChat(server.Alice, "!event hooks"));
        Assert.AreEqual(3, server.Told.Count(t => t.Slot == 1 && t.Text.Contains("[World event]")));
        // the pushed state names the next percent / time hook compactly
        server.Sent.Clear();
        host.FireSector(server.Alice);
        var state = server.Sent.First(s => s.Channel == "worldevents:state").Payload;
        StringAssert.StartsWith(state.GetProperty("hook").GetString(), "At 30% of the goal: 12 of Orc Warrior");
        // after it fired the next one is shown
        Kills(host, server.Alice, 30);
        server.Sent.Clear();
        host.FireSector(server.Alice);
        StringAssert.StartsWith(server.Sent.First(s => s.Channel == "worldevents:state").Payload.GetProperty("hook").GetString(), "After 20m 00s");
        Assert.IsTrue(State(host).GetProperty("active").GetProperty("hooks")[0].GetProperty("fired").GetBoolean());
    }

    [TestMethod]
    public void WebEventsKeepTheirHooksCleanedAndShowThemInTheEditorsData()
    {
        using var folder = new Folder();
        var (host, server) = Start(folder, Script(Event("nil")));
        var body = """{"mod":"world_events","set":{"events":{"hunt":{"title":"Hunt","target":50,"duration":3600,"hooks":[{"at":25,"actions":[{"action":"spawn","n":99999,"target":"players","kinds":[{"type":"Wolf","weight":2},{"type":""},{"type":7}],"radius":10}]},{"on":"nonsense","actions":[]}]}}}}""";
        Assert.AreEqual(200, host.StoreRequest("world_events", null, body).Status);
        for (var i = 0; i < 6; i++) { server.Now += 2000; host.Pump(server.Now); }
        var events = JsonDocument.Parse(host.HandleRequest("world_events", "events", [], publicOnly: false).Body).RootElement.GetProperty("events");
        var hunt = events.EnumerateArray().First(e => e.GetProperty("id").GetString() == "hunt");
        var hooks = hunt.GetProperty("def").GetProperty("hooks");
        Assert.AreEqual(1, hooks.GetArrayLength(), "the second hook has no valid trigger");
        var spawn = hooks[0].GetProperty("actions")[0];
        Assert.AreEqual(40, spawn.GetProperty("n").GetInt32(), "clamped to max_spawn_n");
        Assert.AreEqual(50, spawn.GetProperty("radius").GetInt32(), "clamped");
        Assert.AreEqual(2, spawn.GetProperty("kinds").GetArrayLength(), "the nameless kind is dropped");
        Assert.AreEqual(7, spawn.GetProperty("kinds")[1].GetProperty("type").GetInt32());
        Assert.AreEqual(0, host.Errors);
    }

    [TestMethod]
    public void TheExportIsTheFormatOfTheClientModsEventsJson()
    {
        using var folder = new Folder();
        var hooks = """{ { at = 30, actions = { { action = "spawn", n = 3, target = "owner", kinds = { { type = "Orc Warrior", weight = 3 } } } } } }""";
        var (host, _) = Start(folder, Script(Event(hooks, target: 50, extra: "names = { \"orc\" }, unit = \"orcs\",")));
        var answer = host.HandleRequest("world_events", "export", [], publicOnly: true);
        Assert.AreEqual(200, answer.Status, answer.Body);
        var json = JsonDocument.Parse(answer.Body).RootElement;
        Assert.AreEqual(1, json.GetProperty("v").GetInt32());
        Assert.AreEqual(5, json.GetProperty("config").GetProperty("cooldown").GetInt32());
        var ev = json.GetProperty("events")[0];
        Assert.AreEqual("wave", ev.GetProperty("id").GetString());
        Assert.AreEqual(50, ev.GetProperty("target").GetInt32());
        Assert.AreEqual("orc", ev.GetProperty("names")[0].GetString());
        var hook = ev.GetProperty("hooks")[0];
        Assert.AreEqual(30, hook.GetProperty("at").GetDouble());
        Assert.AreEqual("Orc Warrior", hook.GetProperty("actions")[0].GetProperty("kinds")[0].GetProperty("type").GetString());
        Assert.AreEqual(0, host.Errors);
    }

    // ---- game regions as a scope -----------------------------------------------------------------------------------------------

    private static ScriptMonster OrcIn(uint reference, int sector) => new(reference, 38, 5, "Orc Warrior", sector, 1, 1);

    [TestMethod]
    public void AnEventCanBeBoundToAGameRegionAndOnlyItsSectorsCount()
    {
        using var folder = new Folder();
        var server = new Fake();
        server.RegionOf[2909] = 2; server.RegionOf[3000] = 2; server.RegionOf[5000] = 1;
        var (host, _) = Start(folder, Script(Event("nil", target: 10, extra: "regions = { 2 },", duration: 3600)), server);
        server.Tick(host);
        Assert.AreEqual("Southkern", State(host).GetProperty("active").GetProperty("area").GetString(), "the region's name labels the area");
        host.FireKill(server.Alice, OrcIn(1, 5000));                       // the creature died in the other region
        Assert.AreEqual(0, State(host).GetProperty("active").GetProperty("count").GetInt32());
        host.FireKill(server.Alice, OrcIn(2, 3000));
        host.FireKill(server.Alice, OrcIn(3, 2909));
        Assert.AreEqual(2, State(host).GetProperty("active").GetProperty("count").GetInt32());
        Assert.AreEqual(0, host.Errors);
    }

    [TestMethod]
    public void AKillsHookCanCountOnlyInOneRegionAndAnUnknownRegionIsHarmless()
    {
        using var folder = new Folder();
        var server = new Fake();
        server.RegionOf[2909] = 2; server.RegionOf[5000] = 1;
        var hooks = """{ { kills = { creature = "orc", count = 2, region = 2 }, actions = { { action = "gold", amount = 321, target = "killer" } } } }""";
        var (host, _) = Start(folder, Script(Event(hooks, target: 100)), server);
        server.Tick(host);
        host.FireKill(server.Alice, OrcIn(1, 5000)); host.FireKill(server.Alice, OrcIn(2, 5000)); host.FireKill(server.Alice, OrcIn(3, 5000));
        Assert.AreEqual(0, server.Gold.Count, "kills in another region do not count");
        host.FireKill(server.Bob, OrcIn(4, 2909));
        Assert.AreEqual(0, server.Gold.Count);
        host.FireKill(server.Alice, OrcIn(5, 2909));
        CollectionAssert.AreEqual(new[] { (1, 321u) }, server.Gold);
        StringAssert.Contains(State(host).GetProperty("active").GetProperty("hooks")[0].GetProperty("text").GetString(), "in Southkern");
        // a region that does not exist: the event is empty (never offered / matches nothing), nothing crashes
        using var folder2 = new Folder();
        var server2 = new Fake();
        var (host2, _) = Start(folder2, Script(Event("nil", extra: "regions = { 99 },")), server2);
        server2.Tick(host2);
        host2.FireKill(server2.Alice, OrcIn(1, 2909));
        Assert.AreEqual(0, host2.Errors);
    }

    // ---- world bosses -------------------------------------------------------------------------------------------------------------

    private static string BossHook(string extra = "") =>
        """{ { at = 30, actions = { { action = "boss", name = "Grakk", kinds = { { type = "Orc Warrior" } }, health = 20, damage = 3, taken = 0.5, reflect = 0.2, """
        + extra + """ reward = { gold = 777 }, text = "Grakk comes!", target = "killer" } } } }""";

    [TestMethod]
    public void ABossActionAnnouncesTheBossToEverybodyAndAsksTheTargetsGameToMakeIt()
    {
        using var folder = new Folder();
        var (host, server) = Start(folder, Script(Event(BossHook(), target: 100)));
        server.Tick(host);
        Kills(host, server.Bob, 30);                                              // 30 %: the boss
        var boss = server.Sent.Where(s => s.Channel == "worldevents:boss").ToList();
        CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, boss.Select(b => b.Slot).ToArray(), "everybody in the game sees it");
        var first = boss[0].Payload;
        Assert.AreEqual("active", first.GetProperty("st").GetString());
        Assert.AreEqual("Grakk", first.GetProperty("name").GetString());
        Assert.AreEqual("Wave event", first.GetProperty("event").GetString());
        Assert.AreEqual("777 gold", first.GetProperty("reward").GetString());
        StringAssert.StartsWith(first.GetProperty("id").GetString(), "1:h1:1");
        var spawn = server.Spawns.Single();
        Assert.AreEqual(2, spawn.Slot, "the killer's own game is asked to make it");
        var def = spawn.Payload.GetProperty("boss");
        Assert.AreEqual(20, def.GetProperty("health").GetDouble());
        Assert.AreEqual(3, def.GetProperty("damage").GetDouble());
        Assert.AreEqual(0.5, def.GetProperty("taken").GetDouble());
        Assert.AreEqual(1, spawn.Payload.GetProperty("n").GetInt32());
        Assert.IsTrue(server.Said.Any(s => s.Contains("the boss Grakk has appeared") && s.Contains("777 gold")));
        Assert.AreEqual(0, host.Errors);
    }

    [TestMethod]
    public void ABossIsDefeatedByAKillOfItsKindWhichPaysTheKillerAndTellsEverybody()
    {
        using var folder = new Folder();
        var (host, server) = Start(folder, Script(Event(BossHook(), target: 100)));
        server.Tick(host);
        Kills(host, server.Bob, 30);
        server.Sent.Clear();
        host.FireKill(server.Cara, Orc(9000, "Goblin", 12));                      // another kind: the boss lives
        Assert.AreEqual(0, server.Sent.Count(s => s.Channel == "worldevents:boss"));
        host.FireKill(server.Cara, Orc(9001));
        var done = server.Sent.Where(s => s.Channel == "worldevents:boss").ToList();
        Assert.AreEqual(3, done.Count);
        Assert.AreEqual("defeated", done[0].Payload.GetProperty("st").GetString());
        CollectionAssert.AreEqual(new[] { (3, 777u) }, server.Gold.Where(g => g.Slot == 3 && g.Amount == 777).ToArray());
        Assert.IsTrue(server.Said.Any(s => s.Contains("defeated the boss Grakk")));
        server.Sent.Clear();
        host.FireKill(server.Cara, Orc(9002));
        Assert.AreEqual(0, server.Sent.Count(s => s.Channel == "worldevents:boss"), "a defeated boss is over");
        Assert.AreEqual(0, host.Errors);
    }

    [TestMethod]
    public void ABossThatNobodyKillsIsGoneWhenItsWindowEndsAndAJoinerSeesALiveOne()
    {
        using var folder = new Folder();
        var (host, server) = Start(folder, Script(Event(BossHook("window = 1,"), target: 100)));
        server.Tick(host);
        Kills(host, server.Bob, 30);
        server.Sent.Clear();
        host.FirePlayer("enter", server.Cara);
        Assert.IsTrue(server.Sent.Any(s => s.Slot == 3 && s.Channel == "worldevents:boss" && s.Payload.GetProperty("st").GetString() == "active"), "a joiner is told");
        Thread.Sleep(1200);
        server.Tick(host);
        Assert.IsTrue(server.Sent.Any(s => s.Channel == "worldevents:boss" && s.Payload.GetProperty("st").GetString() == "gone"));
        Assert.IsTrue(server.Said.Any(s => s.Contains("The boss Grakk is gone")));
        Assert.AreEqual(0, host.Errors);
    }

    [TestMethod]
    public void ABossWithoutACreatureIsRefusedAndTheMultipliersAreClamped()
    {
        using var folder = new Folder();
        var (host, server) = Start(folder, Script(Event("""
            { { at = 30, actions = { { action = "boss", name = "Nothing" } } },
              { at = 40, actions = { { action = "boss", near = "Orc", health = 99999, damage = -4, taken = 0, deflect = 5 } } } }
            """, target: 100)));
        server.Tick(host);
        Kills(host, server.Bob, 40);
        var boss = server.Sent.Where(s => s.Channel == "worldevents:boss").ToList();
        Assert.AreEqual(3, boss.Count, "only the valid boss, to everybody");
        Assert.AreEqual("Orc", boss[0].Payload.GetProperty("name").GetString());
        Assert.AreEqual(0, host.Errors);
    }
}
