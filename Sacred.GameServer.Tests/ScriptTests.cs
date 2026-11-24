using System.Buffers.Binary;
using System.Text;
using Sacred.GameServer.Config;
using Sacred.GameServer.Scripting;
using Sacred.GameServer.Session;

namespace Sacred.GameServer.Tests;

[TestClass]
public partial class ScriptTests
{
    private sealed class Folder : IDisposable
    {
        public readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sacred-scripts-" + Guid.NewGuid().ToString("N"));
        public Folder() { Directory.CreateDirectory(Path); }
        public string Scripts => System.IO.Path.Combine(Path, "scripts");
        public string Data => System.IO.Path.Combine(Path, "data.json");
        public void Write(string name, string source) { Directory.CreateDirectory(Scripts); File.WriteAllText(System.IO.Path.Combine(Scripts, name), source); }
        public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
    }

    /// <summary>Script host against a fake server (no sockets).</summary>
    private sealed class FakeServer : IScriptServer
    {
        public long Now;
        public readonly List<string> Said = new();
        public readonly List<(int Slot, string Text)> Told = new();
        public readonly List<int> Kicked = new();
        public readonly List<ScriptPlayer> All = [new ScriptPlayer(1, "Alice", "name:Alice", 2, 10, true, true, 0)];
        public long NowMs => Now;
        public IReadOnlyList<ScriptPlayer> Players() => All;
        public ScriptPlayer? Player(int slot) => All.FirstOrDefault(p => p.Slot == slot);
        public Exception? BroadcastFails;
        public void Broadcast(string text) { if (BroadcastFails is { } e) throw e; Said.Add(text); }
        public void Message(int slot, string text) => Told.Add((slot, text));
        public void Kick(int slot, string reason) => Kicked.Add(slot);
        public readonly List<ScriptLootRequest> Drops = new();
        public ScriptDrop? DropItem(int slot, in ScriptLootRequest request, out string? error)
        {
            Drops.Add(request); error = null;
            return new ScriptDrop(100, 200, request.Quality ?? 0, "Test ring", "Ring");
        }
        public string? CreatureName(uint type) => type == 38 ? "Orc Warrior" : null;
        public string? ItemName(uint type) => type == 2501 ? "Longbow" : null;
        public readonly List<uint> Gold = new();
        public bool GoldFails;
        public ScriptDrop? DropGold(int slot, uint amount, out string? error)
        {
            if (GoldFails) { error = "no"; return null; }
            Gold.Add(amount); error = null;
            return new ScriptDrop(101, GoldPile.ResourceFor(amount), 0, "Gold", "Gold");
        }
    }

    private static (ScriptHost Host, FakeServer Server, ModStore Store) Host(Folder folder, int budget = 1_000_000, string mod = "a")
    {
        var options = new ScriptOptions { Directory = folder.Scripts, DataDirectory = folder.Data, InstructionBudget = budget };
        var server = new FakeServer();
        var host = new ScriptHost(options, server);
        host.Load();
        return (host, server, host.StoreOf(mod));
    }

    private static string Example(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Sacred.GameServer", "scripts-examples", name);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException(name);
    }

    [TestMethod]
    public void SandboxHasNoFilesOrOperatingSystem()
    {
        using var folder = new Folder();
        folder.Write("a.lua", """
            server.set(nil, "io", type(io)); server.set(nil, "os", type(os)); server.set(nil, "dofile", type(dofile))
            server.set(nil, "load", type(load)); server.set(nil, "require", type(require)); server.set(nil, "string", type(string.format))
            """);
        var (host, _, store) = Host(folder);
        foreach (var name in new[] { "io", "os", "dofile", "load", "require" })
        {
            Assert.IsTrue(store.TryGet(name, out var value));
            Assert.AreEqual("nil", value, name);
        }
        Assert.IsTrue(store.TryGet("string", out var format));
        Assert.AreEqual("function", format);
        Assert.IsNull(host.Files.Single().Error);
    }

    [TestMethod]
    public void EndlessLoopsAreStoppedAndTheServerGoesOn()
    {
        using var folder = new Folder();
        folder.Write("loop.lua", """
            server.on("join", function(p) while true do end end)
            server.on("join", function(p) server.broadcast("hello " .. p.name) end)
            server.on("leave", function(p) while true do pcall(function() while true do end end) end end)
            """);
        var (host, server, _) = Host(folder, budget: 100_000);
        var player = server.All[0];
        var watch = System.Diagnostics.Stopwatch.StartNew();
        host.FirePlayer("join", player);
        host.FirePlayer("leave", player);
        Assert.IsTrue(watch.ElapsedMilliseconds < 5000, $"took {watch.ElapsedMilliseconds} ms");
        CollectionAssert.AreEqual(new[] { "hello Alice" }, server.Said, "the next handler still runs");
        Assert.AreEqual(2, host.Errors);
        // A handler that fails 20 times in a row is switched off.
        for (var i = 0; i < 30; i++) host.FirePlayer("join", player);
        Assert.AreEqual(21, host.Errors);
        Assert.AreEqual(31, server.Said.Count);
    }

    [TestMethod]
    public void ExceptionsFromServerFunctionsAreContained()
    {
        using var folder = new Folder();
        folder.Write("a.lua", """
            server.on("join", function(p) server.broadcast("boom") end)
            server.on("join", function(p) server.message(p, "still here") end)
            server.every(1, function() server.broadcast("tick") end)
            server.endpoint("x", function() server.broadcast("x"); return "unreachable" end)
            server.on("chat", function(p, t) return server.get(p, "corrupt") + 1 end)
            """);
        var (host, server, store) = Host(folder);
        foreach (var failure in new Exception[] { new NullReferenceException("null"), new IndexOutOfRangeException("index"),
            new KeyNotFoundException("key"), new FormatException("format"), new NotImplementedException("todo") })
        {
            server.BroadcastFails = failure;
            host.FirePlayer("join", server.All[0]);                        // logged, the next handler still runs
            host.Pump(server.Now += 1500);                                 // timer
            Assert.AreEqual(500, host.HandleRequest("a", "x", [], false).Status);
        }
        Assert.AreEqual(5, server.Told.Count);
        Assert.IsTrue(host.Errors >= 15);
        store.SetHero(host.Uid("name:Alice"), null, "corrupt", "not a number");
        Assert.IsFalse(host.FireChat(server.All[0], "hi"), "a Lua error in a chat handler does not swallow or crash");
        server.BroadcastFails = null;
        host.FirePlayer("join", server.All[0]);
        CollectionAssert.Contains(server.Said, "boom");
    }

