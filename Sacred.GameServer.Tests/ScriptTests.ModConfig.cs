using System.Text.Json;
using Sacred.GameServer.Scripting;

namespace Sacred.GameServer.Tests;

/// <summary>The server mods as data and hot reload: configuration seeded into the storage, edited live (config + config_rev), bad data
/// falling back to the defaults, the admin overview page (/mods/{mod}/config), and state surviving a reload of the mod.</summary>
public partial class ScriptTests
{
    /// <summary>Each mod and one of its numeric config fields (whole numbers between 10 and 20 are valid for all of them).</summary>
    private static readonly (string Mod, string Key)[] ConfigMods =
    [
        ("bounty_board", "slots"), ("achievements", "roster_limit"), ("server_stats", "keep_samples"), ("hall_of_fame", "keep_first_kills"),
        ("seasons", "top_n"), ("rare_drops", "keep"), ("graveyard", "keep"), ("quest_helper", "max_pairs"), ("build_sharer", "max_builds"),
    ];

    private static (ScriptHost Host, FakeServer Server) AllConfigMods(Folder folder, Action<ScriptHost>? beforeLoad = null)
    {
        foreach (var (mod, _) in ConfigMods) folder.Write(mod + ".lua", Example(mod + ".lua"));
        var server = new FakeServer();
        var host = new ScriptHost(new ScriptOptions { Directory = folder.Scripts, DataDirectory = folder.Data }, server);
        beforeLoad?.Invoke(host);
        host.Load();
        foreach (var file in host.Files) Assert.IsNull(file.Error, file.File + ": " + file.Error);
        return (host, server);
    }

    private static string ConfigPage(ScriptHost host, string mod)
    {
        var answer = Get(host, mod, "config", publicOnly: false);
        Assert.AreEqual(200, answer.Status, mod);
        return answer.Body;
    }

    private static string Row(string key, string source, string value) => $"<td>{key}</td><td>{source}</td><td><code>{value}</code></td>";

    [TestMethod]
    public void ModsSeedTheirDefaultsIntoTheStorageAndShowAnOverview()
    {
        using var folder = new Folder();
        var (host, _) = AllConfigMods(folder);
        foreach (var (mod, key) in ConfigMods)
        {
            var store = host.StoreOf(mod);
            Assert.IsTrue(store.TryGet("config", out var config) && config is Dictionary<string, object?> d && d.ContainsKey(key), mod + ": the defaults are data in the storage");
            Assert.IsTrue(store.TryGet("config_seeded", out var seeded) && seeded is true, mod);
            var page = ConfigPage(host, mod);
            StringAssert.Contains(page, "last reload reason: <b>start</b>", mod);
            StringAssert.Contains(page, "No config errors", mod);
            StringAssert.Contains(page, "<td>" + key + "</td>", mod);
            Assert.AreEqual(404, Get(host, mod, "config", publicOnly: true).Status, mod + ": the overview is for the admin only");
            StringAssert.Contains(Get(host, mod, "").Body, "Configuration: ", mod + ": the mod's own page names its configuration");
        }
        // Deleting the whole config gives the defaults back, and the source says so.
        var seasons = host.StoreOf("seasons");
        seasons.Set("config", new Dictionary<string, object?> { ["top_n"] = 4.0 });
        host.Reload("seasons");
        var overview = ConfigPage(host, "seasons");
        StringAssert.Contains(overview, Row("top_n", "storage", "4"));
        StringAssert.Contains(overview, Row("length_days", "default", "30"));
    }

    [TestMethod]
    public void BadConfigFallsBackToTheDefaultsAndIsReported()
    {
        using var folder = new Folder();
        var (host, _) = AllConfigMods(folder);
        foreach (var (mod, key) in ConfigMods)
        {
            host.StoreOf(mod).Set("config", new Dictionary<string, object?> { [key] = "not a number", ["bogus"] = 1.0 });
            Assert.IsTrue(host.Reload(mod));
            Assert.IsNull(host.Mods.Single(m => m.Mod == mod).Error, mod);
            var page = ConfigPage(host, mod);
            StringAssert.Contains(page, key + ": needs a number", mod);
            StringAssert.Contains(page, "default kept", mod);
            StringAssert.Contains(page, "unknown field bogus", mod);
            StringAssert.Contains(page, $"<td>{key}</td><td>default</td>", mod);
            Assert.AreEqual(200, Get(host, mod, "").Status, mod + ": the mod works with the defaults");

            host.StoreOf(mod).Set("config", "garbage");
            Assert.IsTrue(host.Reload(mod));
            StringAssert.Contains(ConfigPage(host, mod), "is not a table", mod);
            Assert.AreEqual(200, Get(host, mod, "").Status, mod);
        }
        Assert.AreEqual(0, host.Errors);
    }

