using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Sacred.GameServer.Config;
using Sacred.GameServer.Scripting;
using Sacred.GameServer.Session;

namespace Sacred.GameServer.Tests;

/// <summary>scripts-examples/world_events.lua against a fake server: progress, milestones, expiry (also while the server was down), the
/// reward split, rewards for heroes who are offline, the sector filter, the pushed state and the public endpoint.</summary>
[TestClass]
public partial class WorldEventsScriptTests
{
    private sealed class Folder : IDisposable
    {
        public readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sacred-we-" + Guid.NewGuid().ToString("N"));
        public string Scripts => System.IO.Path.Combine(Path, "scripts");
        public string Data => System.IO.Path.Combine(Path, "data");
        public Folder() { Directory.CreateDirectory(Scripts); }
        public void Write(string name, string source) { File.WriteAllText(System.IO.Path.Combine(Scripts, name), source); ExampleAssets.CopyTo(Scripts); }
        public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
    }

    private sealed class Fake : IScriptServer
    {
        public long Now;
        public readonly List<string> Said = new();
        public readonly List<(int Slot, string Text)> Told = new();
        public readonly List<(int Slot, uint Amount)> Gold = new();
        public readonly List<(int Slot, uint? Quality)> Items = new();
        public readonly List<(int Slot, JsonElement Payload)> Pushed = new();
        public readonly HashSet<int> NetPlayers = [];
        public readonly List<ScriptPlayer> All = [new ScriptPlayer(1, "Alice", "name:Alice", 2, 10, true, true, 0), new ScriptPlayer(2, "Bob", "name:Bob", 1, 12, true, false, 0)];
        public long NowMs => Now;
        public IReadOnlyList<ScriptPlayer> Players() => All;
        public ScriptPlayer? Player(int slot) => All.FirstOrDefault(p => p.Slot == slot);
        public void Broadcast(string text) => Said.Add(text);
        public void Message(int slot, string text) => Told.Add((slot, text));
        public void Kick(int slot, string reason) { }
        public ScriptDrop? DropItem(int slot, in ScriptLootRequest request, out string? error)
        {
            Items.Add((slot, request.Quality)); error = null;
            return new ScriptDrop(100, 200, request.Quality ?? 0, "Test ring", "Ring");
        }
        public ScriptDrop? DropGold(int slot, uint amount, out string? error)
        {
            Gold.Add((slot, amount)); error = null;
            return new ScriptDrop(101, GoldPile.ResourceFor(amount), 0, "Gold", "Gold");
        }
        public uint NetRegister(string name, bool serverOnly) => 0x1234;
        public bool NetSupports(int slot) => NetPlayers.Contains(slot);
        public int NetSend(int slot, uint channel, bool json, byte[] data, out string? error)
        {
            error = null;
            Assert.IsTrue(json);
            Pushed.Add((slot, JsonDocument.Parse(data).RootElement.Clone()));
            return 1;
        }
        public bool Grid;
        // a fake 20 x 20 grid: the id of cell (x, y) is 1000 + y * 100 + x
        public (int X, int Y)? SectorCell(uint id) => Grid && id >= 1000 && id < 3000 && (id - 1000) % 100 < 20 ? (((int)id - 1000) % 100, ((int)id - 1000) / 100) : null;
        public uint SectorAt(int cx, int cy) => Grid && cx is >= 0 and < 20 && cy is >= 0 and < 20 ? (uint)(1000 + cy * 100 + cx) : 0;
        public ScriptPlayer Alice => All[0];
        public ScriptPlayer Bob => All[1];
        public void Tick(ScriptHost host) { Now += 2000; host.Pump(Now); }
    }

    private static string Source(Func<string, string>? edit = null)
    {
        var text = ReadExample();
        text = text.Replace("first_delay = 60,", "first_delay = 0,").Replace("cooldown = 900,", "cooldown = 5,")
            .Replace("target = 1000, duration = 2 * 3600", "target = 4, duration = 3600")
            .Replace("min_contribution = 10,", "min_contribution = 2,");
        return edit is null ? text : edit(text);
    }