    [TestMethod]
    public void EveryInstalledModIsListedWithItsDescription()
    {
        using var folder = new Folder();
        folder.Write("a.lua", "-- Greets players. More text here.\n-- second line\nserver.on('join', function() end)");
        folder.Write("b.lua", "server.describe('Own words.')\nserver.endpoint('x', function() return 'x' end)");
        folder.Write("c.lua", "this is not lua");
        folder.Write("d.lua", "local x = 1");
        var (host, _, _) = Host(folder);
        var mods = host.Mods.ToDictionary(m => m.Mod);
        CollectionAssert.AreEquivalent(new[] { "a", "b", "c", "d" }, mods.Keys.ToArray(), "mods without pages or endpoints are listed too");
        Assert.AreEqual("Greets players.", mods["a"].Description);
        Assert.AreEqual("Own words.", mods["b"].Description);
        Assert.IsNotNull(mods["c"].Error);
        Assert.IsNull(mods["d"].Description);
        foreach (var example in new[] { "ladders.lua", "kill_milestones.lua", "loot_rewards.lua", "build_sharer.lua", "quest_helper.lua" })
        {
            folder.Write(example, Example(example));
        }
        host.Load();
        Assert.IsTrue(host.Mods.Where(m => m.Mod is "ladders" or "kill_milestones" or "loot_rewards" or "build_sharer" or "quest_helper").All(m => m.Description is { Length: > 10 } && m.Error is null));
    }

    [TestMethod]
    public void LoadErrorsAreReportedPerFile()
    {
        using var folder = new Folder();
        folder.Write("1-good.lua", "server.on('join', function(p) server.broadcast('ok') end)");
        folder.Write("2-bad.lua", "this is not lua");
        var (host, server, _) = Host(folder);
        Assert.IsNull(host.Files[0].Error);
        Assert.IsNotNull(host.Files[1].Error);
        host.FirePlayer("join", server.All[0]);
        CollectionAssert.AreEqual(new[] { "ok" }, server.Said);
    }

    [TestMethod]
    public void TimersStoreAndChatWork()
    {
        using var folder = new Folder();
        folder.Write("a.lua", """
            local ticks = 0
            server.every(1, function() ticks = ticks + 1; server.set(nil, "ticks", ticks) end)
            server.after(2.5, function() server.broadcast("once") end)
            server.on("chat", function(p, text) if text == "!me" then server.message(p, p.name .. " L" .. p.level .. " C" .. p.class); return true end end)
            server.on("kill", function(p, m) server.set(p, "last", m.type); server.add(p, "kills", 2); if m.level > 5 then server.kick(p, "test") end end)
            """);
        var (host, server, store) = Host(folder);
        for (server.Now = 0; server.Now <= 3100; server.Now += 100) host.Pump(server.Now);
        Assert.IsTrue(store.TryGet("ticks", out var ticks));
        Assert.AreEqual(3.0, ticks);
        CollectionAssert.AreEqual(new[] { "once" }, server.Said);
        var alice = server.All[0];
        Assert.IsTrue(host.FireChat(alice, "!me"));
        Assert.IsFalse(host.FireChat(alice, "hello"));
        Assert.AreEqual((1, "Alice L10 C2"), server.Told.Single());
        host.FireKill(alice, new ScriptMonster(4000, 551, 3));
        host.FireKill(alice, new ScriptMonster(4001, 552, 9));
        Assert.IsTrue(store.TryGetHero(host.Uid("name:Alice"), "kills", out var kills));
        Assert.AreEqual(4.0, kills);
        Assert.IsTrue(store.TryGetHero(host.Uid("name:Alice"), "last", out var last));
        Assert.AreEqual(552.0, last);
        CollectionAssert.AreEqual(new[] { 1 }, server.Kicked);
        // Each mod has its own file; values survive a restart (temporary file + move).
        host.FlushStores(wait: true);
        var file = Path.Combine(folder.Data, "a.json");
        Assert.IsTrue(File.Exists(file));
        Assert.IsFalse(File.Exists(file + ".tmp"));
        var reloaded = new ModStore(file);
        Assert.IsTrue(reloaded.TryGetHero(host.Uid("name:Alice"), "kills", out var again));
        Assert.AreEqual(4.0, again);
    }

