using System.Buffers.Binary;
using System.Text.Json;
using Sacred.GameServer.Scripting;
using Sacred.Network.Gameplay;

namespace Sacred.GameServer.Tests;

/// <summary>The statistics mods of scripts-examples: server_stats, hall_of_fame, seasons, rare_drops, graveyard.</summary>
public partial class ScriptTests
{
    private static readonly string[] StatMods = ["server_stats", "hall_of_fame", "seasons", "rare_drops", "graveyard"];

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    private static ScriptResponse Get(ScriptHost host, string mod, string path, bool publicOnly = true, params (string, string)[] query) =>
        host.HandleRequest(mod, path, query.Select(q => KeyValuePair.Create(q.Item1, q.Item2)).ToArray(), publicOnly);

    [TestMethod]
    public void StatsModsLoadAndEveryPageAndEndpointAnswers()
    {
        using var folder = new Folder();
        foreach (var mod in StatMods) folder.Write(mod + ".lua", Example(mod + ".lua"));
        var (host, _, _) = Host(folder);
        foreach (var mod in StatMods)
        {
            var entry = host.Mods.Single(m => m.Mod == mod);
            Assert.IsNull(entry.Error, mod);
            Assert.IsTrue(entry.Description is { Length: > 20 }, mod);
            var page = Get(host, mod, "");
            Assert.AreEqual(200, page.Status, mod);
            Assert.IsTrue(page.Page, mod);
            StringAssert.StartsWith(page.Body, "<!doctype html>");
        }
        foreach (var (mod, path) in new[] { ("server_stats", "stats"), ("hall_of_fame", "records"), ("hall_of_fame", "firsts"), ("seasons", "current"), ("seasons", "archive"),
                     ("rare_drops", "feed"), ("rare_drops", "feed.csv"), ("graveyard", "recent"), ("graveyard", "recent.csv"), ("graveyard", "deadliest") })
            Assert.AreEqual(200, Get(host, mod, path).Status, mod + "/" + path);
        Assert.AreEqual(404, Get(host, "seasons", "end-season").Status, "the season switch is admin only");
    }

    [TestMethod]
    public void ServerStatsCountsAndShowsOnlyCharacterNames()
    {
        using var folder = new Folder();
        folder.Write("server_stats.lua", Example("server_stats.lua"));
        var (host, server, _) = Host(folder, mod: "server_stats");
        var alice = server.All[0] with { Name = "acct.Alice", Character = "Alice" };
        server.All[0] = alice;
        host.FirePlayer("join", alice);
        host.FirePlayer("join", alice);
        for (uint i = 0; i < 3; i++) host.FireKill(alice, new ScriptMonster(100 + i, 38, 5, "Orc Warrior"));
        host.FireKill(alice, new ScriptMonster(200, 551, 1, "<b>Horse</b>"));
        host.FireDeath(alice, null, new ScriptMonster(9000, 38, 12));
        host.FireChat(alice, "hello");
        for (server.Now = 0; server.Now <= 301_000; server.Now += 1000) host.Pump(server.Now);
        var stats = Json(Get(host, "server_stats", "stats").Body);
        Assert.AreEqual(1, stats.GetProperty("online").GetInt32());
        Assert.AreEqual("Alice", stats.GetProperty("players")[0].GetString());
        Assert.AreEqual(1, stats.GetProperty("peak").GetInt32());
        var totals = stats.GetProperty("totals");
        Assert.AreEqual(2, totals.GetProperty("joins").GetInt32());
        Assert.AreEqual(1, totals.GetProperty("heroes").GetInt32(), "a hero counts once");
        Assert.AreEqual(4, totals.GetProperty("kills").GetInt32());
        Assert.AreEqual(1, totals.GetProperty("deaths").GetInt32());
        Assert.AreEqual(1, totals.GetProperty("chat").GetInt32());
        Assert.AreEqual(24, stats.GetProperty("activity").GetArrayLength());
        Assert.AreEqual(4, stats.GetProperty("activity")[23].GetProperty("kills").GetInt32());
        Assert.IsTrue(stats.GetProperty("samples").GetArrayLength() >= 1);
        Assert.AreEqual("Orc Warrior", stats.GetProperty("creatures")[0].GetProperty("creature").GetString());
        var page = Get(host, "server_stats", "").Body;
        StringAssert.Contains(page, "<li>Alice</li>");
        StringAssert.Contains(page, "&lt;b&gt;Horse&lt;/b&gt;");
        Assert.IsFalse(page.Contains("acct"), "no account names");
        Assert.IsFalse(page.Contains("<b>Horse"), "creature names are escaped");
    }

