using System.Text;
using System.Text.Json;
using Sacred.GameServer.Scripting;
using Sacred.GameServer.Web;
using Sacred.GameServer.Wire;

namespace Sacred.GameServer.Tests;

/// <summary>The example mods <c>build_sharer.lua</c> and <c>quest_helper.lua</c> and the <c>server.hero</c> function they rely on, against a
/// fake server (no sockets): the net channel carries the requests, the data comes from a scripted hero.</summary>
[TestClass]
public class ModScriptTests
{
    private sealed class Folder : IDisposable
    {
        public readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sacred-mods-" + Guid.NewGuid().ToString("N"));
        public string Scripts => System.IO.Path.Combine(Path, "scripts");
        public string Data => System.IO.Path.Combine(Path, "data");
        public Folder() { Directory.CreateDirectory(Scripts); }
        public void Write(string name, string source) { File.WriteAllText(System.IO.Path.Combine(Scripts, name), source); ExampleAssets.CopyTo(Scripts); }
        public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
    }

    private sealed class FakeServer : IScriptServer
    {
        public long Now;
        public readonly List<ScriptPlayer> All = new();
        public readonly Dictionary<int, ScriptHero> Heroes = new();
        public readonly List<(int Slot, string Text)> Told = new();
        public readonly List<(int Slot, JsonElement Body)> Sent = new();
        public long NowMs => Now;
        public IReadOnlyList<ScriptPlayer> Players() => All;
        public ScriptPlayer? Player(int slot) => All.FirstOrDefault(p => p.Slot == slot);
        public void Broadcast(string text) { }
        public void Message(int slot, string text) => Told.Add((slot, text));
        public void Kick(int slot, string reason) { }
        public ScriptDrop? DropItem(int slot, in ScriptLootRequest request, out string? error) { error = "no"; return null; }
        public ScriptDrop? DropGold(int slot, uint amount, out string? error) { error = "no"; return null; }
        public ScriptHero? Hero(int slot) => Heroes.GetValueOrDefault(slot);
        public uint NetRegister(string name, bool serverOnly) => NetOpsWire.ChannelId(name);
        public bool NetSupports(int slot) => true;
        public int NetSend(int slot, uint channel, bool json, byte[] data, out string? error)
        {
            error = null;
            Assert.IsTrue(json, "the mods send tables");
            Sent.Add((slot, JsonDocument.Parse(data).RootElement.Clone()));
            return 1;
        }
    }

    private static ScriptPlayer Player(int slot, string name, int level = 10, int cls = 1) =>
        new(slot, name, "name:" + name, cls, level, true, slot == 1, 0, 0, name);

    private static HeroInfo HeroOf(int slot, string name, int level = 40, int cls = 3) => new(slot, name, null, cls, level, 123456, 999, 2,
        [new SkillInfo(2, 12, 3, "Weapon Lore"), new SkillInfo(20, 7, 0, "Vampirism")], [10, 5, 0, 0, 0, 0], [30, 25, 20, 15, 10, 5],
        [new ArtInfo(1, 60, 4, "Blazing Disc")], 1,
        [new EquippedItem(0, new ItemInfo(1, 2501, 30, 13, 2, 500, "Decaying Bow of Oblivion")), new EquippedItem(3, new ItemInfo(2, 1966, 28, 3, 0, 100, null))],
        [new StoredItem(0, 1, 1, new ItemInfo(3, 5171, 1, 0, 0, 5, "Lesser Healing Potion"))], [], true, 60);

    private static ScriptHero Quests(HeroInfo hero, params (uint Key, uint Value)[] pairs) => new(hero, pairs);

    private static (ScriptHost Host, FakeServer Server, Folder Folder) Start(string script, string file, Action<ScriptHost>? beforeLoad = null, Folder? folder = null)
    {
        folder ??= new Folder();
        folder.Write(file + ".lua", File.ReadAllText(ExamplePath(file + ".lua")));
        var server = new FakeServer();
        var host = new ScriptHost(new ScriptOptions { Directory = folder.Scripts, DataDirectory = folder.Data }, server);
        beforeLoad?.Invoke(host);
        host.Load();
        Assert.IsNull(host.Files.Single().Error, host.Files.Single().Error);
        return (host, server, folder);
    }

