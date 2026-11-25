using System.Text.Json;
using System.Text.RegularExpressions;
using Sacred.GameServer.Config;
using Sacred.GameServer.Scripting;
using Sacred.GameServer.Session;

namespace Sacred.GameServer.Tests;

/// <summary>scripts-examples/bounty_board.lua against a fake server: posting and rotation, progress, the sector filter, rewards, the
/// pushed state, the dashboard (names escaped) and a restart.</summary>
[TestClass]
public class BountyBoardScriptTests
{
    private sealed class Folder : IDisposable
    {
        public readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sacred-bb-" + Guid.NewGuid().ToString("N"));
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
        public uint NetRegister(string name, bool serverOnly) => 0x4321;
        public bool NetSupports(int slot) => NetPlayers.Contains(slot);
        public int NetSend(int slot, uint channel, bool json, byte[] data, out string? error)
        {
            error = null;
            Pushed.Add((slot, JsonDocument.Parse(data).RootElement.Clone()));
            return 1;
        }
        public ScriptPlayer Alice => All[0];
        public ScriptPlayer Bob => All[1];
        public void Tick(ScriptHost host) { Now += 6000; host.Pump(Now); }
    }

    // A fixed pool: orcs (3 kills, gold + item) and wolves (2 kills, only in sector 7, gold).
    private const string Pool = """
        pool = {
          { id = "orcs", title = "Orc slayer", names = { "orc" }, target = 3, reward = { gold = 500, item = { quality = 3 } } },
          { id = "wolves", title = "Wolf <hunt>", names = { "wolf" }, target = 2, sectors = { 7 }, region = "North <plains>", reward = { gold = 100 } },
        },
        """;

    private static string Source(Func<string, string>? edit = null)
    {
        var text = ReadExample();
        text = Regex.Replace(text, @"  pool = \{\r?\n    \{ id = ""orcs"".*?\r?\n  \},\r?\n", _ => Pool + "\n", RegexOptions.Singleline);
        text = text.Replace("slots = 3,", "slots = 2,");
        return edit is null ? text : edit(text);
    }

    private static string ReadExample()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Sacred.GameServer", "scripts-examples", "bounty_board.lua");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException("bounty_board.lua");
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

    private static JsonElement State(ScriptHost host)
    {
        var answer = host.HandleRequest("bounty_board", "state", [], true);
        Assert.AreEqual(200, answer.Status, answer.Body);
        return JsonDocument.Parse(answer.Body).RootElement;
    }

    [TestMethod]
    public void TheBoardIsPostedAndABountyPaysItsRewardOnce()
    {
        using var folder = new Folder();
        folder.Write("bounty_board.lua", Source());
        var (host, server) = Start(folder);
        server.Tick(host);
        Assert.AreEqual(2, State(host).GetProperty("bounties").GetArrayLength());
        Assert.AreEqual(2, server.Said.Count(s => s.StartsWith("[Bounty] New bounty")));

        host.FireKill(server.Alice, Wolf(1));                 // sector 0: the wolf bounty is for sector 7 only
        host.FireKill(server.Alice, Orc(2)); host.FireKill(server.Alice, Orc(3));
        Assert.AreEqual(0, server.Gold.Count);
        host.FireKill(server.Alice, Orc(4));                  // 3/3
        CollectionAssert.AreEqual(new[] { (1, 500u) }, server.Gold);
        CollectionAssert.AreEqual(new (int, uint?)[] { (1, 3) }, server.Items);
        Assert.IsTrue(server.Told.Any(t => t.Slot == 1 && t.Text.Contains("Bounty complete: Orc slayer")));
        Assert.IsTrue(server.Said.Any(s => s.Contains("Alice completed the bounty \"Orc slayer\"")));
        host.FireKill(server.Alice, Orc(5));                  // done is done: no second reward
        Assert.AreEqual(1, server.Gold.Count);
        host.FireKill(server.Bob, Orc(6));                    // Bob has his own progress
        Assert.AreEqual(1, server.Gold.Count);

        var orcs = State(host).GetProperty("bounties").EnumerateArray().Single(b => b.GetProperty("title").GetString() == "Orc slayer");
        Assert.AreEqual(1, orcs.GetProperty("claims").GetInt32());
        Assert.AreEqual(2, orcs.GetProperty("hunters").GetInt32());
        Assert.AreEqual("Alice", State(host).GetProperty("top")[0].GetProperty("name").GetString());

        // The chat command lists the board with the hero's own progress.
        server.Told.Clear();
        Assert.IsTrue(host.FireChat(server.Bob, "!bounty"));
        Assert.IsTrue(server.Told.Any(t => t.Slot == 2 && t.Text.StartsWith("Orc slayer:") && t.Text.Contains("1/3")));
        Assert.AreEqual(0, host.Errors);
    }