    [TestMethod]
    public void AConfigEditIsAppliedLiveWithoutAReload()
    {
        using var folder = new Folder();
        var (host, server) = AllConfigMods(folder);
        foreach (var (mod, key) in ConfigMods)
        {
            host.StoreOf(mod).Set("config", new Dictionary<string, object?> { [key] = 17.0 });
            host.StoreOf(mod).Set("config_rev", 1.0);                        // what POST /api/mod-store does
        }
        host.Pump(server.Now += 2100);
        foreach (var (mod, key) in ConfigMods)
        {
            var page = ConfigPage(host, mod);
            StringAssert.Contains(page, Row(key, "storage", "17"), mod);
            StringAssert.Contains(page, "last live edit applied", mod);
            StringAssert.Contains(page, "last reload reason: <b>start</b>", mod);
        }
        Assert.AreEqual(0, host.Errors);
    }

    [TestMethod]
    public void AReloadCountsAsAReloadAndAShutdownAsARestart()
    {
        using var folder = new Folder();
        var (host, _) = AllConfigMods(folder);
        Assert.IsTrue(host.Reload("graveyard"));
        StringAssert.Contains(ConfigPage(host, "graveyard"), "last reload reason: <b>reload</b>");
        StringAssert.Contains(ConfigPage(host, "seasons"), "last reload reason: <b>start</b>", "the other mods were not reloaded");
        host.Shutdown(); host.FlushStores(wait: true); host.Dispose();
        var (again, _) = AllConfigMods(folder);
        StringAssert.Contains(ConfigPage(again, "graveyard"), "last reload reason: <b>restart</b>");
    }

    // ---- state across a reload -------------------------------------------------------------------------------------------------

    [TestMethod]
    public void ServerStatsKeepsItsCountsAndUptimeOverAReloadAndTakesItsLimitsFromTheConfig()
    {
        using var folder = new Folder();
        folder.Write("server_stats.lua", Example("server_stats.lua"));
        var (host, server, store) = Host(folder, mod: "server_stats");
        var alice = server.All[0] with { Character = "Alice" };
        host.FirePlayer("join", alice);
        for (uint i = 0; i < 3; i++) host.FireKill(alice, new ScriptMonster(100 + i, 38, 5, "Orc Warrior"));
        host.FireDeath(alice, null, new ScriptMonster(9000, 38, 12));
        var before = Json(Get(host, "server_stats", "stats").Body);
        store.Set("up_since", before.GetProperty("tracking_since").GetDouble() - 5000);       // the server has been up for a while
        host.Reload("server_stats");                                          // nothing was flushed yet (every 30 s): the unload hook saves
        var after = Json(Get(host, "server_stats", "stats").Body);
        Assert.AreEqual(3, after.GetProperty("totals").GetProperty("kills").GetInt32());
        Assert.AreEqual("Orc Warrior", after.GetProperty("creatures")[0].GetProperty("creature").GetString());
        Assert.AreEqual(3, after.GetProperty("creatures")[0].GetProperty("kills").GetInt32());
        Assert.AreEqual(1, after.GetProperty("activity").EnumerateArray().Sum(h => h.GetProperty("joins").GetInt32()), "the hourly buckets survived");
        Assert.IsTrue(after.GetProperty("uptime").GetInt32() >= 5000, "a reload is not a restart: the uptime goes on");
        Assert.AreEqual(1, host.Mods.Count(m => m.Mod == "server_stats"));

        // A limit from the config: only one creature row.
        store.Set("config", new Dictionary<string, object?> { ["top_creatures"] = 1.0 });
        host.Reload("server_stats");
        host.FireKill(alice, new ScriptMonster(200, 551, 1, "Horse"));
        Assert.AreEqual(1, Json(Get(host, "server_stats", "stats").Body).GetProperty("creatures").GetArrayLength());
    }