    private static string ExamplePath(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Sacred.GameServer", "scripts-examples", name);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(name);
    }

    private static void Say(ScriptHost host, ScriptPlayer player, string channel, string json) =>
        Assert.IsTrue(host.FireNet(NetOpsWire.ChannelId(channel), player, true, Encoding.UTF8.GetBytes(json)), "a handler is listening on " + channel);

    private static JsonElement LastReply(FakeServer server, int slot)
    {
        var reply = server.Sent.Last(s => s.Slot == slot).Body;
        return reply;
    }

    private static ScriptResponse Get(ScriptHost host, string mod, string path, params (string Key, string Value)[] query) =>
        host.HandleRequest(mod, path, query.Select(q => new KeyValuePair<string, string>(q.Key, q.Value)).ToList(), publicOnly: true);

    // ---- server.hero ---------------------------------------------------------------------------------------------------------

    [TestMethod]
    public void ServerHeroGivesTheServersViewAndNilWithoutHeroData()
    {
        using var folder = new Folder();
        folder.Write("a.lua", """
            server.on("chat", function(p, text)
              local h, why = server.hero(p)
              if not h then server.message(p, "none: " .. why); return true end
              local function s(v) if type(v) == "number" then return string.format("%.0f", v) end return tostring(v) end
              local parts = { h.character, h.class, h.level, #h.skills, h.skills[1].name, h.skills[2].level, #h.equipment, h.equipment[1].name, h.equipment[2].name,
                h.arts[1].name, #h.quest_pairs, h.quest_pairs[2].value, h.attributes[1], h.attribute_points[2], h.inventory_items, h.account, h.uid }
              for i = 1, 17 do parts[i] = s(parts[i]) end
              server.message(p, table.concat(parts, "|"))
              return true
            end)
            server.on("join", function(p) assert(select(2, server.hero(nil)) ~= nil) end)
            """);
        var server = new FakeServer();
        var host = new ScriptHost(new ScriptOptions { Directory = folder.Scripts, DataDirectory = folder.Data }, server);
        host.Load();
        var alice = Player(1, "Alice");
        server.All.Add(alice);
        host.FireChat(alice, "x");
        StringAssert.StartsWith(server.Told[0].Text, "none: no hero data");
        server.Heroes[1] = Quests(HeroOf(1, "Alice"), (7, 1), (9, 4000000000));
        host.FireChat(alice, "x");
        Assert.AreEqual("Alice|3|40|2|Weapon Lore|7|2|Decaying Bow of Oblivion|nil|Blazing Disc|2|4000000000|30|5|1|nil|nil", server.Told[1].Text);
        host.FirePlayer("join", alice);
        Assert.AreEqual(0, host.Errors);
    }

    // ---- build_sharer ----------------------------------------------------------------------------------------------------------

    private static (ScriptHost Host, FakeServer Server, Folder Folder) Sharer(Folder? folder = null) =>
        Start("", "build_sharer", h => h.StoreOf("build_sharer").Set("publish_cooldown", 0.0), folder);