    [TestMethod]
    public void HallOfFameKeepsRecordsFirstsAndAnnouncesTheNewHolder()
    {
        using var folder = new Folder();
        folder.Write("hall_of_fame.lua", Example("hall_of_fame.lua"));
        var (host, server, _) = Host(folder, mod: "hall_of_fame");
        var alice = server.All[0] with { Name = "acct.Alice", Character = "Alice", Key = "id:1:1", Level = 12 };
        var bob = alice with { Slot = 2, Name = "Bob", Character = "Bob", Key = "id:2:1", Level = 60 };
        host.FireKill(alice, new ScriptMonster(1, 38, 3, "Orc Warrior"));
        host.FireKill(alice, new ScriptMonster(2, 38, 3, "Orc Warrior"));
        host.FireKill(bob, new ScriptMonster(3, 551, 90, "Horse"));
        host.FireKill(bob, new ScriptMonster(4, 38, 3, "Orc Warrior"));
        host.FireKill(bob, new ScriptMonster(5, 38, 3, "Orc Warrior"));
        var records = Json(Get(host, "hall_of_fame", "records").Body).EnumerateArray().ToDictionary(r => r.GetProperty("record").GetString()!);
        Assert.AreEqual("Bob", records["level"].GetProperty("hero").GetString());
        Assert.AreEqual(60, records["level"].GetProperty("value").GetInt32());
        Assert.AreEqual("Bob", records["kills"].GetProperty("hero").GetString(), "three kills beat two");
        Assert.AreEqual("Horse", records["biggest"].GetProperty("extra").GetString());
        Assert.AreEqual(90, records["biggest"].GetProperty("value").GetInt32());
        var firsts = Json(Get(host, "hall_of_fame", "firsts").Body);
        var levels = firsts.GetProperty("levels").EnumerateArray().ToDictionary(f => f.GetProperty("level").GetInt32(), f => f.GetProperty("hero").GetString());
        Assert.AreEqual("Alice", levels[10]);
        Assert.AreEqual("Bob", levels[50], "Bob is the first to level 50");
        Assert.IsFalse(levels.ContainsKey(100));
        Assert.IsTrue(firsts.GetProperty("creatures").EnumerateArray().Any(f => f.GetProperty("creature").GetString() == "Orc Warrior" && f.GetProperty("hero").GetString() == "Alice"));
        CollectionAssert.Contains(server.Said, "Bob is the first hero on this server to reach level 50!");
        CollectionAssert.Contains(server.Said, "Bob set a new Hall of Fame record: highest level 60 - beating Alice!");
        Assert.IsFalse(server.Said.Any(s => s.Contains("most creatures")), "the kill record is below its announcement minimum: silent");
        Assert.IsTrue(host.FireChat(alice, "!hof"));
        Assert.IsTrue(server.Told.Any(t => t.Text.StartsWith("Highest level: Bob (60")));
        var page = Get(host, "hall_of_fame", "").Body;
        StringAssert.Contains(page, "<b>Bob</b> 60");
        Assert.IsFalse(page.Contains("acct"));
    }

    [TestMethod]
    public void HallOfFameAnnouncesABrokenRecordAboveTheMinimum()
    {
        using var folder = new Folder();
        folder.Write("hall_of_fame.lua", Example("hall_of_fame.lua").Replace("level = 30", "level = 5"));
        var (host, server, _) = Host(folder, mod: "hall_of_fame");
        var alice = server.All[0] with { Character = "Alice", Key = "id:1:1", Level = 10 };
        var bob = alice with { Slot = 2, Character = "Bob", Key = "id:2:1", Level = 11 };
        host.FirePlayer("enter", alice);
        host.FirePlayer("enter", bob);
        CollectionAssert.Contains(server.Said, "Bob set a new Hall of Fame record: highest level 11 - beating Alice!");
    }