    [TestMethod]
    public void TheBoardPublishesBountyEventsForOtherMods()
    {
        using var folder = new Folder();
        folder.Write("bounty_board.lua", Source());
        folder.Write("listener.lua", "server.on('bounty.*', function(name, ev) server.broadcast('EV ' .. name .. ' ' .. (ev.player or '-') .. ' ' .. tostring(ev.n) .. ' ' .. ev.source) end)");
        var server = new Fake();
        var host = new ScriptHost(new ScriptOptions { Directory = folder.Scripts, DataDirectory = folder.Data }, server);
        host.Load();
        server.Tick(host);
        Assert.AreEqual(2, server.Said.Count(s => s == "EV bounty.started - nil server"));
        Assert.AreEqual(5, host.DeclaredEvents("bounty.").Count);
        server.Said.Clear();
        for (uint i = 1; i <= 3; i++) host.FireKill(server.Alice, Orc(i));
        CollectionAssert.AreEqual(new[]
        {
            "EV bounty.progress Alice 1 server", "EV bounty.progress Alice 2 server", "EV bounty.progress Alice 3 server",
            "EV bounty.completed Alice 3 server", "EV bounty.claimed Alice 3 server",
        }, server.Said.Where(s => s.StartsWith("EV ")).ToArray());
        Assert.AreEqual(0, host.Errors);
    }

    [TestMethod]
    public void ARegionBountyCountsOnlyInItsSectors()
    {
        using var folder = new Folder();
        folder.Write("bounty_board.lua", Source());
        var (host, server) = Start(folder);
        server.Tick(host);
        var north = server.Alice with { Sector = 7 };
        host.FireKill(north, Wolf(1));
        host.FireKill(server.Alice, Wolf(2));                 // outside: ignored
        Assert.AreEqual(0, server.Gold.Count);
        host.FireKill(north, Wolf(3));
        CollectionAssert.AreEqual(new[] { (1, 100u) }, server.Gold);
        Assert.IsTrue(server.Told.Any(t => t.Text.Contains("Bounty complete: Wolf <hunt>")));
        Assert.IsTrue(State(host).GetProperty("bounties").EnumerateArray().Any(b => b.TryGetProperty("region", out var region) && region.GetString() == "North <plains>"));
    }

    [TestMethod]
    public void ProgressIsPushedToClientsThatSaidHelloAndTheyCanAskForIt()
    {
        using var folder = new Folder();
        folder.Write("bounty_board.lua", Source());
        var fake = new Fake();
        fake.NetPlayers.Add(1);
        var (host, server) = Start(folder, fake);   // the board is posted while loading ("start")
        server.Tick(host);
        Assert.IsTrue(server.Pushed.All(p => p.Slot == 1), "only capable players get pushes");
        var firstBoard = server.Pushed.Last().Payload;
        Assert.AreEqual(2, firstBoard.GetProperty("bounties").GetArrayLength());
        Assert.AreEqual(0, firstBoard.GetProperty("bounties")[0].GetProperty("n").GetInt32());
        server.Pushed.Clear();
        host.FireKill(server.Alice, Orc(1)); host.FireKill(server.Alice, Orc(2));
        server.Tick(host);                                    // pushed by the one-second timer after the kills
        Assert.AreEqual(1, server.Pushed.Count);
        var orcs = server.Pushed[0].Payload.GetProperty("bounties").EnumerateArray().Single(b => b.GetProperty("title").GetString() == "Orc slayer");
        Assert.AreEqual(2, orcs.GetProperty("n").GetInt32());
        Assert.AreEqual(3, orcs.GetProperty("target").GetInt32());
        Assert.IsFalse(orcs.GetProperty("done").GetBoolean());
        Assert.IsTrue(orcs.GetProperty("left").GetInt32() > 0);
        server.Pushed.Clear();
        host.FireKill(server.Bob, Orc(3));                    // Bob has no HELLO: nothing is sent for him
        server.Tick(host);
        Assert.AreEqual(0, server.Pushed.Count);
    }