    [TestMethod]
    public void BuildIsPublishedFromTheServersOwnHeroDataAndServedWithoutOwnerIds()
    {
        var (host, server, folder) = Sharer();
        using var keep = folder;
        var alice = Player(1, "Alice"); server.All.Add(alice);
        server.Heroes[1] = Quests(HeroOf(1, "Alice", level: 41, cls: 3));
        Say(host, alice, "buildsharer", """{"op":"publish","title":"Bow ranger","note":"fast","tags":["bow","pvp"],"level":216,"class":9,"skills":[{"name":"God mode","level":99}],"equipment":[{"name":"Cheat"}]}""");
        var published = LastReply(server, 1);
        Assert.AreEqual("published", published.GetProperty("op").GetString());
        var build = published.GetProperty("build");
        Assert.IsTrue(build.GetProperty("verified").GetBoolean());
        Assert.AreEqual(3, build.GetProperty("class").GetInt32(), "the server's class, not the client's");
        Assert.AreEqual(41, build.GetProperty("level").GetInt32(), "the server's level, not the client's 216");
        Assert.AreEqual("Weapon Lore", build.GetProperty("skills")[0].GetProperty("name").GetString());
        Assert.AreEqual(2, build.GetProperty("skills").GetArrayLength(), "skills come from the server, the client's list is ignored");
        Assert.AreEqual("Decaying Bow of Oblivion", build.GetProperty("equipment")[0].GetProperty("name").GetString());
        Assert.AreEqual("rare", build.GetProperty("equipment")[0].GetProperty("tier").GetString());
        Assert.AreEqual("Battle Mage", build.GetProperty("class_name").GetString());
        Assert.IsFalse(build.TryGetProperty("owner", out _), "the owner's id never leaves the server");
        Assert.IsFalse(published.GetRawText().Contains(host.Uid("name:Alice")));

        Say(host, alice, "buildsharer", """{"op":"list"}""");
        var list = LastReply(server, 1);
        Assert.AreEqual(1, list.GetProperty("total").GetInt32());
        Assert.AreEqual("Alice", list.GetProperty("builds")[0].GetProperty("name").GetString());
        Assert.IsFalse(list.GetProperty("builds")[0].TryGetProperty("skills", out _), "the list holds summaries");
        var id = list.GetProperty("builds")[0].GetProperty("id").GetString()!;
        Say(host, alice, "buildsharer", $$"""{"op":"get","id":"{{id}}"}""");
        Assert.AreEqual("Blazing Disc", LastReply(server, 1).GetProperty("build").GetProperty("arts")[0].GetProperty("name").GetString());

        // The public endpoints: JSON list and one build; no ids of the owner anywhere.
        var json = Get(host, "build_sharer", "builds", ("q", "bow"));
        Assert.AreEqual(200, json.Status);
        StringAssert.Contains(json.Body, "Alice");
        Assert.IsFalse(json.Body.Contains(host.Uid("name:Alice")));
        Assert.AreEqual(404, Get(host, "build_sharer", "build", ("id", "nope")).Status);
        StringAssert.Contains(Get(host, "build_sharer", "build", ("id", id)).Body, "Vampirism");
        host.StoreOf("build_sharer").TryGet("next_id", out var next);
        Assert.AreEqual(1.0, next);
    }

    [TestMethod]
    public void WithoutHeroDataTheBuildIsReportedAndEverythingTheClientSendsIsLimited()
    {
        var (host, server, folder) = Sharer();
        using var keep = folder;
        var bob = Player(2, "Bob", level: 17, cls: 5); server.All.Add(bob);
        var many = string.Join(",", Enumerable.Range(0, 500).Select(i => $"\"item{i}\""));
        var title = new string('T', 500);
        Say(host, bob, "buildsharer", $$"""{"op":"publish","title":"{{title}}","note":"line\nbreak\u0007","tags":["a","","b",5,["x"],"{{new string('g', 99)}}","c","d","e","f"],"carried":[{{many}}],"level":200}""");
        var build = LastReply(server, 2).GetProperty("build");
        Assert.IsFalse(build.GetProperty("verified").GetBoolean());
        Assert.AreEqual(17, build.GetProperty("level").GetInt32(), "the server's level wins over the client's");
        Assert.AreEqual(40, build.GetProperty("title").GetString()!.Length);
        Assert.AreEqual("line break", build.GetProperty("note").GetString());
        Assert.IsTrue(build.GetProperty("tags").GetArrayLength() <= 5);
        Assert.IsTrue(build.GetProperty("tags").EnumerateArray().All(t => t.GetString()!.Length is > 0 and <= 16));
        Assert.AreEqual(12, build.GetProperty("carried").GetArrayLength());
        Assert.AreEqual(0, build.GetProperty("skills").GetArrayLength());

        // Garbage: not a table, no op, a number as op, a table as id; the script survives all of it.
        foreach (var junk in new[] { "5", "\"x\"", "[1,2]", "{}", """{"op":5}""", """{"op":"get","id":{"a":1}}""", """{"op":"list","q":{"a":1},"class":[1],"page":"x","per":1e99}""",
            """{"op":"publish","tags":"notalist","carried":7,"title":9}""", """{"op":"nonsense"}""" })
            Say(host, bob, "buildsharer", junk);
        Assert.AreEqual(0, host.Errors);
        // Rate limit: a burst of requests ends with "too many requests".
        for (var i = 0; i < 20; i++) Say(host, bob, "buildsharer", """{"op":"list"}""");
        Assert.AreEqual("too many requests", LastReply(server, 2).GetProperty("error").GetString());
    }