    [TestMethod]
    public void SeasonsArchiveTheStandingsAndStartAgainFromZero()
    {
        using var folder = new Folder();
        folder.Write("seasons.lua", Example("seasons.lua"));
        var (host, server, _) = Host(folder, mod: "seasons");
        var alice = server.All[0] with { Name = "acct.Alice", Character = "Alice", Key = "id:1:1", Level = 10 };
        var bob = alice with { Slot = 2, Character = "Bob", Key = "id:2:1", Level = 20 };
        host.FirePlayer("enter", alice);
        for (uint i = 0; i < 5; i++) host.FireKill(alice, new ScriptMonster(10 + i, 38, 3));
        for (uint i = 0; i < 3; i++) host.FireKill(bob, new ScriptMonster(50 + i, 38, 3));
        host.FireKill(alice with { Level = 14 }, new ScriptMonster(99, 38, 3));
        var current = Json(Get(host, "seasons", "current").Body);
        Assert.AreEqual(1, current.GetProperty("season").GetInt32());
        var kills = current.GetProperty("boards").GetProperty("kills");
        Assert.AreEqual("Alice", kills[0].GetProperty("name").GetString());
        Assert.AreEqual(6, kills[0].GetProperty("value").GetInt32());
        Assert.AreEqual(4, current.GetProperty("boards").GetProperty("gain")[0].GetProperty("value").GetInt32(), "levels gained: 10 -> 14");
        Assert.IsTrue(host.FireChat(alice, "!season"));
        StringAssert.Contains(server.Told.Last().Text, "Season 1: 29 d");
        StringAssert.Contains(server.Told.Last().Text, "You are number 1 with 6");

        var ended = Json(Get(host, "seasons", "end-season", publicOnly: false).Body);
        Assert.AreEqual(1, ended.GetProperty("ended").GetInt32());
        Assert.AreEqual(2, ended.GetProperty("season").GetInt32());
        StringAssert.Contains(server.Said.Last(), "Season 1 is over! Most creatures defeated: Alice (6). Season 2 has begun");
        var fresh = Json(Get(host, "seasons", "current").Body);
        Assert.AreEqual(2, fresh.GetProperty("season").GetInt32());
        Assert.AreEqual(0, fresh.GetProperty("boards").GetProperty("kills").GetArrayLength(), "a new season starts from zero");
        var archive = Json(Get(host, "seasons", "archive").Body);
        Assert.AreEqual(1, archive.GetArrayLength());
        Assert.AreEqual("Alice", archive[0].GetProperty("boards").GetProperty("kills")[0].GetProperty("name").GetString());
        Assert.AreEqual("Bob", archive[0].GetProperty("boards").GetProperty("kills")[1].GetProperty("name").GetString());
        host.FireKill(bob, new ScriptMonster(300, 38, 3));
        Assert.AreEqual("Bob", Json(Get(host, "seasons", "current").Body).GetProperty("boards").GetProperty("kills")[0].GetProperty("name").GetString());
        var page = Get(host, "seasons", "").Body;
        StringAssert.Contains(page, "<h1>Season 2</h1>");
        StringAssert.Contains(page, "Past seasons");
        StringAssert.Contains(page, "<b>Alice</b> 6");
        Assert.IsFalse(page.Contains("acct"));
    }

    [TestMethod]
    public void SeasonsEndOnTheirOwnWhenTheTimeIsUp()
    {
        using var folder = new Folder();
        folder.Write("seasons.lua", Example("seasons.lua").Replace("length_days = 30", "length_days = 0.00001"));
        var (host, server, _) = Host(folder, mod: "seasons");
        host.FireKill(server.All[0], new ScriptMonster(1, 38, 3));
        Thread.Sleep(1200);
        for (server.Now = 0; server.Now <= 61_000; server.Now += 1000) host.Pump(server.Now);
        Assert.AreEqual(2, Json(Get(host, "seasons", "current").Body).GetProperty("season").GetInt32());
        Assert.AreEqual(1, Json(Get(host, "seasons", "archive").Body).GetArrayLength());
    }