    [TestMethod]
    public void ExpiredBountiesAreReplacedAndTheDashboardEscapesNames()
    {
        using var folder = new Folder();
        folder.Write("bounty_board.lua", Source(t => t.Replace("math.max(60, tonumber(def.duration)", "math.max(1, tonumber(def.duration)")
            .Replace("target = 3, reward", "target = 3, duration = 2, reward")));
        var (host, server) = Start(folder);
        server.Tick(host);
        var evil = server.Alice with { Character = "<img src=x onerror=alert(1)>", Name = "acct.<img>" };
        host.FireKill(evil, Orc(1)); host.FireKill(evil, Orc(2)); host.FireKill(evil, Orc(3));   // done: on the ladder and in "recent"
        var page = host.HandleRequest("bounty_board", "", [], true);
        Assert.AreEqual(200, page.Status);
        Assert.IsTrue(page.Page);
        StringAssert.Contains(page.Body, "&lt;img src=x onerror=alert(1)&gt;");
        Assert.IsFalse(page.Body.Contains("<img"), "player text is escaped");
        StringAssert.Contains(page.Body, "Wolf &lt;hunt&gt;");
        Assert.IsFalse(page.Body.Contains("acct"), "no account names");

        server.Said.Clear();
        Thread.Sleep(2300);
        server.Tick(host);
        Assert.IsTrue(server.Said.Any(s => s.Contains("The bounty \"Orc slayer\" has ended (1 hunters finished it)")));
        Assert.AreEqual(1, server.Said.Count(s => s.Contains("New bounty")), "the board is filled up again");
        Assert.AreEqual(2, State(host).GetProperty("bounties").GetArrayLength());
        Assert.AreEqual(0, host.Errors);
    }

    [TestMethod]
    public void TheBoardAndProgressSurviveARestartAndBadPoolEntriesAreSkipped()
    {
        using var folder = new Folder();
        folder.Write("bounty_board.lua", Source());
        var (host, server) = Start(folder);
        server.Tick(host);
        host.FireKill(server.Alice, Orc(1)); host.FireKill(server.Alice, Orc(2));
        server.Tick(host);
        host.FlushStores(wait: true);
        host.Dispose();

        var (again, server2) = Start(folder);
        server2.Tick(again);
        Assert.AreEqual(0, server2.Said.Count(s => s.Contains("New bounty")), "the same board, not a new one");
        server2.Told.Clear();
        again.FireChat(server2.Alice, "!bounty");
        Assert.IsTrue(server2.Told.Any(t => t.Text.StartsWith("Orc slayer:") && t.Text.Contains("2/3")));
        again.FireKill(server2.Alice, Orc(3));
        Assert.AreEqual(1, server2.Gold.Count);
        again.Dispose();

        using var folder2 = new Folder();
        folder2.Write("bounty_board.lua", Source(t => t.Replace("  pool = {", "  pool = { 5, { id = 'x' },")));
        var (host3, server3) = Start(folder2);
        server3.Tick(host3);
        Assert.AreEqual(2, State(host3).GetProperty("bounties").GetArrayLength());
        Assert.AreEqual(0, host3.Errors);
    }

    [TestMethod]
    public void TheShippedPoolLoadsAndFillsTheBoard()
    {
        using var folder = new Folder();
        folder.Write("bounty_board.lua", ReadExample());
        var (host, server) = Start(folder);
        server.Tick(host);
        Assert.AreEqual(3, State(host).GetProperty("bounties").GetArrayLength());
        StringAssert.Contains(host.HandleRequest("bounty_board", "", [], true).Body, "Bounty board");
        Assert.IsTrue(host.Mods.Single().Description is { Length: > 10 });
        Assert.AreEqual(0, host.Errors);
    }