    [TestMethod]
    public void SearchFiltersByTextAndClassAndSortsAndPages()
    {
        var (host, server, folder) = Sharer();
        using var keep = folder;
        for (var i = 1; i <= 5; i++)
        {
            var p = Player(i, "Hero" + i, level: 10 * i, cls: i <= 2 ? 1 : 3); server.All.Add(p);
            server.Heroes[i] = Quests(HeroOf(i, "Hero" + i, 10 * i, p.Class));
            Say(host, p, "buildsharer", $$"""{"op":"publish","title":"build {{i}}","tags":["{{(i == 4 ? "needle" : "hay")}}"]}""");
        }
        Say(host, server.All[0], "buildsharer", """{"op":"list","q":"NEEDLE"}""");
        var found = LastReply(server, 1);
        Assert.AreEqual(1, found.GetProperty("total").GetInt32());
        Assert.AreEqual("Hero4", found.GetProperty("builds")[0].GetProperty("name").GetString());
        Say(host, server.All[1], "buildsharer", """{"op":"list","class":"seraphim"}""");
        Assert.AreEqual(2, LastReply(server, 2).GetProperty("total").GetInt32());
        Say(host, server.All[2], "buildsharer", """{"op":"list","class":"Battle Mage","sort":"level","per":2}""");
        var page = LastReply(server, 3);
        Assert.AreEqual(3, page.GetProperty("total").GetInt32());
        Assert.AreEqual(2, page.GetProperty("pages").GetInt32());
        Assert.AreEqual("Hero5", page.GetProperty("builds")[0].GetProperty("name").GetString(), "highest level first");
        Say(host, server.All[3], "buildsharer", """{"op":"list","class":3,"sort":"level","per":2,"page":99}""");
        Assert.AreEqual(2, LastReply(server, 4).GetProperty("page").GetInt32(), "a page past the end is the last page");
        StringAssert.Contains(Get(host, "build_sharer", "builds", ("class", "Seraphim")).Body, "Hero1");
    }

    [TestMethod]
    public void BuildCooldownUpdatesInPlaceUnpublishAndPersistence()
    {
        var folder = new Folder();
        var (host, server, _) = Start("", "build_sharer", null, folder);               // the default cooldown (15 s)
        var alice = Player(1, "Alice"); server.All.Add(alice);
        server.Heroes[1] = Quests(HeroOf(1, "Alice"));
        Say(host, alice, "buildsharer", """{"op":"publish","title":"one"}""");
        Assert.AreEqual("published", LastReply(server, 1).GetProperty("op").GetString());
        Say(host, alice, "buildsharer", """{"op":"publish","title":"two"}""");
        StringAssert.Contains(LastReply(server, 1).GetProperty("error").GetString(), "wait");
        host.StoreOf("build_sharer").Set("config", new Dictionary<string, object?> { ["publish_cooldown"] = 0.0 }); host.Load();           // reload: the build comes back from storage
        Say(host, alice, "buildsharer", """{"op":"mine"}""");
        Assert.AreEqual("one", LastReply(server, 1).GetProperty("build").GetProperty("title").GetString());
        Assert.IsTrue(LastReply(server, 1).GetProperty("hero_data").GetBoolean());
        Say(host, alice, "buildsharer", """{"op":"publish","title":"two"}""");
        Say(host, alice, "buildsharer", """{"op":"list"}""");
        Assert.AreEqual(1, LastReply(server, 1).GetProperty("total").GetInt32(), "publishing again replaces the hero's build");
        Assert.AreEqual("two", LastReply(server, 1).GetProperty("builds")[0].GetProperty("title").GetString());
        Assert.IsTrue(host.FireChat(alice, "!sharebuild chat title"));
        StringAssert.Contains(server.Told.Last().Text, "Build shared as b1");
        Assert.IsTrue(host.FireChat(alice, "!builds"));
        StringAssert.StartsWith(server.Told.Last().Text, "1 shared build.");
        host.FlushStores(wait: true); host.Dispose();
        var second = new ScriptHost(new ScriptOptions { Directory = folder.Scripts, DataDirectory = folder.Data }, server);   // a restart
        second.StoreOf("build_sharer").Set("publish_cooldown", 0.0); second.Load();
        Say(second, alice, "buildsharer", """{"op":"list"}""");
        Assert.AreEqual("chat title", LastReply(server, 1).GetProperty("builds")[0].GetProperty("title").GetString());
        Say(second, alice, "buildsharer", """{"op":"unpublish"}""");
        Assert.IsTrue(LastReply(server, 1).GetProperty("removed").GetBoolean());
        Say(second, alice, "buildsharer", """{"op":"list"}""");
        Assert.AreEqual(0, LastReply(server, 1).GetProperty("total").GetInt32());
        Assert.AreEqual(0, second.Errors + host.Errors);
        second.FlushStores(wait: true); second.Dispose(); folder.Dispose();
    }