    [TestMethod]
    public void ModsHaveTheirOwnStorageForLuaTables()
    {
        using var folder = new Folder();
        folder.Write("a.lua", """
            server.save("deaths", { { name = "Bob", level = 7, items = { "sword", "shield" } }, { name = "Cara", level = 9 } })
            server.save("config", { enabled = true, chance = 0.25, title = "A" })
            server.set(nil, "plain", 5)
            server.endpoint("read", function()
              local d = server.load("deaths")
              local c = server.load("config")
              return d[1].name .. d[1].items[2] .. d[2].level .. tostring(c.enabled) .. c.chance .. table.concat(server.keys(), ",") .. tostring(server.load("missing", "dflt"))
            end)
            server.endpoint("drop", function() server.delete("config"); return table.concat(server.keys(), ",") end)
            """);
        folder.Write("b.lua", """
            server.endpoint("read", function() return tostring(server.load("deaths")) .. "/" .. tostring(server.get(nil, "plain")) end)
            server.endpoint("bad", function() server.save("@x", 1) end)
            """);
        var (host, _, store) = Host(folder);
        Assert.AreEqual("Bobshield9true0.25config,deaths,plaindflt", host.HandleRequest("a", "read", [], false).Body);
        Assert.AreEqual("nil/nil", host.HandleRequest("b", "read", [], false).Body, "mod b cannot see mod a's data");
        Assert.AreEqual(500, host.HandleRequest("b", "bad", [], false).Status, "@ keys belong to the per-hero values");
        Assert.AreEqual("deaths,plain", host.HandleRequest("a", "drop", [], false).Body);
        host.FlushStores(wait: true);
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(folder.Data, "a.json")));
        Assert.AreEqual("shield", json.RootElement.GetProperty("deaths")[0].GetProperty("items")[1].GetString());
        // After a reload (and a restart) the tables come back.
        host.Load();
        Assert.AreEqual("Bobshield9", host.HandleRequest("a", "read", [], false).Body[..10]);
    }

    [TestMethod]
    public void LaddersModKeepsItsOwnDataAndServesItsDashboard()
    {
        using var folder = new Folder();
        folder.Write("ladders.lua", Example("ladders.lua"));
        var (host, server, store) = Host(folder, mod: "ladders");
        // Heroes from earlier sessions, in the mod's own file (names as the mod stores them: character only).
        store.SetHero(host.Uid("id:1:1"), "Bob", "kills", 30.0); store.SetHero(host.Uid("id:1:1"), null, "level", 20.0);
        store.SetHero(host.Uid("id:2:1"), "Cara", "kills", 70.0); store.SetHero(host.Uid("id:2:1"), null, "level", 5.0);
        var alice = server.All[0] with { Name = "acct.Alice", Character = "Alice" };   // level 10
        for (uint i = 0; i < 50; i++) host.FireKill(alice, new ScriptMonster(100 + i, 551, 3));
        for (server.Now = 0; server.Now <= 61_000; server.Now += 1000) host.Pump(server.Now);
        host.FireDeath(alice, null, new ScriptMonster(9000, 38, 12));
        var top = System.Text.Json.JsonDocument.Parse(host.HandleRequest("ladders", "top", [KeyValuePair.Create("board", "kills")], true).Body).RootElement;
        CollectionAssert.AreEqual(new[] { "Cara", "Alice", "Bob" }, top.EnumerateArray().Select(r => r.GetProperty("name").GetString()).ToArray());
        Assert.AreEqual(50, top[1].GetProperty("value").GetDouble());
        var level = System.Text.Json.JsonDocument.Parse(host.HandleRequest("ladders", "top", [KeyValuePair.Create("board", "level")], true).Body).RootElement;
        CollectionAssert.AreEqual(new[] { "Bob", "Alice", "Cara" }, level.EnumerateArray().Select(r => r.GetProperty("name").GetString()).ToArray());
        var online = System.Text.Json.JsonDocument.Parse(host.HandleRequest("ladders", "top", [KeyValuePair.Create("board", "online")], true).Body).RootElement;
        Assert.AreEqual(60.0 / 3600, online[0].GetProperty("value").GetDouble(), 1e-9);
        var page = host.HandleRequest("ladders", "", [], publicOnly: true);
        Assert.AreEqual(200, page.Status);
        Assert.IsTrue(page.Page);
        StringAssert.StartsWith(page.ContentType, "text/html");
        StringAssert.Contains(page.Body, "<b>Cara</b> 70");
        StringAssert.Contains(page.Body, "creature type 551: <b>Alice</b> (50)");
        StringAssert.Contains(page.Body, "<b>Alice</b> <small>level 10, by creature type 38</small>");
        Assert.IsFalse(page.Body.Contains("acct"), "no account names on the page");
        // A real game: the creature names come from the game (several types share one name), and a death names its killer.
        host.FireKill(alice, new ScriptMonster(7001, 551, 3, "Thief"));
        host.FireKill(alice, new ScriptMonster(7002, 552, 3, "Thief"));
        host.FireDeath(alice, null, new ScriptMonster(9001, 307, 12, "Brigand"));
        var named = host.HandleRequest("ladders", "", [], publicOnly: true).Body;
        Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(named, "Thief:").Count, "one line per creature name");
        StringAssert.Contains(named, "Thief: <b>Alice</b> (2)");
        StringAssert.Contains(named, "by Brigand</small>");
        Assert.IsTrue(host.FireChat(alice, "!top"));
        Assert.AreEqual("1. Cara: 70 kills", server.Told[0].Text);
    }

    [TestMethod]
    public void PagesEscapeNamesAndRenderTemplates()
    {
        using var folder = new Folder();
        folder.Write("evil.html", "<p>{{name}}</p><script>const data = {{json data}};</script>");
        folder.Write("p.lua", """
            local template = server.file("evil.html")
            server.page("", function() return server.render(template, { name = "<img src=x onerror=alert(1)>", data = { n = "</script><script>alert(2)</script>" } }) end)
            server.page("static", "<h1>static</h1>", { public = true })
            server.endpoint("file", function(r) return server.file(r.query.f) end)
            """);
        var (host, _, _) = Host(folder, mod: "p");
        var page = host.HandleRequest("p", "", [], false).Body;
        StringAssert.Contains(page, "&lt;img src=x onerror=alert(1)&gt;");
        Assert.IsFalse(page.Contains("</script><script>alert(2)"), "JSON in a script element cannot close it");
        Assert.AreEqual(404, host.HandleRequest("p", "", [], publicOnly: true).Status, "not public");
        var stat = host.HandleRequest("p", "static", [], publicOnly: true);
        Assert.IsTrue(stat.Page);
        Assert.AreEqual("<h1>static</h1>", stat.Body);
        foreach (var bad in new[] { "../p.lua", "p.lua", ".hidden.html", "sub/evil.html", "C:\\x.html", "missing.html" })
            Assert.AreEqual(500, host.HandleRequest("p", "file", [KeyValuePair.Create("f", bad)], false).Status, bad);
        Assert.AreEqual(200, host.HandleRequest("p", "file", [KeyValuePair.Create("f", "evil.html")], false).Status);
    }

    [TestMethod]
    public void ScriptEndpointsAnswerCsvJsonAndErrors()
    {
        using var folder = new Folder();
        folder.Write("ladders.lua", Example("ladders.lua"));
        folder.Write("other.lua", """
            server.endpoint("/Boom/", function() error("broken") end)
            server.endpoint("secret", function(r) return { a = r.query.x, list = { 1, 2 }, empty = {} } end)
            """);
        var (host, server, store) = Host(folder, mod: "ladders");
        var alice = server.All[0];
        store.SetHero(host.Uid("id:9:1"), "Bob", "level", 3.0);
        for (uint i = 0; i < 3; i++) host.FireKill(alice, new ScriptMonster(100 + i, 551, 3));
        host.FireKill(alice, new ScriptMonster(200, 77, 3));
        var csv = host.HandleRequest("ladders", "by-creature", [], publicOnly: true);
        Assert.AreEqual(200, csv.Status);
        StringAssert.StartsWith(csv.ContentType, "text/csv");
        Assert.AreEqual("creature,hunter,kills\r\ncreature type 551,Alice,3\r\ncreature type 77,Alice,1\r\n", csv.Body);
        var top = host.HandleRequest("LADDERS", "/top/", [KeyValuePair.Create("board", "kills"), KeyValuePair.Create("n", "1")], publicOnly: true);
        Assert.AreEqual("[{\"rank\":1,\"name\":\"Alice\",\"value\":4}]", top.Body);
        StringAssert.StartsWith(top.ContentType, "application/json");
        Assert.AreEqual(404, host.HandleRequest("ladders", "top", [KeyValuePair.Create("board", "nope")], false).Status);
        Assert.AreEqual(404, host.HandleRequest("ladders", "missing", [], false).Status);
        Assert.AreEqual(500, host.HandleRequest("other", "boom", [], false).Status);
        Assert.AreEqual(404, host.HandleRequest("other", "secret", [], publicOnly: true).Status, "not public");
        var json = host.HandleRequest("other", "secret", [KeyValuePair.Create("x", "1")], false);
        using var doc = System.Text.Json.JsonDocument.Parse(json.Body);
        Assert.AreEqual("1", doc.RootElement.GetProperty("a").GetString());
        Assert.AreEqual(2, doc.RootElement.GetProperty("list").GetArrayLength());
        Assert.AreEqual(System.Text.Json.JsonValueKind.Array, doc.RootElement.GetProperty("empty").ValueKind);
        CollectionAssert.AreEquivalent(new[] { "ladders/top", "ladders/by-creature", "other/boom", "other/secret" },
            host.Endpoints.Where(e => !e.Page).Select(e => e.Mod + "/" + e.Path).ToArray());
    }

    [TestMethod]
    public void EndpointAnswersAreCappedAndOnlyUseSafeStatusesAndTypes()
    {
        using var folder = new Folder();
        folder.Write("m.lua", """
            server.endpoint("big", function() return string.rep("x", 3 * 1024 * 1024) end)
            server.endpoint("concat", function() local s = string.rep("y", 1024 * 1024); return s .. s .. s end)
            server.endpoint("table", function() local t = {} for i = 1, 300000 do t[i] = "0123456789" end return t end)
            server.endpoint("ok", function() return string.rep("z", 1000) end)
            server.endpoint("status", function(r) return "s", "text/plain", tonumber(r.query.s) end)
            server.endpoint("type", function(r) return "<b>x</b>", r.query.t end)
            server.endpoint("empty", function() return "body", "text/plain", 204 end)
            """);
        var options = new ScriptOptions { Directory = folder.Scripts, DataDirectory = folder.Data, MaxResponseChars = 2 * 1024 * 1024 };
        var server = new FakeServer();
        var host = new ScriptHost(options, server);
        host.Load();
        foreach (var path in new[] { "big", "concat", "table" })
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var answer = host.HandleRequest("m", path, [], false);
            Assert.IsTrue(watch.ElapsedMilliseconds < 5000, $"{path} took {watch.ElapsedMilliseconds} ms");
            Assert.AreEqual(500, answer.Status, path);
            Assert.IsTrue(answer.Body.Length < 100, path + ": no huge body");
        }
        Assert.AreEqual(1000, host.HandleRequest("m", "ok", [], false).Body.Length);
        foreach (var (asked, expected) in new[] { ("200", 200), ("404", 404), ("499", 499), ("500", 500), ("302", 302), ("100", 500), ("599", 500), ("12.5", 500) })
            Assert.AreEqual(expected, host.HandleRequest("m", "status", [KeyValuePair.Create("s", asked)], false).Status, asked);
        Assert.AreEqual("script returned an invalid status", host.HandleRequest("m", "status", [KeyValuePair.Create("s", "503")], false).Body);
        foreach (var (asked, expected) in new[] { ("text/csv", "text/csv"), ("text/html", "text/html"), ("application/json", "application/json"),
                     ("application/javascript", "text/plain"), ("image/svg+xml", "text/plain"), ("text/html\r\nX-Evil: 1", "text/plain"), ("TEXT/CSV; charset=latin1", "text/csv") })
            Assert.AreEqual(expected + "; charset=utf-8", host.HandleRequest("m", "type", [KeyValuePair.Create("t", asked)], false).ContentType, asked);
        var empty = host.HandleRequest("m", "empty", [], false);
        Assert.AreEqual(204, empty.Status);
        Assert.AreEqual("", empty.Body);
        // string.rep is capped where it builds the string; the error is contained.
        folder.Write("m.lua", "server.endpoint('rep', function() return string.rep('x', 300 * 1024 * 1024) end)");
        host.Load();
        Assert.AreEqual(500, host.HandleRequest("m", "rep", [], false).Status);
    }

    [TestMethod]
    public void EndpointRequestsRunOnTheGameLoop()
    {
        using var folder = new Folder();
        folder.Write("hello.lua", "server.endpoint('players', function() return #server.players() .. ' online' end, { public = true })");
        using var server = new Server(folder);
        using var a = RawClient.Admit(server.Port, "A", 1);
        var answer = server.Host.ModRequest("hello", "players", [], true, CancellationToken.None).GetAwaiter().GetResult();
        Assert.AreEqual(200, answer.Status);
        Assert.AreEqual("1 online", answer.Body);
        Assert.AreEqual("hello", server.Host.ModEndpoints().Single().Mod);
    }

    [TestMethod]
    public void LaddersNeverShowOwnerKeys()
    {
        var store = new ModStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));
        store.SetHero("0123abcd", null, "kills", 9.0);
        store.SetHero("4567ef01", "Cara", "kills", 5.0);
        store.SetHero("89ab2345", "id:4711:2", "kills", 1.0);
        CollectionAssert.AreEqual(new[] { ModStore.UnknownName, "Cara", ModStore.UnknownName }, store.Top("kills", 10).Select(t => t.Name).ToArray());
        foreach (var (raw, shown) in new[] { ("id:4711:2", ModStore.UnknownName), ("name:acct.Dora", ModStore.UnknownName), ("Mr.X", "Mr.X"), ("Finn", "Finn"), ("@x", ModStore.UnknownName) })
            Assert.AreEqual(shown, ModStore.DisplayName(raw), raw);
    }

    [TestMethod]
    public void PlayerTablesHoldNoLobbyIdOwnerKeyOrAccount()
    {
        using var folder = new Folder();
        folder.Write("a.lua", """
            server.on("join", function(p)
              local fields = {}
              for k, v in pairs(p) do fields[#fields + 1] = k .. "=" .. tostring(v) end
              table.sort(fields)
              server.broadcast(table.concat(fields, ";"))
            end)
            """);
        var (host, server, _) = Host(folder);
        var lobbyPlayer = new ScriptPlayer(3, "secretaccount.Hero", "id:4711:2", 1, 20, true, false, 4711, 12, Character: "Hero");
        host.FirePlayer("join", lobbyPlayer);
        var line = server.Said.Single();
        foreach (var leak in new[] { "4711", "secretaccount", "id:", "name:" })
            Assert.IsFalse(line.Contains(leak), $"'{leak}' in {line}");
        Assert.IsFalse(line.Split(';').Any(f => f.StartsWith("id=", StringComparison.Ordinal) || f.StartsWith("key=", StringComparison.Ordinal)), line);
        StringAssert.Contains(line, "character=Hero");
        StringAssert.Contains(line, "name=Hero");
        var uid = host.Uid("id:4711:2");
        StringAssert.Contains(line, "uid=" + uid);
        Assert.AreEqual(32, uid.Length);
        Assert.IsTrue(File.Exists(Path.Combine(folder.Data, ".uid-secret")) && File.Exists(Path.Combine(folder.Data, ".gitignore")));
        if (!OperatingSystem.IsWindows()) Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(folder.Data, ".uid-secret")));
        // Stable on this server (the secret is kept in the data folder), different on another one.
        var again = new ScriptHost(new ScriptOptions { Directory = folder.Scripts, DataDirectory = folder.Data }, server);
        Assert.AreEqual(uid, again.Uid("id:4711:2"));
        using var other = new Folder();
        Assert.AreNotEqual(uid, new ScriptHost(new ScriptOptions { Directory = other.Scripts, DataDirectory = other.Data }, server).Uid("id:4711:2"));
        // A LAN name keeps its dots.
        host.FirePlayer("join", new ScriptPlayer(4, "Mr.X", "name:Mr.X", 1, 1, true, false, 0, Character: "Mr.X"));
        StringAssert.Contains(server.Said[1], "character=Mr.X");
    }

    [TestMethod]
    public void ModStorageIsCappedPerModAndInTotal()
    {
        using var folder = new Folder();
        folder.Write("a.lua", """
            server.endpoint("save", function(r) server.save(r.query.k, string.rep("x", tonumber(r.query.n))) return "ok" end)
            server.endpoint("hero", function(r) local p = { uid = "u" .. r.query.k, character = "H" } server.set(p, "v", string.rep("y", tonumber(r.query.n))) return "ok" end)
            server.endpoint("add", function(r) local p = { uid = "u1", character = "H" } return tostring(server.add(p, "n", 1)) end)
            """);
        folder.Write("b.lua", "server.endpoint('save', function(r) server.save(r.query.k, string.rep('z', tonumber(r.query.n))) return 'ok' end)");
        var options = new ScriptOptions { Directory = folder.Scripts, DataDirectory = folder.Data, MaxModStoreChars = 300_000, MaxTotalStoreChars = 500_000, MaxStoreChars = 1_000_000 };
        var host = new ScriptHost(options, new FakeServer());
        host.Load();
        ScriptResponse Call(string mod, string path, string k, int n) =>
            host.HandleRequest(mod, path, [KeyValuePair.Create("k", k), KeyValuePair.Create("n", n.ToString(System.Globalization.CultureInfo.InvariantCulture))], false);
        Assert.AreEqual(200, Call("a", "save", "one", 200_000).Status);
        Assert.AreEqual(500, Call("a", "save", "two", 200_000).Status, "past the mod's limit");
        Assert.AreEqual(500, Call("a", "hero", "1", 200_000).Status, "per-hero values count too");
        Assert.AreEqual(200, Call("a", "save", "one", 100_000).Status, "shrinking is always allowed");
        Assert.AreEqual(200, Call("b", "save", "one", 250_000).Status);
        Assert.AreEqual(500, Call("b", "save", "two", 200_000).Status, "past the total of all mods");
        Assert.IsTrue(host.StoreOf("a").Size < 300_000 && host.StoreOf("b").Size < 300_000);
        Assert.IsFalse(host.StoreOf("a").TryGet("two", out _), "a refused save changes nothing");
        Assert.AreEqual("1", host.HandleRequest("a", "add", [], false).Body);
        // Writing happens off the game loop; at shutdown it is awaited.
        host.FlushStores(wait: true);
        Assert.IsTrue(new FileInfo(Path.Combine(folder.Data, "a.json")).Length < 300_000);
        StringAssert.Contains(File.ReadAllText(Path.Combine(folder.Data, ".gitignore")), "*", "the data folder keeps itself out of git");
    }

    [TestMethod]
    public void ScriptsSeeTheCharacterName()
    {
        using var folder = new Folder();
        folder.Write("a.lua", "server.on('join', function(p) server.broadcast(p.character) end)");
        var (host, server, _) = Host(folder);
        host.FirePlayer("join", server.All[0] with { Name = "acct.Alice", Character = "Alice" });
        CollectionAssert.AreEqual(new[] { "Alice" }, server.Said);
    }

    [TestMethod]
    public void ChangedScriptsAreReloaded()
    {
        using var folder = new Folder();
        folder.Write("a.lua", "server.on('join', function(p) server.broadcast('v1') end)");
        var (host, server, _) = Host(folder);
        host.FirePlayer("join", server.All[0]);
        folder.Write("a.lua", "server.on('join', function(p) server.broadcast('v2') end)");
        host.RequestReload();
        host.Pump(0); host.Pump(600);
        host.FirePlayer("join", server.All[0]);
        CollectionAssert.AreEqual(new[] { "v1", "v2" }, server.Said);
    }

    [TestMethod]
    public void KillMilestoneExampleAnnouncesAtFifty()
    {
        using var folder = new Folder();
        folder.Write("kill_milestones.lua", Example("kill_milestones.lua"));
        var (host, server, _) = Host(folder);
        var alice = server.All[0];
        for (uint i = 0; i < 49; i++) host.FireKill(alice, new ScriptMonster(5000 + i, 551, 3));
        Assert.AreEqual(0, server.Said.Count);
        host.FireKill(alice, new ScriptMonster(6000, 551, 3));
        CollectionAssert.AreEqual(new[] { "Alice has defeated 50 creatures of type 551!" }, server.Said);
        Assert.IsTrue(host.FireChat(alice, "!kills"));
        Assert.AreEqual("You have defeated 50 creatures.", server.Told.Last().Text);
    }

    [TestMethod]
    public void KillMilestoneExampleUsesTheGameName()
    {
        using var folder = new Folder();
        folder.Write("kill_milestones.lua", Example("kill_milestones.lua"));
        var (host, server, _) = Host(folder);
        for (uint i = 0; i < 50; i++) host.FireKill(server.All[0], new ScriptMonster(5000 + i, 38, 3, "Orc Warrior"));
        CollectionAssert.AreEqual(new[] { "Alice has defeated Orc Warrior 50 times!" }, server.Said);
    }

    [TestMethod]
    public void ScriptsSeeCreatureAndItemNames()
    {
        using var folder = new Folder();
        folder.Write("a.lua", """
            server.on("kill", function(p, m)
              server.broadcast(tostring(m.name) .. "|" .. tostring(server.creature_name(m.type)) .. "|" .. tostring(server.creature_name(m)) .. "|" ..
                tostring(server.item_name(2501)) .. "|" .. tostring(server.item_name(1)) .. "|" .. tostring(server.creature_name("x")))
            end)
            """);
        var (host, server, _) = Host(folder);
        host.FireKill(server.All[0], new ScriptMonster(1, 38, 3, "Orc Warrior"));
        host.FireKill(server.All[0], new ScriptMonster(2, 39, 3));
        CollectionAssert.AreEqual(new[] { "Orc Warrior|Orc Warrior|Orc Warrior|Longbow|nil|nil", "nil|nil|nil|Longbow|nil|nil" }, server.Said);
    }

    private static string? GameDirectory()
    {
        var dir = Environment.GetEnvironmentVariable("SACRED_GAME_DIR") ?? @"B:\Spiele\GOG Games\sacred gold GOG";
        return Directory.Exists(dir) ? dir : null;
    }

    [TestMethod]
    public void LootIsGeneratedForClassLevelAndQuality()
    {
        if (GameDirectory() is not { } dir) { Assert.Inconclusive("no game folder (SACRED_GAME_DIR)"); return; }
        var loot = new LootService(dir);
        var item = loot.Roll(20, 1, 0, 6, Sacred.Core.Pak.Items.SacredItemCategory.Ring, 12345, out var error);
        Assert.IsNotNull(item, error);
        Assert.AreEqual(130, item.Values.Length);
        Assert.AreEqual(Sacred.Core.Pak.Items.SacredItemCategory.Ring, item.Category);
        Assert.AreEqual(item.Type, BinaryPrimitives.ReadUInt32LittleEndian(item.Values), "the object type is the value block's resource");
        Assert.IsTrue(item.Row > 0);
        var same = loot.Roll(20, 1, 0, 6, Sacred.Core.Pak.Items.SacredItemCategory.Ring, 12345, out _);
        CollectionAssert.AreEqual(item.Values, same!.Values, "a seed repeats the roll");
        Assert.IsNull(new LootService(null).Roll(20, 1, 0, null, null, 1, out var missing));
        StringAssert.Contains(missing, "--game-dir");
    }

    [TestMethod]
    public void DropItemOptionsReachTheServer()
    {
        using var folder = new Folder();
        folder.Write("a.lua", """
            server.on("chat", function(p, text)
              local item, err = server.drop_item(p, { quality = 7, category = "ring", level = 33 })
              server.message(p, item.name .. " q" .. item.quality)
              local _, bad = server.drop_item(p, { category = "spoon" })
              server.message(p, bad)
              return true
            end)
            """);
        var (host, server, _) = Host(folder);
        Assert.IsTrue(host.FireChat(server.All[0], "!loot"));
        var request = server.Drops.Single();
        Assert.AreEqual(33, request.Level); Assert.AreEqual(2, request.HeroClass); Assert.AreEqual(7u, request.Quality);
        Assert.AreEqual(Sacred.Core.Pak.Items.SacredItemCategory.Ring, request.Category);
        CollectionAssert.AreEqual(new[] { "Test ring q7", "unknown category spoon" }, server.Told.Select(t => t.Text).ToArray());
    }

    // ---- through the real server -------------------------------------------------------------------------------------

    private sealed class Server : IDisposable
    {
        public readonly GameServerHost Host;
        private readonly Thread _thread;
        public Server(Folder folder, string? gameDirectory = null)
        {
            var config = new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5, GameDirectory = gameDirectory };
            config.Scripts.Directory = folder.Scripts; config.Scripts.DataDirectory = folder.Data;
            Host = new GameServerHost(config, new AdmissionRules(null), null);
            Host.StartScripts();
            _thread = new Thread(Host.Run) { IsBackground = true };
            _thread.Start();
        }
        public int Port => Host.TcpPort;
        public void Dispose() { Host.Stop(); _thread.Join(); Host.Dispose(); }
    }

    private static byte[] Word(uint value) { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, value); return b; }
    private static byte[] Chat(string text) { var b = new byte[4 + text.Length * 2 + 2]; Encoding.Unicode.GetBytes(text, b.AsSpan(4)); return b; }
    private static string ChatText(byte[] payload)
    {
        var text = Encoding.Unicode.GetString(payload, 4, payload.Length - 4);
        var end = text.IndexOf('\0');
        return end < 0 ? text : text[..end];
    }

    [TestMethod]
    public void KillsChatAndJoinRunThroughTheGameServer()
    {
        using var folder = new Folder();
        folder.Write("kill_milestones.lua", Example("kill_milestones.lua").Replace("[50] = true", "[3] = true"));
        using var server = new Server(folder);
        var host = RawClient.Admit(server.Port, "Host", 1);
        host.Send(0x016, default); host.Send(0x018, Word(0x1964)); host.WaitFor(0x018);
        Assert.AreEqual("Welcome, Host! Type !kills to see your kill count.", ChatText(host.WaitFor(0x003).Payload));
        var joiner = RawClient.Admit(server.Port, "Joiner", 2);
        joiner.Send(0x015, default); joiner.Send(0x017, default); joiner.WaitFor(0x017);
        StringAssert.StartsWith(ChatText(joiner.WaitFor(0x003).Payload), "Welcome, Joiner!");
        using (host) using (joiner)
        {
            // Three creatures die by the joiner's hand (one reported twice).
            foreach (var victim in new uint[] { 9001, 9002, 9002, 9003 })
            {
                var death = new byte[8];
                BinaryPrimitives.WriteUInt32LittleEndian(death, victim);
                BinaryPrimitives.WriteUInt32LittleEndian(death.AsSpan(4), joiner.Slot);
                joiner.Send(0x17D, death);
            }
            var announce = host.WaitFor(0x005);
            Assert.AreEqual((byte)0, announce.Origin);
            Assert.AreEqual("Joiner has defeated 3 creatures of type 0!", ChatText(announce.Payload));
            // "!kills" is answered privately and not relayed.
            joiner.Send(0x005, Chat("!kills"));
            Assert.AreEqual("You have defeated 3 creatures.", ChatText(joiner.WaitFor(0x003).Payload));
            Assert.IsTrue(host.Silent(0x005));
            joiner.Send(0x005, Chat("hi"));
            Assert.AreEqual("hi", ChatText(host.WaitFor(0x005).Payload));
        }
    }

    [TestMethod]
    public void DroppedLootAppearsNextToTheHeroForEverybody()
    {
        if (GameDirectory() is not { } dir) { Assert.Inconclusive("no game folder (SACRED_GAME_DIR)"); return; }
        using var folder = new Folder();
        folder.Write("a.lua", "server.on('chat', function(p, t) local item, err = server.drop_item(p, { quality = 5, category = 'ring', level = 12, class = 1 }); server.message(p, item and item.name or err); return true end)");
        using var server = new Server(folder, dir);
        var host = RawClient.Admit(server.Port, "Host", 1);
        host.Send(0x016, default); host.Send(0x018, Word(0x1964)); host.WaitFor(0x018);
        var joiner = RawClient.Admit(server.Port, "Joiner", 2);
        joiner.Send(0x015, default); joiner.Send(0x017, default); joiner.WaitFor(0x017);
        using (host) using (joiner)
        {
            // The host's hero stands at (5000, 7000) in sector 1234 (a 0x169 state with the 24-byte prefix).
            var hero = new byte[24 + 64];
            BinaryPrimitives.WriteUInt32LittleEndian(hero, host.Slot);
            BinaryPrimitives.WriteInt32LittleEndian(hero.AsSpan(8), 5000);
            BinaryPrimitives.WriteInt32LittleEndian(hero.AsSpan(12), 7000);
            BinaryPrimitives.WriteUInt32LittleEndian(hero.AsSpan(20), 1234);
            host.Send(0x169, hero);
            host.Send(0x005, Chat("!loot"));
            foreach (var client in new[] { joiner, host })
            {
                var state = client.WaitFor(0x16F).Payload;
                var reference = BinaryPrimitives.ReadUInt32LittleEndian(state);
                Assert.IsTrue(reference >= 0x11, "a server reference");
                Assert.AreEqual(5000, BinaryPrimitives.ReadInt32LittleEndian(state.AsSpan(8)));
                Assert.AreEqual(7000, BinaryPrimitives.ReadInt32LittleEndian(state.AsSpan(12)));
                Assert.AreEqual(1234u, BinaryPrimitives.ReadUInt32LittleEndian(state.AsSpan(20)));
                var item = Sacred.Network.Gameplay.GoldItemState.FromPayload(state.AsSpan(24));
                Assert.AreEqual(host.Slot, item.PhysicalState.BaseState.Owner, "the receiving player owns the item");
                Assert.AreEqual(BinaryPrimitives.ReadUInt32LittleEndian(state.AsSpan(4)), item.Instance.ItemType);
                Assert.AreEqual(0, item.Count);
            }
            Assert.IsFalse(string.IsNullOrEmpty(ChatText(host.WaitFor(0x003).Payload)), "the script told the player the item's name");
        }
    }

    [TestMethod]
    public void DropGoldAmountsReachTheServer()
    {
        using var folder = new Folder();
        folder.Write("a.lua", """
            server.on("chat", function(p, text)
              local pile = server.drop_gold(p, 1234.7)
              server.message(p, pile.ref .. " " .. pile.type .. " " .. pile.amount)
              local _, zero = server.drop_gold(p, 0)
              local _, missing = server.drop_gold(p)
              local _, nobody = server.drop_gold(9, 10)
              server.message(p, zero .. "|" .. missing .. "|" .. nobody)
              server.drop_gold(p, 1e12)
              return true
            end)
            """);
        var (host, server, _) = Host(folder);
        Assert.IsTrue(host.FireChat(server.All[0], "!gold"));
        CollectionAssert.AreEqual(new[] { 1234u, GoldPile.MaximumAmount }, server.Gold, "whole gold, capped at the game's limit");
        CollectionAssert.AreEqual(new[] { $"101 {0x140F} 1234", "amount must be at least 1|amount must be at least 1|no such player" },
            server.Told.Select(t => t.Text).ToArray());
    }

    [TestMethod]
    public void DroppedGoldAppearsNextToTheHeroForEverybody()
    {
        using var folder = new Folder();
        folder.Write("a.lua", "server.on('chat', function(p, t) local pile, err = server.drop_gold(p, 777); server.message(p, pile and tostring(pile.amount) or err); return true end)");
        using var server = new Server(folder);
        var host = RawClient.Admit(server.Port, "Host", 1);
        host.Send(0x016, default); host.Send(0x018, Word(0x1964)); host.WaitFor(0x018);
        var joiner = RawClient.Admit(server.Port, "Joiner", 2);
        joiner.Send(0x015, default); joiner.Send(0x017, default); joiner.WaitFor(0x017);
        using (host) using (joiner)
        {
            var hero = new byte[24 + 64];
            BinaryPrimitives.WriteUInt32LittleEndian(hero, host.Slot);
            BinaryPrimitives.WriteInt32LittleEndian(hero.AsSpan(8), 5000);
            BinaryPrimitives.WriteInt32LittleEndian(hero.AsSpan(12), 7000);
            BinaryPrimitives.WriteUInt32LittleEndian(hero.AsSpan(20), 1234);
            host.Send(0x169, hero);
            host.Send(0x005, Chat("!gold"));
            foreach (var client in new[] { joiner, host })
            {
                var state = client.WaitFor(0x16F).Payload;
                Assert.IsTrue(BinaryPrimitives.ReadUInt32LittleEndian(state) >= 0x11, "a server reference");
                Assert.AreEqual(GoldPile.LargeResource, BinaryPrimitives.ReadUInt32LittleEndian(state.AsSpan(4)), "777 gold is the 350..999 pile");
                Assert.AreEqual(5000, BinaryPrimitives.ReadInt32LittleEndian(state.AsSpan(8)));
                Assert.AreEqual(7000, BinaryPrimitives.ReadInt32LittleEndian(state.AsSpan(12)));
                Assert.AreEqual(1234u, BinaryPrimitives.ReadUInt32LittleEndian(state.AsSpan(20)));
                var item = Sacred.Network.Gameplay.GoldItemState.FromPayload(state.AsSpan(24));
                Assert.AreEqual(host.Slot, item.PhysicalState.BaseState.Owner, "the receiving player owns the pile");
                Assert.AreEqual(GoldPile.LargeResource, item.PhysicalState.BaseState.ObjectType);
                Assert.AreEqual(GoldPile.LargeResource, item.Instance.ItemType);
                Assert.AreEqual(777u, item.Instance.Price, "the amount is the value block's price");
                Assert.AreEqual(0, item.Count);
                Assert.IsFalse(item.HasWeaponExtension);
                Assert.AreEqual(0, item.RemainingData.Length);
            }
            Assert.AreEqual("777", ChatText(host.WaitFor(0x003).Payload));
        }
    }

    [TestMethod]
    public void PlayerTableCarriesTheSectorAndTheSectorEventFires()
    {
        using var folder = new Folder();
        folder.Write("a.lua", """
            server.on("sector", function(p, sector) server.broadcast("sector " .. sector .. "/" .. p.sector .. " " .. p.name) end)
            server.on("join", function(p) server.broadcast("join " .. p.sector) end)
            """);
        var (host, server, _) = Host(folder);
        host.FirePlayer("join", server.All[0]);
        host.FireSector(server.All[0] with { Sector = 2909 });
        CollectionAssert.AreEqual(new[] { "join 0", "sector 2909/2909 Alice" }, server.Said);
    }

    [TestMethod]
    public void SectorEventRunsThroughTheGameServerOnlyWhenTheSectorChanges()
    {
        using var folder = new Folder();
        folder.Write("a.lua", """
            server.on("sector", function(p, sector) server.message(p, "sector " .. sector .. "/" .. p.sector) end)
            """);
        using var server = new Server(folder);
        using var host = RawClient.Admit(server.Port, "Host", 1);
        host.WaitFor(0x153);
        var entry = Convert.FromHexString("5d0b00001f0b200b210b5e0b9d0b9c0b9b0b5c0b5d0b" + new string('0', 36 * 2) + "7c080000");
        host.Send(0x148, entry);
        host.WaitFor(0x148);
        Assert.AreEqual("sector 2909/2909", ChatText(host.WaitFor(0x003).Payload));   // 0x0B5D
        // The same sector again is no change: no second event.
        host.Send(0x148, entry);
        host.WaitFor(0x148);
        Assert.IsTrue(host.Silent(0x003));
    }
}