    [TestMethod]
    public void HallOfFameKeepsItsRecordsOverAReloadAndTheConfigTurnsAnnouncementsOff()
    {
        using var folder = new Folder();
        folder.Write("hall_of_fame.lua", Example("hall_of_fame.lua"));
        var (host, server, store) = Host(folder, mod: "hall_of_fame");
        store.Set("config", new Dictionary<string, object?> { ["announce_min"] = new Dictionary<string, object?> { ["level"] = 5.0 } });
        host.Reload("hall_of_fame");
        var alice = server.All[0] with { Name = "acct.Alice", Character = "Alice", Key = "id:1:1", Level = 10 };
        var bob = alice with { Slot = 2, Character = "Bob", Key = "id:2:1", Level = 11 };
        host.FirePlayer("enter", alice);
        host.Reload("hall_of_fame");
        host.FirePlayer("enter", bob);
        CollectionAssert.Contains(server.Said, "Bob set a new Hall of Fame record: highest level 11 - beating Alice!", "the record of Alice was still there");
        var records = Json(Get(host, "hall_of_fame", "records").Body);
        Assert.AreEqual("Bob", records.EnumerateArray().Single(r => r.GetProperty("record").GetString() == "level").GetProperty("hero").GetString());

        server.Said.Clear();
        store.Set("config", new Dictionary<string, object?> { ["announce"] = false, ["announce_min"] = new Dictionary<string, object?> { ["level"] = 5.0 } });
        store.Set("config_rev", 1.0);
        host.Pump(server.Now += 2100);
        host.FirePlayer("enter", alice with { Level = 30 });
        Assert.AreEqual(0, server.Said.Count, "announce = false: records are kept, nobody is told");
        Assert.AreEqual("Alice", Json(Get(host, "hall_of_fame", "records").Body).EnumerateArray().Single(r => r.GetProperty("record").GetString() == "level").GetProperty("hero").GetString());
    }

    [TestMethod]
    public void SeasonsCarryOnThroughAReloadWithTheirStartAndArchive()
    {
        using var folder = new Folder();
        folder.Write("seasons.lua", Example("seasons.lua"));
        var (host, server, store) = Host(folder, mod: "seasons");
        var alice = server.All[0] with { Name = "acct.Alice", Character = "Alice", Key = "id:1:1", Level = 10 };
        host.FireKill(alice, new ScriptMonster(1, 38, 3));
        var first = Json(Get(host, "seasons", "current").Body);
        host.Reload("seasons");
        var second = Json(Get(host, "seasons", "current").Body);
        Assert.AreEqual(first.GetProperty("start").GetDouble(), second.GetProperty("start").GetDouble(), "the season did not begin anew");
        Assert.AreEqual(first.GetProperty("finish").GetDouble(), second.GetProperty("finish").GetDouble());
        Assert.AreEqual(1, second.GetProperty("boards").GetProperty("kills")[0].GetProperty("value").GetInt32());

        Get(host, "seasons", "end-season", publicOnly: false);
        host.Reload("seasons");
        Assert.AreEqual(2, Json(Get(host, "seasons", "current").Body).GetProperty("season").GetInt32());
        Assert.AreEqual(1, Json(Get(host, "seasons", "archive").Body).GetArrayLength());

        // Data: no end date, one board only, no announcement.
        store.Set("config", new Dictionary<string, object?>
        {
            ["length_days"] = 0.0, ["announce"] = false,
            ["boards"] = new List<object?> { new Dictionary<string, object?> { ["id"] = "kills", ["title"] = "Slayers" }, new Dictionary<string, object?> { ["id"] = "nonsense" } },
        });
        host.Reload("seasons");
        server.Said.Clear();
        var current = Json(Get(host, "seasons", "current").Body);
        Assert.AreEqual(JsonValueKind.Null, current.TryGetProperty("finish", out var finish) ? finish.ValueKind : JsonValueKind.Null, "length_days = 0: no end date");
        Assert.IsFalse(current.GetProperty("boards").TryGetProperty("gain", out _));
        StringAssert.Contains(ConfigPage(host, "seasons"), "id is kills, gain or online");
        Get(host, "seasons", "end-season", publicOnly: false);
        Assert.AreEqual(0, server.Said.Count, "announce = false");
        StringAssert.Contains(Get(host, "seasons", "").Body, "Past seasons");
    }