    [TestMethod]
    public void BuildPageEscapesEveryPlayerText()
    {
        var (host, server, folder) = Sharer();
        using var keep = folder;
        var evil = new ScriptPlayer(1, "name", "name:evil", 1, 10, true, true, 0, 0, "<script>alert(1)</script>");
        server.All.Add(evil);
        server.Heroes[1] = Quests(HeroOf(1, "x"));
        Say(host, evil, "buildsharer", """{"op":"publish","title":"\"><img src=x onerror=alert(2)>","note":"<b>n</b>","tags":["<i>t</i>"]}""");
        foreach (var query in new[] { Array.Empty<(string, string)>(), [("q", "\"><script>alert(3)</script>")], [("id", "b1")] })
        {
            var page = Get(host, "build_sharer", "", query);
            Assert.AreEqual(200, page.Status);
            Assert.IsTrue(page.Page);
            if (query.Length == 0 || query[0].Item1 == "id") StringAssert.Contains(page.Body, "&lt;script&gt;alert(1)&lt;/script&gt;");
            else StringAssert.Contains(page.Body, "&quot;&gt;&lt;script&gt;alert(3)");
            Assert.IsFalse(page.Body.Contains("<script>alert"), "no player text becomes a tag");
            Assert.IsFalse(page.Body.Contains("<img"), "no player text becomes a tag");
            Assert.IsFalse(page.Body.Contains("<i>t"), "no player text becomes a tag");
            Assert.IsFalse(page.Body.Contains(host.Uid("name:evil")));
        }
        Assert.AreEqual(0, host.Errors);
    }

    [TestMethod]
    public void BuildListIsCappedAtTwoHundred()
    {
        var (host, server, folder) = Sharer();
        using var keep = folder;
        for (var i = 1; i <= 201; i++)
        {
            var p = Player(i, "H" + i); server.All.Add(p);
            Say(host, p, "buildsharer", """{"op":"publish"}""");
        }
        StringAssert.Contains(LastReply(server, 201).GetProperty("error").GetString(), "full");
        Say(host, server.All[0], "buildsharer", """{"op":"unpublish"}""");
        Say(host, server.All[200], "buildsharer", """{"op":"publish"}""");
        Assert.AreEqual("published", LastReply(server, 201).GetProperty("op").GetString(), "room again after one was removed");
        Assert.AreEqual(0, host.Errors);
    }

    // ---- quest_helper ----------------------------------------------------------------------------------------------------------

    [TestMethod]
    public void QuestHelperSaysSoWhenTheServerHasNoHeroData()
    {
        var (host, server, folder) = Start("", "quest_helper");
        using var keep = folder;
        var alice = Player(1, "Alice"); server.All.Add(alice);
        Say(host, alice, "questhelper", """{"op":"state"}""");
        var state = LastReply(server, 1);
        Assert.IsFalse(state.GetProperty("available").GetBoolean());
        StringAssert.Contains(state.GetProperty("reason").GetString(), "no hero data");
        Assert.IsTrue(host.FireChat(alice, "!quests"));
        StringAssert.Contains(server.Told.Last().Text, "no hero data");
        Assert.AreEqual(0, host.Errors);
    }