    [TestMethod]
    public void AReloadKeepsTheBoardAndProgressAndStartsNoSecondTimer()
    {
        using var folder = new Folder();
        folder.Write("bounty_board.lua", Source());
        var (host, server) = Start(folder);
        server.Tick(host);
        host.FireKill(server.Alice, Orc(1)); host.FireKill(server.Alice, Orc(2));     // 2/3, only in memory plus the dirty flag
        server.Said.Clear();
        Assert.IsTrue(host.Reload("bounty_board"));
        Assert.AreEqual(2, State(host).GetProperty("bounties").GetArrayLength(), "the same board: nothing posted twice");
        server.Tick(host);
        Assert.AreEqual(0, server.Said.Count(s => s.Contains("New bounty")));
        server.Told.Clear();
        Assert.IsTrue(host.FireChat(server.Alice, "!bounty"));
        Assert.IsTrue(server.Told.Any(t => t.Text.StartsWith("Orc slayer:") && t.Text.Contains("2/3")), string.Join(";", server.Told));
        host.FireKill(server.Alice, Orc(3));
        CollectionAssert.AreEqual(new[] { (1, 500u) }, server.Gold, "paid once");
        Assert.AreEqual(1, host.Endpoints.Count(e => e.Mod == "bounty_board" && e.Path == "state"));
        StringAssert.Contains(host.HandleRequest("bounty_board", "config", [], false).Body, "last reload reason: <b>reload</b>");
        Assert.AreEqual(0, host.Errors);
    }

    [TestMethod]
    public void TheBoardIsDataSlotsAndPoolComeFromTheStorageAndBadDataFallsBack()
    {
        using var folder = new Folder();
        folder.Write("bounty_board.lua", Source());
        var server = new Fake();
        var host = new ScriptHost(new ScriptOptions { Directory = folder.Scripts, DataDirectory = folder.Data }, server);
        host.StoreOf("bounty_board").Set("config", new Dictionary<string, object?>
        {
            ["slots"] = 1.0, ["announce"] = false, ["history"] = "ten",
            ["pool"] = new List<object?>
            {
                5.0, new Dictionary<string, object?> { ["id"] = "x" },
                new Dictionary<string, object?> { ["id"] = "solo", ["title"] = "Solo hunt", ["names"] = new List<object?> { "wolf" }, ["target"] = 1.0,
                    ["reward"] = new Dictionary<string, object?> { ["gold"] = 5.0 } },
            },
        });
        host.Load();
        server.Tick(host);
        var bounties = State(host).GetProperty("bounties");
        Assert.AreEqual(1, bounties.GetArrayLength(), "slots = 1");
        Assert.AreEqual("Solo hunt", bounties[0].GetProperty("title").GetString(), "the pool from the storage, bad entries dropped");
        Assert.AreEqual(0, server.Said.Count, "announce = false");
        var rules = host.HandleRequest("bounty_board", "rules", [], false);
        Assert.AreEqual(200, rules.Status, "the editor page reads the configuration in force");
        StringAssert.Contains(rules.Body, "\"slots\":1");
        StringAssert.Contains(rules.Body, "Solo hunt");
        Assert.AreEqual(404, host.HandleRequest("bounty_board", "rules", [], true).Status, "not public");
        var page = host.HandleRequest("bounty_board", "config", [], false).Body;
        StringAssert.Contains(page, "history: needs a number");
        StringAssert.Contains(page, "item #1 refused");
        host.FireKill(server.Alice, Wolf(1));
        CollectionAssert.AreEqual(new[] { (1, 5u) }, server.Gold);

        // A live edit: the pool is replaced without a reload (the board keeps what is posted, the next bounty comes from the new pool).
        host.StoreOf("bounty_board").Set("config", new Dictionary<string, object?> { ["slots"] = 2.0, ["pool"] = new List<object?>
            { new Dictionary<string, object?> { ["id"] = "orcs2", ["title"] = "More orcs", ["names"] = new List<object?> { "orc" }, ["target"] = 2.0 } } });
        host.StoreOf("bounty_board").Set("config_rev", 1.0);
        server.Now += 2100; host.Pump(server.Now);
        server.Tick(host);
        Assert.IsTrue(State(host).GetProperty("bounties").EnumerateArray().Any(b => b.GetProperty("title").GetString() == "More orcs"));
        Assert.AreEqual(0, host.Errors);
    }
}