    private static string ReadExample()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Sacred.GameServer", "scripts-examples", "world_events.lua");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException("world_events.lua");
    }

    private static (ScriptHost Host, Fake Server) Start(Folder folder, Fake? server = null)
    {
        server ??= new Fake();
        var host = new ScriptHost(new ScriptOptions { Directory = folder.Scripts, DataDirectory = folder.Data }, server);
        host.Load();
        Assert.IsNull(host.Files.Single().Error);
        return (host, server);
    }

    private static ScriptMonster Orc(uint reference) => new(reference, 38, 5, "Orc Warrior");
    private static ScriptMonster Wolf(uint reference) => new(reference, 77, 5, "Wolf");

    private static JsonElement State(ScriptHost host, bool publicOnly = true)
    {
        var answer = host.HandleRequest("world_events", "state", [], publicOnly);
        Assert.AreEqual(200, answer.Status, answer.Body);
        return JsonDocument.Parse(answer.Body).RootElement;
    }

    [TestMethod]
    public void AnEventRunsToItsTargetAndTheRewardIsSharedByContribution()
    {
        using var folder = new Folder();
        folder.Write("world_events.lua", Source());
        var (host, server) = Start(folder);
        Assert.AreEqual(0, server.Said.Count, "the schedule waits for the first tick");
        server.Tick(host);
        StringAssert.Contains(server.Said.Single(), "Orc hunt begins: Slay 1000 orcs");
        Assert.AreEqual("Orc hunt", State(host).GetProperty("active").GetProperty("title").GetString());

        host.FireKill(server.Alice, Wolf(1));                 // not an orc
        host.FireKill(server.Alice, Orc(2));                  // 1/4 = 25 %
        host.FireKill(server.Bob, Orc(3));                    // 2/4 = 50 %
        host.FireKill(server.Alice, Orc(4));                  // 3/4 = 75 %
        Assert.IsTrue(server.Said.Any(s => s.Contains("25% (1/4)")));
        Assert.IsTrue(server.Said.Any(s => s.Contains("50% (2/4)")));
        Assert.IsTrue(server.Said.Any(s => s.Contains("75% (3/4)")));
        Assert.AreEqual(0, server.Gold.Count);
        host.FireKill(server.Alice, Orc(5));                  // 4/4: done (Alice 3, Bob 1)
        var done = server.Said.Last();
        StringAssert.Contains(done, "Orc hunt is complete! 4/4");
        StringAssert.Contains(done, "Alice (3)");
        StringAssert.Contains(done, "1 heroes share the reward", "Bob has 1 kill, 2 are needed");

        // Alice alone qualifies: the whole pool plus gold_each; a common item for everybody and the top prize for the best.
        CollectionAssert.AreEqual(new[] { (1, 50500u) }, server.Gold);
        Assert.AreEqual(2, server.Items.Count, "the rolled item and the prize");
        Assert.AreEqual(1, server.Items.Count(i => i.Quality == 9), "the best contributor's prize");
        Assert.IsTrue(server.Items.Any(i => i.Quality is 0 or 4 or 8 or 11 or 14), "a rolled item of one of the tiers");
        Assert.IsTrue(server.Told.Any(t => t.Slot == 1 && t.Text.Contains("50500 gold")));

        // The finished event is the last result; the cooldown applies; the next one is another one in the rotation.
        var state = State(host);
        Assert.IsFalse(state.TryGetProperty("active", out _));
        Assert.IsTrue(state.GetProperty("last").GetProperty("success").GetBoolean());
        server.Said.Clear();
        server.Tick(host);
        Assert.AreEqual(0, server.Said.Count, "cooldown of 5 s");
        server.Tick(host); server.Tick(host);
        Thread.Sleep(5200);
        server.Tick(host);
        StringAssert.Contains(server.Said.Single(), "The great cull begins");
    }

    [TestMethod]
    public void TwoQualifyingHeroesShareThePoolByTheirKills()
    {
        using var folder = new Folder();
        folder.Write("world_events.lua", Source(t => t.Replace("min_contribution = 2,", "min_contribution = 1,")));
        var (host, server) = Start(folder);
        server.Tick(host);
        host.FireKill(server.Alice, Orc(1)); host.FireKill(server.Bob, Orc(2)); host.FireKill(server.Alice, Orc(3)); host.FireKill(server.Alice, Orc(4));
        // pool 50000: Alice 3/4, Bob 1/4; each gets 500 on top
        CollectionAssert.AreEquivalent(new[] { (1, 38000u), (2, 13000u) }, server.Gold);
        Assert.AreEqual(3, server.Items.Count, "two rolled items and one prize");
        Assert.AreEqual(1, server.Items.Count(i => i.Slot == 1 && i.Quality == 9));
        Assert.AreEqual(0, server.Items.Count(i => i.Slot == 2 && i.Quality == 9));
    }

    [TestMethod]
    public void AnEventThatRunsOutFailsWithoutReward()
    {
        using var folder = new Folder();
        folder.Write("world_events.lua", Source(t => t.Replace("target = 4, duration = 3600", "target = 4, duration = 1")));
        var (host, server) = Start(folder);
        server.Tick(host);
        host.FireKill(server.Alice, Orc(1));
        Thread.Sleep(1200);
        server.Tick(host);
        StringAssert.Contains(server.Said.Last(), "Orc hunt has failed: 1/4");
        Assert.AreEqual(0, server.Gold.Count + server.Items.Count);
        Assert.IsFalse(State(host).GetProperty("last").GetProperty("success").GetBoolean());
        host.FireKill(server.Alice, Orc(2));                  // no event: nothing happens, no error
        Assert.AreEqual(0, host.Errors);
    }

    [TestMethod]
    public void AnEventThatRanOutWhileTheServerWasDownIsClosedOnStart()
    {
        using var folder = new Folder();
        folder.Write("world_events.lua", Source(t => t.Replace("target = 4, duration = 3600", "target = 4, duration = 1")));
        var (host, server) = Start(folder);
        server.Tick(host);
        host.FireKill(server.Alice, Orc(1));
        server.Tick(host);                                    // saves the progress
        host.FlushStores(wait: true);
        host.Dispose();
        Thread.Sleep(1200);

        var (_, again) = Start(folder);                       // "restart"
        StringAssert.Contains(again.Said.Single(), "Orc hunt has failed: 1/4");
    }

    [TestMethod]
    public void ARunningEventAndItsContributionsSurviveARestart()
    {
        using var folder = new Folder();
        folder.Write("world_events.lua", Source());
        var (host, server) = Start(folder);
        server.Tick(host);
        host.FireKill(server.Alice, Orc(1)); host.FireKill(server.Alice, Orc(2));
        server.Tick(host);
        host.FlushStores(wait: true);
        host.Dispose();

        var (host2, server2) = Start(folder);
        Assert.AreEqual(0, server2.Said.Count);
        var active = State(host2).GetProperty("active");
        Assert.AreEqual(2, active.GetProperty("count").GetInt32());
        Assert.AreEqual("Alice", active.GetProperty("top")[0].GetProperty("name").GetString());
        host2.FireKill(server2.Alice, Orc(3)); host2.FireKill(server2.Alice, Orc(4));
        StringAssert.Contains(server2.Said.Last(), "Orc hunt is complete! 4/4");
        Assert.AreEqual(50500u, server2.Gold.Single().Amount);
    }

    [TestMethod]
    public void HeroesWhoAreOfflineGetTheirRewardWhenTheyEnter()
    {
        using var folder = new Folder();
        folder.Write("world_events.lua", Source(t => t.Replace("min_contribution = 2,", "min_contribution = 1,")));
        var (host, server) = Start(folder);
        server.Tick(host);
        var offlineBob = server.Bob with { InGame = false };
        server.All[1] = offlineBob;
        host.FireKill(server.Alice, Orc(1)); host.FireKill(offlineBob, Orc(2)); host.FireKill(server.Alice, Orc(3)); host.FireKill(server.Alice, Orc(4));
        CollectionAssert.AreEqual(new[] { (1, 38000u) }, server.Gold, "Bob is not in the world: nothing dropped yet");
        host.FlushStores(wait: true);

        // A restart in between loses nothing: the waiting reward is in the mod's storage.
        host.Dispose();
        var (host2, server2) = Start(folder);
        server2.All[1] = server2.Bob with { InGame = false };
        server2.Tick(host2);
        Assert.AreEqual(0, server2.Gold.Count);
        server2.All[1] = server2.Bob with { InGame = true };
        host2.FirePlayer("enter", server2.Bob);
        CollectionAssert.AreEqual(new[] { (2, 13000u) }, server2.Gold);
        Assert.AreEqual(1, server2.Items.Count(i => i.Slot == 2), "the plain item");
        server2.Tick(host2);
        Assert.AreEqual(1, server2.Gold.Count, "delivered once");
    }

    [TestMethod]
    public void AnEventWithSectorsCountsOnlyKillsInThem()
    {
        using var folder = new Folder();
        folder.Write("world_events.lua", Source(t => t.Replace("names = { \"orc\" },", "sectors = { 2909, 2910 },")));
        var (host, server) = Start(folder);
        server.Tick(host);
        var inside = server.Alice with { Sector = 2910 };
        var outside = server.Bob with { Sector = 100 };
        host.FireKill(outside, Orc(1)); host.FireKill(server.Alice, Orc(2));      // sector 0: unknown
        host.FireKill(inside, Orc(3)); host.FireKill(inside, Wolf(4));            // any creature there
        var active = State(host).GetProperty("active");
        Assert.AreEqual(2, active.GetProperty("count").GetInt32());
        Assert.IsFalse(active.TryGetProperty("area", out _), "an anonymous sector list has no name");

        // !sector tells the number
        Assert.IsTrue(host.FireChat(inside, "!sector"));
        Assert.AreEqual("You are in sector 2910.", server.Told.Last().Text);
    }

    [TestMethod]
    public void TheStateIsPushedToClientsWithTheirOwnSharePerSector()
    {
        using var folder = new Folder();
        folder.Write("world_events.lua", Source(t => t.Replace("names = { \"orc\" },", "sectors = { 2909 },")));
        var server = new Fake();
        server.NetPlayers.Add(1); server.NetPlayers.Add(2);
        var (host, _) = Start(folder, server);
        server.Tick(host);                                    // starts and pushes to both
        Assert.AreEqual(2, server.Pushed.Count);
        host.FireKill(server.Alice with { Sector = 2909 }, Orc(1));
        server.Pushed.Clear();
        server.Tick(host);                                    // progress changed: pushed
        var alice = server.Pushed.Single(p => p.Slot == 1).Payload;
        var bob = server.Pushed.Single(p => p.Slot == 2).Payload;
        Assert.AreEqual("active", alice.GetProperty("st").GetString());
        Assert.AreEqual("creatures", alice.GetProperty("unit").GetString());
        Assert.AreEqual(1, alice.GetProperty("count").GetInt32());
        Assert.AreEqual(4, alice.GetProperty("target").GetInt32());
        Assert.AreEqual(1, alice.GetProperty("mine").GetInt32());
        Assert.IsFalse(alice.GetProperty("here").GetBoolean(), "sector 0: not seen in the sector yet");
        Assert.AreEqual(0, bob.GetProperty("mine").GetInt32());
        Assert.IsTrue(alice.GetProperty("left").GetDouble() > 3000);

        // moving into the sector pushes at once, with here = true
        server.Pushed.Clear();
        server.All[0] = server.Alice with { Sector = 2909 };
        host.FireSector(server.Alice);
        Assert.IsTrue(server.Pushed.Single().Payload.GetProperty("here").GetBoolean());

        // a client's request is answered, at most once a second; players without the messages get nothing
        server.Pushed.Clear();
        server.NetPlayers.Remove(2);
        Assert.IsTrue(host.FireNet(0x1234, server.Bob, true, "{}"u8.ToArray()));
        Assert.AreEqual(0, server.Pushed.Count);
        Assert.IsTrue(host.FireNet(0x1234, server.Alice, true, "{}"u8.ToArray()));
        Assert.IsTrue(host.FireNet(0x1234, server.Alice, true, "{}"u8.ToArray()));
        Assert.AreEqual(1, server.Pushed.Count);
        Assert.AreEqual(0, host.Errors);
    }

    [TestMethod]
    public void ChatCommandAnswersPrivatelyAndPublicOutputHasNoOwnerKeys()
    {
        using var folder = new Folder();
        folder.Write("world_events.lua", Source());
        var (host, server) = Start(folder);
        Assert.IsTrue(host.FireChat(server.Alice, "!event"));
        StringAssert.Contains(server.Told.Last().Text, "No world event is running");
        server.Tick(host);
        host.FireKill(server.Alice, Orc(1));
        server.Told.Clear();
        Assert.IsTrue(host.FireChat(server.Alice, "!event"));
        Assert.IsTrue(server.Told.Any(t => t.Text.Contains("1/4 (25%)")));
        Assert.IsTrue(server.Told.Any(t => t.Text.Contains("Your share: 1 (2 needed")));
        Assert.IsFalse(host.FireChat(server.Alice, "hello"));

        var json = host.HandleRequest("world_events", "state", [], publicOnly: true).Body;
        Assert.IsFalse(json.Contains("name:Alice") || json.Contains("uid"), json);
        StringAssert.Contains(json, "\"Alice\"");
        var page = host.HandleRequest("world_events", "", [], publicOnly: true);
        Assert.AreEqual(200, page.Status);
        Assert.IsTrue(page.Page);
        StringAssert.Contains(page.Body, "Orc hunt");
        StringAssert.Contains(page.Body, "<b>Alice</b>");
        Assert.AreEqual(0, host.Errors);
    }

    private sealed class LiveServer : IDisposable
    {
        public readonly GameServerHost Host;
        private readonly Thread _thread;
        public LiveServer(Folder folder)
        {
            var config = new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5 };
            config.Scripts.Directory = folder.Scripts; config.Scripts.DataDirectory = folder.Data;
            Host = new GameServerHost(config, new AdmissionRules(null), null);
            Host.StartScripts();
            _thread = new Thread(Host.Run) { IsBackground = true };
            _thread.Start();
        }
        public int Port => Host.TcpPort;
        public void Dispose() { Host.Stop(); _thread.Join(); Host.Dispose(); }
    }

    private static string ChatText(byte[] payload)
    {
        var text = Encoding.Unicode.GetString(payload, 4, payload.Length - 4);
        var end = text.IndexOf(' ');
        return end < 0 ? text : text[..end];
    }

    [TestMethod]
    public void KillsInASectorCountThroughTheRealGameServer()
    {
        using var folder = new Folder();
        folder.Write("world_events.lua", Source(t => t.Replace("names = { \"orc\" },", "sectors = { 2909 },")));
        using var server = new LiveServer(folder);
        using var host = RawClient.Admit(server.Port, "Host", 1);
        host.Send(0x016, default);
        host.Send(0x018, BitConverter.GetBytes(0x1964u)); host.WaitFor(0x018);
        // the scheduler starts the event on its first tick (every 2 s) and tells everybody
        var begins = host.WaitFor(0x005, 6000);
        StringAssert.Contains(ChatText(begins.Payload), "Orc hunt begins");

        void Kill(uint victim)
        {
            var death = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(death, victim);
            BinaryPrimitives.WriteUInt32LittleEndian(death.AsSpan(4), (uint)host.Slot);
            host.Send(0x17D, death);
        }
        var chat = new byte[4 + "!event".Length * 2 + 2];
        Encoding.Unicode.GetBytes("!event", chat.AsSpan(4));
        Kill(9001);                                           // the hero has not entered a sector yet: sector 0, not counted
        host.Send(0x005, chat);
        Assert.IsTrue(ChatText(host.WaitFor(0x003).Payload).Contains("0/4 (0%)"), "outside the sector");

        var entry = Convert.FromHexString("5d0b00001f0b200b210b5e0b9d0b9c0b9b0b5c0b5d0b" + new string('0', 36 * 2) + "7c080000");   // sector 0x0B5D = 2909
        host.Send(0x148, entry);
        host.WaitFor(0x148);
        Kill(9002); Kill(9003); Kill(9003);                   // one reported twice
        host.Send(0x005, chat);
        var lines = new List<string>();
        for (var i = 0; i < 2; i++) lines.Add(ChatText(host.WaitFor(0x003).Payload));
        Assert.IsTrue(lines.Any(l => l.Contains("2/4 (50%)")), string.Join(" | ", lines));
        Assert.IsTrue(lines.Any(l => l.Contains("Your share: 2")), string.Join(" | ", lines));
    }

    [TestMethod]
    public void ScriptsSeeTheVictimsSectorAndPositionAndThePlayersPosition()
    {
        using var folder = new Folder();
        folder.Write("probe.lua", """
            server.on("kill", function(p, m) server.broadcast("kill " .. tostring(m.sector) .. "," .. tostring(m.x) .. "," .. tostring(m.y)) end)
            server.on("chat", function(p, t) if t == "!probe" then server.message(p, "player " .. p.sector .. "," .. p.x .. "," .. p.y) return true end end)
            """);
        using var server = new LiveServer(folder);
        using var host = RawClient.Admit(server.Port, "Host", 1);
        host.Send(0x016, default);
        host.Send(0x018, BitConverter.GetBytes(0x1964u)); host.WaitFor(0x018);
        // a creature's 0x16F state: [ref][type][x][y][surface][sector]
        var state = new byte[24 + 40];
        BinaryPrimitives.WriteUInt32LittleEndian(state, 9001);
        BinaryPrimitives.WriteUInt32LittleEndian(state.AsSpan(4), 7);
        BinaryPrimitives.WriteInt32LittleEndian(state.AsSpan(8), 123456);
        BinaryPrimitives.WriteInt32LittleEndian(state.AsSpan(12), 654321);
        state[16] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(state.AsSpan(20), 2909);
        host.Send(0x16F, state);
        Thread.Sleep(200);
        var death = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(death, 9001);
        BinaryPrimitives.WriteUInt32LittleEndian(death.AsSpan(4), (uint)host.Slot);
        host.Send(0x17D, death);
        Assert.AreEqual("kill 2909,123456,654321", ChatText(host.WaitFor(0x005).Payload));
        // a creature the server never saw: no position
        var unknown = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(unknown, 9002);
        BinaryPrimitives.WriteUInt32LittleEndian(unknown.AsSpan(4), (uint)host.Slot);
        host.Send(0x17D, unknown);
        Assert.AreEqual("kill nil,nil,nil", ChatText(host.WaitFor(0x005).Payload));
    }

    [TestMethod]
    public void TheSectorGridIsAskedFromTheHostAndAbsentWithoutIt()
    {
        using var folder = new Folder();
        folder.Write("grid.lua", """
            local c = server.sector_cell(1105)
            server.save("cell", c and (c.x .. "," .. c.y) or "none")
            server.save("none", tostring(server.sector_cell(5)))
            server.save("at", tostring(server.sector_at(5, 1)))
            server.save("at0", tostring(server.sector_at(99, 99)))
            """);
        var server = new Fake { Grid = true };
        var (host, _) = Start(folder, server);
        var store = host.StoreOf("grid");
        Assert.IsTrue(store.TryGet("cell", out var cell)); Assert.AreEqual("5,1", cell);
        Assert.IsTrue(store.TryGet("none", out var none)); Assert.AreEqual("nil", none);
        Assert.IsTrue(store.TryGet("at", out var at)); Assert.AreEqual("1105", at);
        Assert.IsTrue(store.TryGet("at0", out var at0)); Assert.AreEqual("nil", at0);
        var bare = Start(folder, new Fake()).Host.StoreOf("grid");           // a host without game data
        Assert.IsTrue(bare.TryGet("cell", out var nogrid)); Assert.AreEqual("none", nogrid);
    }

    // ---- areas ------------------------------------------------------------------------------------------------------------

    private static string AreaSource(string areaLine, Func<string, string>? more = null) =>
        Source(t =>
        {
            t = t.Replace("silver_creek = { name = \"Silver Creek\", sectors = {}, near = 1 },", areaLine)
                 .Replace("target = 4, duration = 3600, names = { \"orc\" },", "target = 100, duration = 3600, area = \"silver_creek\", names = { \"thief\" },");
            return more is null ? t : more(t);
        });

    private static ScriptMonster Thief(uint reference, int sector = 0, int x = 0, int y = 0) => new(reference, 77, 5, "Thief", sector, x, y);

    private static int Count(ScriptHost host) => State(host).GetProperty("active").GetProperty("count").GetInt32();

    [TestMethod]
    public void AnAreaOfListsAndRangesCountsKillsByTheVictimsSectorWithAFallback()
    {
        using var folder = new Folder();
        folder.Write("world_events.lua", AreaSource("silver_creek = { name = \"Silver Creek\", sectors = { 2900, { 2910, 2912 } } },"));
        var (host, server) = Start(folder);
        server.Tick(host);
        StringAssert.Contains(server.Said.Single(), "Orc hunt begins: Slay 1000 orcs");
        var inside = server.Alice with { Sector = 2911 };
        var outside = server.Bob with { Sector = 5000 };
        host.FireKill(outside, Thief(1, 2911));              // the thief is in the area, the killer is not: counts
        host.FireKill(inside, Thief(2, 5000));               // the killer is in the area, the thief is not: does not
        host.FireKill(inside, Thief(3));                     // the creature's place is unknown: the killer's place decides
        host.FireKill(outside, Thief(4));                    // ... outside
        host.FireKill(outside, Thief(5, 2909)); host.FireKill(outside, Thief(6, 2913));   // just outside the range
        host.FireKill(outside, Thief(7, 2910)); host.FireKill(outside, Thief(8, 2912)); host.FireKill(outside, Thief(9, 2900));
        Assert.AreEqual(5, Count(host));
        Assert.AreEqual("Silver Creek", State(host).GetProperty("active").GetProperty("area").GetString());
    }

    [TestMethod]
    public void TheWhereSettingChoosesVictimPlayerOrEither()
    {
        foreach (var (where, want) in new[] { ("victim", 2), ("player", 2), ("either", 3) })
        {
            using var folder = new Folder();
            folder.Write("world_events.lua", AreaSource("silver_creek = { name = \"Silver Creek\", sectors = { 10 } },",
                t => t.Replace("where = \"victim\",", $"where = \"{where}\",")));
            var (host, server) = Start(folder);
            server.Tick(host);
            var inside = server.Alice with { Sector = 10 };
            var outside = server.Bob with { Sector = 99 };
            host.FireKill(outside, Thief(1, 10));            // victim in, killer out
            host.FireKill(inside, Thief(2, 99));             // victim out, killer in
            host.FireKill(inside, Thief(3, 10));             // both in
            host.FireKill(outside, Thief(4, 99));            // both out
            Assert.AreEqual(want, Count(host), where);
        }
    }

    [TestMethod]
    public void ACircleCountsKillsInsideItsRadiusWhateverTheSector()
    {
        using var folder = new Folder();
        folder.Write("world_events.lua", AreaSource("silver_creek = { name = \"Silver Creek\", sectors = {}, circle = { x = 1000, y = 1000, r = 500 } },"));
        var (host, server) = Start(folder);
        server.Tick(host);
        host.FireKill(server.Alice, Thief(1, 7, 1200, 1200));    // 283 away
        host.FireKill(server.Alice, Thief(2, 7, 1500, 1000));    // exactly on the edge
        host.FireKill(server.Alice, Thief(3, 7, 1600, 1000));    // 600 away
        host.FireKill(server.Alice, Thief(4, 8, 0, 0));          // far
        Assert.AreEqual(2, Count(host));
    }

    [TestMethod]
    public void RectUsesTheSectorGridAndMarginWidensWhereTheClientShowsTheEvent()
    {
        using var folder = new Folder();
        // the fake grid: id = 1000 + y * 100 + x. The rect (3,3)-(2,2) is the cells 1202 1203 1302 1303
        folder.Write("world_events.lua", AreaSource("silver_creek = { name = \"Silver Creek\", rect = { 3, 3, 2, 2 } },"));
        var server = new Fake { Grid = true };
        server.NetPlayers.Add(1);
        var (host, _) = Start(folder, server);
        server.Tick(host);
        foreach (var id in new[] { 1202, 1203, 1302, 1303 }) host.FireKill(server.Bob, Thief((uint)id, id));
        host.FireKill(server.Bob, Thief(90, 1204)); host.FireKill(server.Bob, Thief(91, 1201));
        Assert.AreEqual(4, Count(host));
        // the margin: one cell around the area is "here" for the client, two cells away is not
        server.Pushed.Clear();
        host.FireSector(server.Alice with { Sector = 1204 });          // cell (4,2): next to the rect
        Assert.IsTrue(server.Pushed.Single().Payload.GetProperty("here").GetBoolean());
        Assert.AreEqual("Silver Creek", server.Pushed.Single().Payload.GetProperty("area").GetString());
        server.Pushed.Clear();
        host.FireSector(server.Alice with { Sector = 1205 });          // cell (5,2): two away
        Assert.IsFalse(server.Pushed.Single().Payload.GetProperty("here").GetBoolean());
    }

    [TestMethod]
    public void NearAddsTheNeighboursAndAnEmptyAreaIsNotOffered()
    {
        using var folder = new Folder();
        folder.Write("world_events.lua", AreaSource("silver_creek = { name = \"Silver Creek\", sectors = { 1505 }, near = 1 },"));
        var (host, server) = Start(folder, new Fake { Grid = true });
        server.Tick(host);
        foreach (var id in new[] { 1404, 1405, 1406, 1504, 1505, 1506, 1604, 1605, 1606 }) host.FireKill(server.Bob, Thief((uint)id, id));
        host.FireKill(server.Bob, Thief(90, 1403)); host.FireKill(server.Bob, Thief(91, 1607));
        Assert.AreEqual(9, Count(host));

        // an area without sectors: the event is skipped, the next one in the rotation starts
        using var empty = new Folder();
        empty.Write("world_events.lua", AreaSource("silver_creek = { name = \"Silver Creek\", sectors = {} },"));
        var (host2, server2) = Start(empty);
        server2.Tick(host2);
        StringAssert.Contains(server2.Said.Single(), "The great cull begins");
    }

    [TestMethod]
    public void AdminsRecordAreasByWalkingAndTheSnippetIsReady()
    {
        using var folder = new Folder();
        folder.Write("world_events.lua", Source());
        var (host, server) = Start(folder);
        var alice = server.Alice with { Sector = 2909, X = 5000, Y = 6000 };            // the host: an admin
        Assert.IsTrue(host.FireChat(server.Bob with { Sector = 1 }, "!area start town"));
        StringAssert.Contains(server.Told.Last().Text, "Only the admins");
        Assert.IsTrue(host.FireChat(alice, "!area add"));
        StringAssert.Contains(server.Told.Last().Text, "Not recording");
        Assert.IsTrue(host.FireChat(alice, "!area start silver_creek Silver \"Creek\""));
        foreach (var id in new[] { 2910, 2911, 2912, 2920, 2915 }) host.FireSector(alice with { Sector = id });
        host.FireSector(alice with { Sector = 2911 });                                  // twice: once
        Assert.IsTrue(host.FireChat(alice, "!area circle 800"));
        Assert.IsTrue(host.FireChat(alice, "!area remove"));                             // the sector it stands in (2909)
        Assert.IsTrue(host.FireChat(alice, "!area stop"));
        var line = server.Told.Last().Text;
        Assert.AreEqual("silver_creek = { name = \"Silver \\\"Creek\\\"\", sectors = {{2910, 2912}, 2915, 2920}, circle = { x = 5000, y = 6000, r = 800 } },", line);
        host.FlushStores(wait: true);
        // stored in the mod's storage in the config format
        Assert.IsTrue(host.StoreOf("world_events").TryGet("areas", out var stored));
        StringAssert.Contains(System.Text.Json.JsonSerializer.Serialize(stored), "silver_creek");
        // where am I: the area is listed
        Assert.IsTrue(host.FireChat(alice with { Sector = 2915 }, "!here"));
        StringAssert.Contains(server.Told.Last().Text, "sector 2915");
        StringAssert.Contains(server.Told.Last().Text, "area silver_creek");
        Assert.IsTrue(host.FireChat(alice, "!area show silver_creek"));
        Assert.AreEqual(line, server.Told.Last().Text);
        Assert.IsTrue(host.FireChat(alice, "!area drop silver_creek"));
        Assert.IsTrue(host.FireChat(alice, "!area list"));
        Assert.IsFalse(server.Told.Last().Text.Contains("silver_creek*"));
    }

    // ---- reward odds ------------------------------------------------------------------------------------------------------

    private static string WithMilestones(string rows, Func<string, string>? more = null) =>
        Source(t =>
        {
            t = System.Text.RegularExpressions.Regex.Replace(t, @"      milestones = \{.*?\n      \} \} \},",
                "      milestones = {\n" + rows + "\n      } } },", System.Text.RegularExpressions.RegexOptions.Singleline);
            return more is null ? t : more(t);
        });

    private static readonly string FourRows = @"
        { at = 1,  weights = { common = 100 } },
        { at = 10, weights = { common = 75, magic = 25 } },
        { at = 30, weights = { common = 50, magic = 30, rare = 20 } },
        { at = 80, weights = { common = 35, magic = 30, rare = 25, epic = 8, legendary = 2 } },";

    [TestMethod]
    public void OddsAreNormalisedAndABadRowIsRefusedNotTheEvent()
    {
        using var folder = new Folder();
        folder.Write("world_events.lua", WithMilestones(@"
            { at = 1, weights = { legendary = 100 } },
            { at = 2, weights = { common = 0, magic = 0 } },
            { at = 3, weights = { flaming = 5, common = 5 } },
            { at = 4, weights = { common = -1, magic = 5 } },
            { at = 150, weights = { common = 5 } },
            { at = 10, weights = { common = 70, magic = 30 } },
            { at = 90, weights = { common = 1, magic = 1, rare = 1, epic = 1, legendary = 1 } },"));
        var (host, server) = Start(folder);
        server.Tick(host);
        var odds = State(host).GetProperty("active").GetProperty("reward_odds");
        Assert.AreEqual(2, odds.GetArrayLength(), "only the two valid rows are kept");
        foreach (var row in odds.EnumerateArray())
        {
            double sum = 0, top = 0;
            foreach (var c in row.GetProperty("chances").EnumerateArray())
            {
                sum += c.GetProperty("percent").GetDouble();
                if (c.GetProperty("tier").GetString() == "legendary") top = c.GetProperty("percent").GetDouble();
            }
            Assert.AreEqual(100.0, sum, 1e-9);
            Assert.IsTrue(top < 100);
        }
        var first = odds[0].GetProperty("chances");
        Assert.AreEqual("common", first[0].GetProperty("tier").GetString());
        Assert.AreEqual(70.0, first[0].GetProperty("percent").GetDouble(), 1e-9);
        Assert.AreEqual(30.0, first[1].GetProperty("percent").GetDouble(), 1e-9);
        Assert.AreEqual(20.0, odds[1].GetProperty("chances")[4].GetProperty("percent").GetDouble(), 1e-9, "five equal weights: 20 % each");
    }

    [TestMethod]
    public void ReachingAMilestoneAnnouncesTheNewOddsAndTheCommandShowsThemAll()
    {
        using var folder = new Folder();
        folder.Write("world_events.lua", WithMilestones(FourRows, t => t.Replace("target = 4, duration = 3600", "target = 20, duration = 3600")));
        var (host, server) = Start(folder);
        server.Tick(host);
        host.FireKill(server.Alice, Orc(1));                                // 5 %: the first milestone (1 %)
        Assert.IsTrue(server.Said.Any(s => s.Contains("Orc hunt 1%: reward odds now: Common 100%")), string.Join(" | ", server.Said));
        for (uint i = 2; i <= 6; i++) host.FireKill(server.Alice, Orc(i));  // 30 %
        Assert.IsTrue(server.Said.Any(s => s.Contains("Orc hunt 30%: reward odds now: Rare 20%, Magic 30%, Common 50%")), string.Join(" | ", server.Said));
        Assert.IsTrue(host.FireChat(server.Alice, "!event rewards"));
        var told = string.Join("\n", server.Told.Select(t => t.Text));
        StringAssert.Contains(told, "from 1%: Common 100%");
        StringAssert.Contains(told, "from 80%: Magic 30%, Common 35%".Replace("Magic 30%, Common 35%", "Legendary 2%, Epic 8%, Rare 25%, Magic 30%, Common 35%"));
        StringAssert.Contains(told, "Only heroes with at least 2 kills take part. Nothing else changes the odds.");
        StringAssert.Contains(told, "Now (30% done): Rare 20%, Magic 30%, Common 50%");
        // the dashboard shows the table with the current row marked, and the JSON has all of it
        var page = host.HandleRequest("world_events", "", [], publicOnly: true).Body;
        StringAssert.Contains(page, "Reward odds");
        StringAssert.Contains(page, "<tr class=\"now\"><td>30%</td><td>50%</td><td>30%</td><td>20%</td><td>&ndash;</td><td>&ndash;</td></tr>");
        var active = State(host).GetProperty("active");
        Assert.AreEqual(3, active.GetProperty("odds_now").GetInt32());
        Assert.IsTrue(active.GetProperty("reward_rules").GetArrayLength() >= 4);
    }

    [TestMethod]
    public void TheRollsUseTheOddsOfTheHighestMilestoneAndAreLoggedPublicly()
    {
        using var folder = new Folder();
        folder.Write("world_events.lua", WithMilestones(@"
            { at = 1,  weights = { common = 100 } },
            { at = 50, weights = { rare = 100 } },
            { at = 99, weights = { legendary = 1, epic = 99 } },", t => t.Replace("min_contribution = 2,", "min_contribution = 1,")));
        var (host, server) = Start(folder);
        server.Tick(host);
        for (uint i = 1; i <= 4; i++) host.FireKill(server.Alice, Orc(i));     // 100 %: the last row: epic 99 %, legendary 1 %
        Assert.AreEqual(2, server.Items.Count, "one rolled item and the prize");
        Assert.IsTrue(server.Items.Any(i => i.Quality == 11 || i.Quality == 14), "an epic or a legendary item");
        Assert.AreEqual(1, server.Items.Count(i => i.Quality == 9), "the best contributor's prize is not rolled");
        var rewards = State(host).GetProperty("rewards");
        Assert.AreEqual(1, rewards.GetArrayLength());
        Assert.AreEqual("Alice", rewards[0].GetProperty("name").GetString());
        Assert.AreEqual("Test ring", rewards[0].GetProperty("item").GetString());
        Assert.AreEqual(99.0, rewards[0].GetProperty("at").GetDouble());
        var json = host.HandleRequest("world_events", "state", [], publicOnly: true).Body;
        Assert.IsFalse(json.Contains("uid") || json.Contains("name:Alice"), json);
        StringAssert.Contains(host.HandleRequest("world_events", "", [], publicOnly: true).Body, "<b>Alice</b>");
    }

    [TestMethod]
    public void AnEventThatRunsOutStillRollsWithTheOddsItReachedAndPaysNoGold()
    {
        foreach (var onFail in new[] { true, false })
        {
            using var folder = new Folder();
            folder.Write("world_events.lua", WithMilestones(@"
                { at = 1,  weights = { common = 100 } },
                { at = 30, weights = { rare = 100 } },
                { at = 90, weights = { magic = 100 } },", t => t.Replace("target = 4, duration = 3600", "target = 10, duration = 1")
                    .Replace("min_contribution = 2,", "min_contribution = 1,")
                    .Replace("      milestones = {", (onFail ? "" : "      on_fail = false,\n") + "      milestones = {")));
            var (host, server) = Start(folder);
            server.Tick(host);
            for (uint i = 1; i <= 4; i++) host.FireKill(server.Alice, Orc(i));     // 40 %: the 30 % row, the rare one
            Thread.Sleep(1200);
            server.Tick(host);
            StringAssert.Contains(server.Said.Last(), "has failed: 4/10");
            Assert.AreEqual(0, server.Gold.Count, "no gold without the goal");
            if (onFail)
            {
                CollectionAssert.AreEqual(new (int, uint?)[] { (1, 8) }, server.Items, "a rare item, no prize");
                StringAssert.Contains(server.Said.Last(), "30% odds: Rare 100%");
            }
            else Assert.AreEqual(0, server.Items.Count);
        }
    }

    [TestMethod]
    public void TheRollIsFairOverManyHeroes()
    {
        using var folder = new Folder();
        folder.Write("world_events.lua", WithMilestones(@"
            { at = 1, weights = { common = 70, magic = 30 } },", t => t.Replace("target = 4, duration = 3600", "target = 150, duration = 3600")
                .Replace("min_contribution = 2,", "min_contribution = 1,").Replace("top_item = { quality = 9 }", "top_item = nil")));
        var server = new Fake();
        server.All.Clear();
        for (var slot = 1; slot <= 150; slot++) server.All.Add(new ScriptPlayer(slot, "Hero" + slot, "name:Hero" + slot, 1, 10, true, slot == 1, 0));
        var (host, _) = Start(folder, server);
        server.Tick(host);
        for (var i = 0; i < 150; i++) host.FireKill(server.All[i], Orc((uint)(100 + i)));
        Assert.AreEqual(150, server.Items.Count);
        var magic = server.Items.Count(i => i.Quality == 4);
        Assert.AreEqual(150, magic + server.Items.Count(i => i.Quality == 0), "only the two tiers with a weight");
        Assert.IsTrue(magic is > 20 and < 70, $"30 % of 150 is 45, got {magic}");
        Assert.AreEqual(0, host.Errors);
    }

    [TestMethod]
    public void ThePushedStateCarriesTheCurrentAndTheNextOdds()
    {
        using var folder = new Folder();
        folder.Write("world_events.lua", WithMilestones(FourRows, t => t.Replace("target = 4, duration = 3600", "target = 20, duration = 3600")));
        var server = new Fake();
        server.NetPlayers.Add(1);
        var (host, _) = Start(folder, server);
        server.Tick(host);
        for (uint i = 1; i <= 2; i++) host.FireKill(server.Alice, Orc(i));       // 10 %
        server.Pushed.Clear();
        server.Tick(host);
        var payload = server.Pushed.Single(p => p.Slot == 1).Payload;
        Assert.AreEqual("Magic 25%, Common 75%", payload.GetProperty("odds").GetString());
        Assert.AreEqual(10, payload.GetProperty("odds_at").GetInt32());
        Assert.AreEqual("Rare 20%, Magic 30%, Common 50%", payload.GetProperty("odds_next").GetString());
        Assert.AreEqual(30, payload.GetProperty("odds_next_at").GetInt32());
    }
}