    [TestMethod]
    public void QuestHelperListsRecordsWithCatalogHintsAnnouncesChangesAndNamesOtherHeroes()
    {
        var (host, server, folder) = Start("", "quest_helper", h => h.StoreOf("quest_helper").Set("catalog", new Dictionary<string, object?>
        {
            ["100"] = new Dictionary<string, object?> { ["name"] = "The <lost> sword", ["area"] = "Ancaria", ["hint"] = "Ask the smith.", ["states"] = new Dictionary<string, object?> { ["1"] = "started", ["2"] = "done" } },
        }));
        using var keep = folder;
        var alice = Player(1, "Alice"); var bob = Player(2, "Bob"); server.All.Add(alice); server.All.Add(bob);
        server.Heroes[1] = Quests(HeroOf(1, "Alice"), (100, 1), (555, 7));
        server.Heroes[2] = Quests(HeroOf(2, "Bob"), (100, 2));
        host.FirePlayer("enter", alice); host.FirePlayer("enter", bob);
        host.Pump(server.Now += 3100); host.Pump(server.Now += 100);                       // the 3 s delayed first read of both
        Say(host, alice, "questhelper", """{"op":"state","watch":true}""");
        var state = LastReply(server, 1);
        Assert.IsTrue(state.GetProperty("available").GetBoolean());
        Assert.AreEqual(2, state.GetProperty("total").GetInt32());
        Assert.AreEqual(1, state.GetProperty("known").GetInt32());
        var first = state.GetProperty("records")[0];
        Assert.AreEqual(100, first.GetProperty("key").GetInt32());
        Assert.AreEqual("The <lost> sword", first.GetProperty("name").GetString());
        Assert.AreEqual("started", first.GetProperty("label").GetString());
        Assert.AreEqual("Ask the smith.", first.GetProperty("hint").GetString());
        Assert.AreEqual("Bob", first.GetProperty("others")[0].GetString());
        Assert.IsFalse(state.GetProperty("records")[1].TryGetProperty("name", out _), "an unknown key has no invented name");
        Assert.AreEqual(0, server.Told.Count, "the first read seeds the baseline: no notice for records the hero already had");

        // A change: record 100 moves on, a new record appears: a notice and a push to the watching client.
        var sent = server.Sent.Count;
        server.Heroes[1] = Quests(HeroOf(1, "Alice"), (100, 2), (555, 7), (777, 1));
        host.Pump(server.Now += 5100);
        Assert.IsTrue(server.Told.Any(t => t.Slot == 1 && t.Text.StartsWith("Quest record: ")), string.Join(";", server.Told));
        Assert.IsTrue(server.Sent.Count > sent, "the watching client got the new state");
        Assert.AreEqual(3, LastReply(server, 1).GetProperty("total").GetInt32());
        Assert.AreEqual(0, server.Told.Count(t => t.Slot == 2), "Bob's records did not change");

        Assert.IsTrue(host.FireChat(alice, "!quests"));
        StringAssert.Contains(string.Join("\n", server.Told.Select(t => t.Text)), "3 quest records");

        // The page and the endpoint: names only, escaped, counts.
        var page = Get(host, "quest_helper", "");
        StringAssert.Contains(page.Body, "The &lt;lost&gt; sword");
        Assert.IsFalse(page.Body.Contains("<lost>"));
        StringAssert.Contains(page.Body, "Alice");
        var records = JsonDocument.Parse(Get(host, "quest_helper", "records").Body).RootElement;
        Assert.AreEqual(100, records[0].GetProperty("key").GetInt32());
        Assert.AreEqual(2, records[0].GetProperty("heroes").GetInt32());

        // Leaving forgets the hero's session state; garbage requests do nothing.
        host.FirePlayer("leave", alice);
        foreach (var junk in new[] { "5", "{}", """{"op":7}""", """{"op":"state","watch":{"a":1}}""", """{"op":"x"}""" }) Say(host, bob, "questhelper", junk);
        Assert.AreEqual(0, host.Errors);
    }

    // ---- through the real server, with a real hero upload (saves under SACRED_HERO_SAVES; inconclusive without them) ---------------

