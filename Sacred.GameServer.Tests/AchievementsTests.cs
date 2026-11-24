using System.Text.Json;
using Sacred.GameServer.Scripting;

namespace Sacred.GameServer.Tests;

/// <summary>scripts-examples/achievements.lua against the fake server (events, rewards, streak, page); the live net test is in
/// AchievementsNetTests.cs.</summary>
public partial class ScriptTests
{
    private static long Today() => DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400;

    private static ScriptHost AchievementsHost(Folder folder, out FakeServer server, out ModStore store, ScriptPlayer? player = null)
    {
        folder.Write("achievements.lua", Example("achievements.lua"));
        var (host, fake, mod) = Host(folder, mod: "achievements");
        if (player is not null) { fake.All.Clear(); fake.All.Add(player); }
        Assert.IsNull(host.Files.Single().Error, "the mod loads (without custom net messages too)");
        server = fake; store = mod;
        return host;
    }

    private static object? Hero(ScriptHost host, ModStore store, string key, string owner = "name:Alice")
    {
        store.TryGetHero(host.Uid(owner), key, out var value);
        return value;
    }

    [TestMethod]
    public void AchievementsUnlockFromKillsAndPayTheirReward()
    {
        using var folder = new Folder();
        var host = AchievementsHost(folder, out var server, out var store);
        var alice = server.All[0];
        host.FireKill(alice, new ScriptMonster(4000, 551, 3));
        Assert.IsTrue(server.Told.Any(t => t.Text.StartsWith("Achievement unlocked: First Blood")), string.Join("|", server.Told.Select(t => t.Text)));
        CollectionAssert.Contains(server.Gold, 50u, "the reward is a gold pile next to the hero");
        var unlocked = (Dictionary<string, object?>)Hero(host, store, "ach.u")!;
        Assert.IsTrue(unlocked.ContainsKey("kills_1"));
        // Further kills: counted, the same achievement is not paid twice; two creature types so far.
        server.Told.Clear(); server.Gold.Clear();
        host.FireKill(alice, new ScriptMonster(4001, 551, 3));
        host.FireKill(alice, new ScriptMonster(4002, 552, 3));
        Assert.AreEqual(3.0, Hero(host, store, "ach.kills"));
        Assert.AreEqual(2.0, Hero(host, store, "ach.species"));
        Assert.AreEqual(0, server.Gold.Count);
        // A creature 10 levels above the hero (level 10) is a giant.
        host.FireKill(alice, new ScriptMonster(4003, 553, 25));
        Assert.AreEqual(1.0, Hero(host, store, "ach.giants"));
        Assert.IsTrue(server.Told.Any(t => t.Text.Contains("Giant Slayer")));
        // The hero's level (10) was noted: "Apprentice" is unlocked by the first event that sees it.
        Assert.IsTrue(((Dictionary<string, object?>)Hero(host, store, "ach.u")!).ContainsKey("level_10"));
        Assert.AreEqual(0, host.Errors);
    }

    [TestMethod]
    public void ItemRewardsAndDeathsAndPvpCount()
    {
        using var folder = new Folder();
        var host = AchievementsHost(folder, out var server, out var store);
        var alice = server.All[0];
        var bob = new ScriptPlayer(2, "Bob", "name:Bob", 1, 30, true, false, 0);
        server.All.Add(bob);
        host.FireDeath(alice, bob, null);                     // Alice dies to Bob
        host.FireDeath(alice, null, new ScriptMonster(9000, 38, 12));
        Assert.AreEqual(2.0, Hero(host, store, "ach.deaths"));
        Assert.AreEqual(1.0, Hero(host, store, "ach.pvp", "name:Bob"));
        Assert.IsTrue(server.Told.Any(t => t.Slot == 1 && t.Text.Contains("A Taste of Death")));
        Assert.IsTrue(server.Told.Any(t => t.Slot == 2 && t.Text.Contains("Duelist")));
        // "Slayer" pays gold and an item.
        store.SetHero(host.Uid("name:Alice"), "Alice", "ach.kills", 999.0);
        host.FireKill(alice, new ScriptMonster(5000, 551, 3));
        Assert.IsTrue(server.Drops.Any(d => d.Quality == 3u), "the item reward");
        CollectionAssert.Contains(server.Gold, 1000u);
        CollectionAssert.Contains(server.Said, "Alice earned the achievement \"Slayer\"!");
    }