    [TestMethod]
    public void RareDropsFeedTiesDropsToKillsAndEscapesNames()
    {
        using var folder = new Folder();
        folder.Write("rare_drops.lua", Example("rare_drops.lua"));
        var (host, server, _) = Host(folder, mod: "rare_drops");
        var alice = server.All[0] with { Name = "acct.Alice", Character = "Alice" };
        server.All[0] = alice;
        ScriptItem Item(uint reference, int rarity, string? name = "Decaying Bow of Oblivion") => new(reference, 2501, 33, rarity, 1, 800, name, 1234, 5000, 7000);
        host.FireDrop(alice, Item(1, 15));                                   // no kill: ignored (chest, world)
        host.FireKill(alice, new ScriptMonster(9, 38, 12, "Orc <i>Warrior</i>"));
        host.FireDrop(alice, Item(2, 3));                                    // normal: ignored
        host.FireDrop(alice, Item(3, 15));
        host.FireDrop(alice, Item(3, 15));                                   // the same object again: once
        host.FireDrop(alice, Item(4, 12, "<b>Gold Ring</b>"));
        CollectionAssert.AreEqual(new[] { "Alice found a unique Decaying Bow of Oblivion, dropped by Orc <i>Warrior</i>!",
                                          "Alice found a rare <b>Gold Ring</b>, dropped by Orc <i>Warrior</i>!" }, server.Said);
        var feed = Json(Get(host, "rare_drops", "feed").Body);
        Assert.AreEqual(2, feed.GetArrayLength());
        Assert.AreEqual("<b>Gold Ring</b>", feed[0].GetProperty("item").GetString(), "newest first");
        Assert.AreEqual("unique", feed[1].GetProperty("tier").GetString());
        Assert.AreEqual("Alice", feed[1].GetProperty("hero").GetString());
        Assert.AreEqual("Orc <i>Warrior</i>", feed[1].GetProperty("creature").GetString());
        StringAssert.Contains(Get(host, "rare_drops", "feed.csv").Body, "Decaying Bow of Oblivion");
        var page = Get(host, "rare_drops", "").Body;
        StringAssert.Contains(page, "&lt;b&gt;Gold Ring&lt;/b&gt;");
        StringAssert.Contains(page, "Orc &lt;i&gt;Warrior&lt;/i&gt;");
        StringAssert.Contains(page, "<li><b>Alice</b> 2</li>");
        Assert.IsFalse(page.Contains("<b>Gold Ring"));
        Assert.IsFalse(page.Contains("acct"));
        Assert.AreEqual(1, Json(Get(host, "rare_drops", "feed", true, ("n", "1")).Body).GetArrayLength());
    }