    private static byte[] Word(uint value) { var b = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b, value); return b; }
    private static byte[] Chat(string text) { var b = new byte[4 + text.Length * 2 + 2]; Encoding.Unicode.GetBytes(text, b.AsSpan(4)); return b; }
    private static string ChatText(byte[] payload)
    {
        var text = Encoding.Unicode.GetString(payload, 4, payload.Length - 4);
        var end = text.IndexOf(' ');
        return end < 0 ? text : text[..end];
    }

    [TestMethod]
    public void RealServerGivesScriptsTheHeroItKeepsAndTheBuildIsSharedFromIt()
    {
        var save = HeroWriterTests.Saves().FirstOrDefault(p => p.Contains("hero07", StringComparison.OrdinalIgnoreCase)) ?? HeroWriterTests.Saves().FirstOrDefault();
        if (save is null) { Assert.Inconclusive("no hero saves (set SACRED_HERO_SAVES)"); return; }
        var decoded = HeroWriterTests.Decode(File.ReadAllBytes(save));
        using var folder = new Folder();
        folder.Write("build_sharer.lua", File.ReadAllText(ExamplePath("build_sharer.lua")));
        folder.Write("quest_helper.lua", File.ReadAllText(ExamplePath("quest_helper.lua")));
        var config = new Config.GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5 };
        config.Scripts.Directory = folder.Scripts; config.Scripts.DataDirectory = folder.Data;
        var host = new Session.GameServerHost(config, new Session.AdmissionRules(null), null) { TrackHeroes = true, Names = NameServiceTests.Shared };
        host.StartScripts();
        var thread = new Thread(host.Run) { IsBackground = true };
        thread.Start();
        try
        {
            using var player = RawClient.Admit(host.TcpPort, "acct.Hero", 7);
            player.Send(0x016, default); player.Send(0x018, Word(0x1964)); player.WaitFor(0x018);
            // Before the client's hero arrives the server has nothing: the mods say so, nothing is made up.
            player.Send(0x005, Chat("!quests"));
            StringAssert.Contains(ChatText(player.WaitFor(0x003).Payload), "no hero data");
            var (live, bodies) = HeroWriterTests.Upload(decoded, player.Slot);
            byte[] State(uint reference, byte[] body)
            {
                var payload = new byte[8 + body.Length];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(payload, reference);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8)));
                body.CopyTo(payload, 8);
                return payload;
            }
            foreach (var (reference, body) in bodies) player.Send(0x16E, State(reference, body));
            player.Send(0x16E, State(player.Slot, live.HeroBody.ToArray()));
            player.Send(0x122, [.. BitConverter.GetBytes((uint)player.Slot), .. live.InventoryStream.ToArray()]);
            Web.HeroInfo? hero = null;
            for (var i = 0; i < 100 && (hero is null || hero.Equipment.Length == 0); i++) { Thread.Sleep(30); hero = host.Heroes().FirstOrDefault(); }
            Assert.IsNotNull(hero);
            player.Send(0x005, Chat("!sharebuild Live build"));
            StringAssert.Contains(ChatText(player.WaitFor(0x003).Payload), "Build shared as b1");
            var store = host.Scripts!.StoreOf("build_sharer");
            Assert.IsTrue(store.TryGet("build:b1", out var stored));
            var json = JsonSerializer.Serialize(stored);
            var build = JsonDocument.Parse(json).RootElement;
            Assert.IsTrue(build.GetProperty("verified").GetBoolean());
            Assert.AreEqual("Hero", build.GetProperty("name").GetString());
            Assert.AreEqual(hero.Level, build.GetProperty("level").GetInt32());
            Assert.AreEqual(hero.Skills.Length, build.GetProperty("skills").GetArrayLength());
            Assert.AreEqual(hero.Equipment.Length, build.GetProperty("equipment").GetArrayLength());
            // The same hero through quest_helper: the pairs of the saved inventory stream.
            var pairs = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(live.InventoryStream[Sacred.Core.Pax.Hero.Inventory.SacredSavedInventoryLayout.QuestCountOffset..]);
            player.Send(0x005, Chat("!quests"));
            var reply = ChatText(player.WaitFor(0x003).Payload);
            StringAssert.StartsWith(reply, $"{pairs} quest record");
            Console.WriteLine($"quest pairs in this hero: {pairs}; build: {json.Length} bytes");
        }
        finally { host.Stop(); thread.Join(); host.Dispose(); }
    }

    // ---- hot reload and configuration of build_sharer and quest_helper ------------------------------------------------------------

    [TestMethod]
    public void BuildSharerKeepsItsRateLimitOverAReloadAndTakesItsCapsFromTheConfig()
    {
        var (host, server, folder) = Start("", "build_sharer", h => h.StoreOf("build_sharer").Set("config", new Dictionary<string, object?>
            { ["max_title"] = 5.0, ["publish_cooldown"] = 60.0, ["max_builds"] = "many" }));
        using var keep = folder;
        var alice = Player(1, "Alice"); server.All.Add(alice);
        server.Heroes[1] = Quests(HeroOf(1, "Alice"));
        Say(host, alice, "buildsharer", """{"op":"publish","title":"abcdefghij"}""");
        Assert.AreEqual("abcde", LastReply(server, 1).GetProperty("build").GetProperty("title").GetString(), "max_title from the config");
        Assert.IsTrue(host.Reload("build_sharer"));
        Say(host, alice, "buildsharer", """{"op":"publish","title":"again"}""");
        StringAssert.Contains(LastReply(server, 1).GetProperty("error").GetString(), "wait", "the cooldown is not reset by a reload");
        Say(host, alice, "buildsharer", """{"op":"list"}""");
        Assert.AreEqual(1, LastReply(server, 1).GetProperty("total").GetInt32());
        var page = host.HandleRequest("build_sharer", "config", [], false).Body;
        StringAssert.Contains(page, "max_builds: needs a number");
        StringAssert.Contains(page, "<td>max_title</td><td>storage</td><td><code>5</code></td>");
        // After a restart (shutdown) the cooldown starts clean, the build is still there.
        host.Shutdown(); host.FlushStores(wait: true); host.Dispose();
        var (again, server2, _) = Start("", "build_sharer", null, folder);
        server2.All.Add(alice); server2.Heroes[1] = Quests(HeroOf(1, "Alice"));
        Say(again, alice, "buildsharer", """{"op":"mine"}""");
        Assert.AreEqual("abcde", LastReply(server2, 1).GetProperty("build").GetProperty("title").GetString());
        Say(again, alice, "buildsharer", """{"op":"publish","title":"restart"}""");
        Assert.AreEqual("published", LastReply(server2, 1).GetProperty("op").GetString());
        Assert.AreEqual(0, again.Errors);
    }

    [TestMethod]
    public void QuestHelperKeepsItsBaselineOverAReloadAndTheCatalogIsConfigData()
    {
        var (host, server, folder) = Start("", "quest_helper", h => h.StoreOf("quest_helper").Set("config", new Dictionary<string, object?>
        {
            ["catalog"] = new Dictionary<string, object?>
            {
                ["100"] = new Dictionary<string, object?> { ["name"] = "The sword", ["hint"] = "Ask the smith.", ["states"] = new Dictionary<string, object?> { ["2"] = "done" } },
                ["abc"] = new Dictionary<string, object?> { ["name"] = "not a key" },
            },
            ["max_pairs"] = -3.0,
        }));
        using var keep = folder;
        var alice = Player(1, "Alice"); server.All.Add(alice);
        server.Heroes[1] = Quests(HeroOf(1, "Alice"), (100, 1));
        host.FirePlayer("enter", alice);
        host.Pump(server.Now += 3100); host.Pump(server.Now += 100);
        Say(host, alice, "questhelper", """{"op":"state","watch":true}""");
        Assert.AreEqual("The sword", LastReply(server, 1).GetProperty("records")[0].GetProperty("name").GetString());
        var page = host.HandleRequest("quest_helper", "config", [], false).Body;
        StringAssert.Contains(page, "catalog entry abc refused");
        StringAssert.Contains(page, "max_pairs: needs a number");

        Assert.IsTrue(host.Reload("quest_helper"));
        server.Told.Clear();
        var sent = server.Sent.Count;
        server.Heroes[1] = Quests(HeroOf(1, "Alice"), (100, 2));                // the record moves on right after the reload
        host.Pump(server.Now += 5100);
        Assert.IsTrue(server.Told.Any(t => t.Slot == 1 && t.Text.Contains("Quest record: The sword") && t.Text.Contains("Ask the smith.")),
            "the baseline survived (a changed record is announced, not silently taken as the first read): " + string.Join(";", server.Told));
        Assert.IsTrue(server.Sent.Count > sent, "the panel that was watching still gets its updates");
        Assert.AreEqual("done", LastReply(server, 1).GetProperty("records")[0].GetProperty("label").GetString());
        Assert.AreEqual(0, host.Errors);
    }
}