    [TestMethod]
    public void LoginStreakGrowsResetsAndPaysOncePerDay()
    {
        using var folder = new Folder();
        var host = AchievementsHost(folder, out var server, out var store);
        var alice = server.All[0];
        var uid = host.Uid("name:Alice");
        // Yesterday was day 2 of the streak: today is day 3 (300 gold), "On a Roll" unlocks.
        store.SetHero(uid, "Alice", "ach.last_day", (double)(Today() - 1));
        store.SetHero(uid, "Alice", "ach.streak", 2.0);
        store.SetHero(uid, "Alice", "ach.best", 2.0);
        store.SetHero(uid, "Alice", "ach.days", 2.0);
        host.FirePlayer("enter", alice);
        Assert.AreEqual(3.0, Hero(host, store, "ach.streak"));
        Assert.AreEqual(3.0, Hero(host, store, "ach.best"));
        Assert.AreEqual(3.0, Hero(host, store, "ach.days"));
        CollectionAssert.Contains(server.Gold, 300u);
        Assert.IsTrue(server.Told.Any(t => t.Text.StartsWith("Daily login: day 3")));
        Assert.IsTrue(server.Told.Any(t => t.Text.Contains("On a Roll")));
        // The same day again: nothing more.
        server.Gold.Clear(); server.Told.Clear();
        host.FirePlayer("enter", alice);
        host.Pump(server.Now += 25_000);
        Assert.AreEqual(3.0, Hero(host, store, "ach.streak"));
        Assert.AreEqual(0, server.Gold.Count);
        Assert.IsFalse(server.Told.Any(t => t.Text.StartsWith("Daily login")));
        // "!streak" tells the numbers and tomorrow's reward.
        Assert.IsTrue(host.FireChat(alice, "!streak"));
        StringAssert.Contains(server.Told.Last().Text, "Login streak: 3 day(s), best 3");
        StringAssert.Contains(server.Told.Last().Text, "Tomorrow: 400 gold");
        // A missed day starts again at 1 (the best stays).
        store.SetHero(uid, "Alice", "ach.last_day", (double)(Today() - 3));
        server.Gold.Clear();
        host.FirePlayer("enter", alice);
        Assert.AreEqual(1.0, Hero(host, store, "ach.streak"));
        Assert.AreEqual(3.0, Hero(host, store, "ach.best"));
        Assert.AreEqual(4.0, Hero(host, store, "ach.days"));
        CollectionAssert.AreEqual(new[] { 100u }, server.Gold);
        // The 7th day brings an item as well.
        store.SetHero(uid, "Alice", "ach.last_day", (double)(Today() - 1));
        store.SetHero(uid, "Alice", "ach.streak", 6.0);
        server.Gold.Clear(); server.Drops.Clear();
        host.FirePlayer("enter", alice);
        Assert.AreEqual(7.0, Hero(host, store, "ach.streak"));
        Assert.IsTrue(server.Drops.Any(d => d.Quality == 5u), "an item on day 7");
        Assert.AreEqual(0, host.Errors);
    }

    [TestMethod]
    public void RewardsWaitUntilTheHeroIsInTheWorld()
    {
        using var folder = new Folder();
        var host = AchievementsHost(folder, out var server, out _, new ScriptPlayer(1, "Alice", "name:Alice", 2, 10, false, true, 0));
        host.FireKill(server.All[0], new ScriptMonster(4000, 551, 3));
        Assert.AreEqual(0, server.Gold.Count, "not in the world: nothing is dropped yet");
        server.All[0] = server.All[0] with { InGame = true };
        host.Pump(server.Now += 25_000);                       // the 20 s timer pays what is held (and the day's login reward)
        CollectionAssert.Contains(server.Gold, 50u);
        CollectionAssert.Contains(server.Gold, 100u);
    }

    [TestMethod]
    public void AchievementsPageAndEndpointsEscapeNamesAndAreCharacterOnly()
    {
        using var folder = new Folder();
        var host = AchievementsHost(folder, out var server, out _, new ScriptPlayer(1, "evil.<script>alert(1)</script>", "lobby:5", 2, 12, true, true, 5, 0, "<script>alert(1)</script>"));
        host.FireKill(server.All[0], new ScriptMonster(4000, 551, 3));
        var page = host.HandleRequest("achievements", "", [], publicOnly: true);
        Assert.AreEqual(200, page.Status);
        StringAssert.Contains(page.Body, "&lt;script&gt;alert(1)&lt;/script&gt;");
        Assert.IsFalse(page.Body.Contains("<script>"), "player text never becomes markup");
        StringAssert.Contains(page.Body, "First Blood");
        var players = host.HandleRequest("achievements", "players", [], publicOnly: true);
        Assert.AreEqual(200, players.Status);
        var root = JsonDocument.Parse(players.Body).RootElement;
        Assert.AreEqual(1, root.GetArrayLength());
        Assert.AreEqual("<script>alert(1)</script>", root[0].GetProperty("character").GetString());
        Assert.IsFalse(players.Body.Contains("lobby") || players.Body.Contains("evil."), "no account part, lobby id or uid");
        CollectionAssert.Contains(root[0].GetProperty("achievements").EnumerateArray().Select(e => e.GetString()).ToArray(), "kills_1");
        var list = JsonDocument.Parse(host.HandleRequest("achievements", "achievements", [], publicOnly: true).Body).RootElement;
        Assert.IsTrue(list.GetArrayLength() >= 25);
    }
}