    [TestMethod]
    public void RareDropsAndGraveyardKeepTheirFeedsAndFollowTheirConfig()
    {
        using var folder = new Folder();
        folder.Write("rare_drops.lua", Example("rare_drops.lua"));
        folder.Write("graveyard.lua", Example("graveyard.lua"));
        var (host, server, _) = Host(folder, mod: "rare_drops");
        var alice = server.All[0] with { Name = "acct.Alice", Character = "Alice" };
        server.All[0] = alice;
        ScriptItem Item(uint reference, int rarity) => new(reference, 2501, 33, rarity, 1, 800, "Bow " + reference, 1234, 5000, 7000);
        host.FireKill(alice, new ScriptMonster(9, 38, 12, "Orc"));
        host.FireDrop(alice, Item(1, 12));
        host.FireDeath(alice, null, new ScriptMonster(9000, 38, 12, "Orc"));
        host.Reload("rare_drops"); host.Reload("graveyard");
        Assert.AreEqual(1, Json(Get(host, "rare_drops", "feed").Body).GetArrayLength(), "the feed is still there");
        Assert.AreEqual(1, Json(Get(host, "graveyard", "recent").Body).GetArrayLength());
        Assert.AreEqual(1, Json(Get(host, "graveyard", "deadliest").Body).GetArrayLength());

        // Live edit: only unique drops now, a graveyard of two.
        host.StoreOf("rare_drops").Set("config", new Dictionary<string, object?> { ["min_rarity"] = 15.0, ["announce"] = false });
        host.StoreOf("rare_drops").Set("config_rev", 1.0);
        host.StoreOf("graveyard").Set("config", new Dictionary<string, object?> { ["keep"] = 2.0, ["announce"] = false });
        host.StoreOf("graveyard").Set("config_rev", 1.0);
        host.Pump(server.Now += 2100);
        server.Said.Clear();
        host.FireKill(alice, new ScriptMonster(10, 38, 12, "Orc"));
        host.FireDrop(alice, Item(2, 12));                                   // a rare: below the new minimum
        host.FireKill(alice, new ScriptMonster(11, 38, 12, "Orc"));
        host.FireDrop(alice, Item(3, 15));
        Assert.AreEqual(2, Json(Get(host, "rare_drops", "feed").Body).GetArrayLength());
        for (uint i = 0; i < 3; i++) host.FireDeath(alice, null, new ScriptMonster(9100 + i, 38, 12, "Orc"));
        Assert.AreEqual(2, Json(Get(host, "graveyard", "recent").Body).GetArrayLength(), "keep = 2");
        Assert.AreEqual(0, server.Said.Count, "announce = false");
    }

    [TestMethod]
    public void AchievementsKeepTheirRosterAndTheRetryQueueOverAReloadAndTheListIsData()
    {
        using var folder = new Folder();
        folder.Write("achievements.lua", Example("achievements.lua"));
        var (host, server, store) = Host(folder, mod: "achievements");
        store.Set("config", new Dictionary<string, object?>
        {
            ["achievements"] = new List<object?>
            {
                new Dictionary<string, object?> { ["id"] = "pair", ["name"] = "Pair", ["desc"] = "Two kills.", ["stat"] = "kills", ["target"] = 2.0, ["reward"] = new Dictionary<string, object?> { ["gold"] = 7.0 } },
                new Dictionary<string, object?> { ["id"] = "bad", ["name"] = "Bad", ["desc"] = "x", ["stat"] = "nonsense", ["target"] = 1.0 },
            },
            ["reward_tries"] = 3.0,
        });
        host.Reload("achievements");
        var page = ConfigPage(host, "achievements");
        StringAssert.Contains(page, "stat must be one of");
        var list = Json(Get(host, "achievements", "achievements").Body);
        Assert.AreEqual(1, list.GetArrayLength(), "the valid entry is used, the invalid one is dropped");
        Assert.AreEqual("pair", list[0].GetProperty("id").GetString());

        var alice = server.All[0];
        server.GoldFails = true;                                             // the reward cannot be dropped yet: it waits in the retry queue
        host.FireKill(alice, new ScriptMonster(1, 38, 3)); host.FireKill(alice, new ScriptMonster(2, 38, 3));
        Assert.AreEqual(0, server.Gold.Count);
        Assert.IsNotNull(Hero(host, store, "ach.pending"), "queued: the reward waits in the retry queue");
        host.Reload("achievements");
        var players = Json(Get(host, "achievements", "players").Body);
        Assert.AreEqual("Alice", players[0].GetProperty("character").GetString(), "the roster survived (saved by the unload hook)");
        Assert.AreEqual("pair", players[0].GetProperty("achievements")[0].GetString());
        server.GoldFails = false;
        host.Pump(server.Now += 21_000);                                     // the 20 s tend: the retry queue goes on in the new version
        Assert.AreEqual(1, server.Gold.Count(g => g == 7), "paid once, after the reload (the daily login reward of the first tick is another pile)");
        host.Pump(server.Now += 21_000);
        Assert.AreEqual(1, server.Gold.Count(g => g == 7), "and not twice");
        Assert.AreEqual(0, host.Errors);
    }
}