    [TestMethod]
    public void GraveyardRecordsKillersAndNamesThem()
    {
        using var folder = new Folder();
        folder.Write("graveyard.lua", Example("graveyard.lua"));
        var (host, server, _) = Host(folder, mod: "graveyard");
        var alice = server.All[0] with { Name = "acct.Alice", Character = "Alice", Level = 15 };
        var bob = alice with { Slot = 2, Character = "Bob", Key = "name:Bob", Level = 40 };
        host.FireDeath(alice, null, new ScriptMonster(9000, 307, 12, "Brigand"));
        host.FireDeath(bob, null, new ScriptMonster(9001, 307, 14, "Brigand"));
        host.FireDeath(alice, bob, null);
        host.FireDeath(bob, null, null);
        CollectionAssert.AreEqual(new[] { "Alice was slain by Brigand.", "Bob was slain by Brigand.", "Alice was slain by Bob.", "Bob has died." }, server.Said);
        var recent = Json(Get(host, "graveyard", "recent").Body);
        Assert.AreEqual(4, recent.GetArrayLength());
        Assert.AreEqual("unknown", recent[0].GetProperty("killer_kind").GetString());
        Assert.AreEqual("hero", recent[1].GetProperty("killer_kind").GetString());
        Assert.AreEqual("Bob", recent[1].GetProperty("killer").GetString());
        Assert.AreEqual("Brigand", recent[3].GetProperty("killer").GetString());
        Assert.AreEqual(12, recent[3].GetProperty("killer_level").GetInt32());
        var deadliest = Json(Get(host, "graveyard", "deadliest").Body);
        Assert.AreEqual("Brigand", deadliest[0].GetProperty("killer").GetString());
        Assert.AreEqual(2, deadliest[0].GetProperty("kills").GetInt32());
        Assert.IsTrue(host.FireChat(alice, "!rip"));
        Assert.AreEqual("Bob (level 40) - cause unknown", server.Told.First(t => t.Text.StartsWith("Bob")).Text);
        var page = Get(host, "graveyard", "").Body;
        StringAssert.Contains(page, "<b>Alice</b><span>level 15</span><span>slain by Bob</span>");
        StringAssert.Contains(page, "slain by Brigand (level 12)");
        Assert.IsFalse(page.Contains("acct"));
    }

    // ---- the "drop" event through the real server -----------------------------------------------------------------------------

    private static byte[] ItemState(uint reference, uint type, byte rarity, byte level, int x, int y, ushort sector, byte owner)
    {
        var values = new byte[GoldItemStateLayout.ItemDataSize];
        BinaryPrimitives.WriteUInt32LittleEndian(values, type);
        values[6] = rarity; values[25] = level;
        BinaryPrimitives.WriteUInt32LittleEndian(values.AsSpan(8), 800);
        var basis = new GoldObjectBaseState(reference, type, 0, sector, x, y, 0, 0, 0, 0, owner, 0);
        var physical = GoldPhysicalConstructionWriter.Write(basis, new GoldPhysicalVector(0, 1, 0));
        var body = GoldItemState.Create(physical, [], values, 0, []).ToPayload();
        var payload = new byte[24 + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, reference);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), type);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8), x);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12), y);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20), sector);
        body.CopyTo(payload, 24);
        return payload;
    }

    [TestMethod]
    public void ANewItemOnTheGroundFiresTheDropEventOnce()
    {
        using var folder = new Folder();
        folder.Write("a.lua", """
            server.on('drop', function(item, p)
              server.broadcast(('drop %d type %d level %d rarity %d sockets %d price %d at %d/%d/%d by %s'):format(item.ref, item.type, item.level, item.rarity, item.sockets, item.price, item.sector, item.x, item.y, p.character))
            end)
            """);
        using var server = new Server(folder);
        var host = RawClient.Admit(server.Port, "Host", 1);
        host.Send(0x016, default); host.Send(0x018, Word(0x1964)); host.WaitFor(0x018);
        var joiner = RawClient.Admit(server.Port, "Joiner", 2);
        joiner.Send(0x015, default); joiner.Send(0x017, default); joiner.WaitFor(0x017);
        using (host) using (joiner)
        {
            host.Send(0x16F, ItemState(0x5000, 2501, 13, 33, 5000, 7000, 1234, 1));
            Assert.AreEqual("drop 20480 type 2501 level 33 rarity 13 sockets 0 price 800 at 1234/5000/7000 by Host", ChatText(joiner.WaitFor(0x005).Payload));
            host.Send(0x16F, ItemState(0x5000, 2501, 13, 33, 5010, 7000, 1234, 1));      // an update of the same object: not a new drop
            host.Send(0x16F, ItemState(0x5001, 2501, 2, 10, 0, 0, 0, 1));              // carried (no position): not on the ground
            host.Send(0x16F, ItemState(0x5002, 2501, 15, 50, 100, 200, 7, 1));
            Assert.AreEqual("drop 20482 type 2501 level 50 rarity 15 sockets 0 price 800 at 7/100/200 by Host", ChatText(joiner.WaitFor(0x005).Payload));
        }
    }
}
