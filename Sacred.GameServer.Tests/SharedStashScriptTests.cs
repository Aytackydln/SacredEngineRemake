using System.Text.Json;
using Sacred.GameServer.Scripting;

namespace Sacred.GameServer.Tests;

/// <summary>scripts-examples/shared_stash.lua (the shared stash of the Sacred Gear mod shared-stash, kept per lobby account by the server
/// in Closed Net) against a fake server: nothing outside authoritative mode, a deposit that needs the item seen leaving and the hero saved,
/// a withdrawal that needs the item seen arriving and the hero saved, repeated request ids, the stash shared by the heroes of one account,
/// a failed hero save, and the recovery after a restart.</summary>
[TestClass]
public class SharedStashScriptTests
{
    private const uint Channel = 0x5A5A;
    private static readonly string Record = string.Concat(Enumerable.Range(0, 130).Select(i => (i % 251).ToString("x2")));
    private static readonly string Item = "2a010000" + "0000" + Record + "deadbeef";

    private sealed class Folder : IDisposable
    {
        public readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sacred-ss-" + Guid.NewGuid().ToString("N"));
        public string Scripts => System.IO.Path.Combine(Path, "scripts");
        public string Data => System.IO.Path.Combine(Path, "data");
        public Folder() { Directory.CreateDirectory(Scripts); }
        public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
    }

    private sealed class Fake : IScriptServer
    {
        public long Now;
        public readonly List<(int Slot, JsonElement Message)> Sent = new();
        // Alice (slot 1) and Carol (slot 3) are two heroes of lobby account 501, Bob (slot 2) is account 502, Dan (4) has no lobby identity
        public readonly List<ScriptPlayer> All = [
            new ScriptPlayer(1, "501.Alice", "id:501:1", 2, 10, true, true, 501), new ScriptPlayer(2, "502.Bob", "id:502:1", 1, 12, true, false, 502),
            new ScriptPlayer(3, "501.Carol", "id:501:2", 1, 12, true, false, 501), new ScriptPlayer(4, "Dan", "name:Dan", 1, 12, true, false, 0)];
        public long NowMs => Now;
        public IReadOnlyList<ScriptPlayer> Players() => All;
        public ScriptPlayer? Player(int slot) => All.FirstOrDefault(p => p.Slot == slot);
        public void Broadcast(string text) { }
        public void Message(int slot, string text) { }
        public void Kick(int slot, string reason) { }
        public ScriptDrop? DropItem(int slot, in ScriptLootRequest request, out string? error) { error = "no"; return null; }
        public ScriptDrop? DropGold(int slot, uint amount, out string? error) { error = "no"; return null; }
        public uint NetRegister(string name, bool serverOnly) { Assert.AreEqual("shared-stash", name); Assert.IsTrue(serverOnly); return Channel; }
        public bool NetSupports(int slot) => true;
        public int NetSend(int slot, uint channel, bool json, byte[] data, out string? error)
        {
            error = null;
            Sent.Add((slot, JsonDocument.Parse(data).RootElement.Clone()));
            return 1;
        }
        public bool Authoritative = true;
        public readonly Dictionary<(int, uint), ScriptHeroItem> Items = new();
        public readonly HashSet<(int, uint)> Claimed = new();
        public Func<int, Task<string?>> Save = _ => Task.FromResult<string?>(null);
        public int Saves;
        public double? UploadTime;
        public bool HeroSavedByServer(int slot) => Authoritative;
        public ScriptHero? Hero(int slot) => null;
        public ScriptHeroItem? HeroItem(int slot, uint reference) => Items.TryGetValue((slot, reference), out var item) ? item : null;
        public IReadOnlyList<ScriptHeroItem> HeroItems(int slot) => Items.Where(i => i.Key.Item1 == slot && i.Value.Where != "gone").Select(i => i.Value).ToList();
        public bool ClaimGoneItem(int slot, uint reference) => Claimed.Add((slot, reference));
        public Task<string?> SaveHeroNow(int slot) { Saves++; return Save(slot); }
        public double? HeroUploadTime(int slot) => UploadTime;
        public ScriptPlayer Alice => All[0];
        public ScriptPlayer Bob => All[1];
        public ScriptPlayer Carol => All[2];
        public ScriptPlayer Dan => All[3];
    }

    private static string Source()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Sacred.GameServer", "scripts-examples", "shared_stash.lua");
            if (File.Exists(candidate)) return File.ReadAllText(candidate).Replace("verify_seconds = 30,", "verify_seconds = 1,").Replace("retry_seconds = 15,", "retry_seconds = 1,");
        }
        throw new FileNotFoundException("shared_stash.lua");
    }

    private static ScriptHost Start(Folder folder, Fake server)
    {
        File.WriteAllText(Path.Combine(folder.Scripts, "shared_stash.lua"), Source());
        var host = new ScriptHost(new ScriptOptions { Directory = folder.Scripts, DataDirectory = folder.Data }, server);
        host.Load();
        Assert.IsNull(host.Files.Single().Error, host.Files.Single().Error);
        Settle(host, server);
        return host;
    }

    private static void Settle(ScriptHost host, Fake server, int pumps = 2)
    {
        for (var i = 0; i < pumps; i++) { host.WaitDurable(); host.Pump(server.Now += 600); }
        host.WaitDurable();
    }

    private static void Wait(ScriptHost host, Fake server, Func<bool> done, double seconds = 4)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!done() && DateTime.UtcNow < until) { Thread.Sleep(50); host.Pump(server.Now += 600); host.WaitDurable(); }
    }

    private static int _rid;
    private static string Rid() => "rid" + Interlocked.Increment(ref _rid);

    private static List<JsonElement> Ask(ScriptHost host, Fake server, ScriptPlayer player, object message)
    {
        server.Sent.Clear();
        Assert.IsTrue(host.FireNet(Channel, player, true, JsonSerializer.SerializeToUtf8Bytes(message)));
        Settle(host, server);
        return To(server, player.Slot);
    }

    private static List<JsonElement> To(Fake server, int slot) => server.Sent.Where(s => s.Slot == slot).Select(s => s.Message).ToList();
    private static JsonElement Answer(List<JsonElement> answers, string op) => answers.Last(a => a.GetProperty("op").GetString() == op);
    private static bool Ok(JsonElement answer) => answer.TryGetProperty("ok", out var ok) && ok.GetBoolean();
    private static bool Has(Fake server, int slot, string op) => To(server, slot).Any(a => a.GetProperty("op").GetString() == op);
    private static List<JsonElement> Entries(ScriptHost host, Fake server, ScriptPlayer p) =>
        Ask(host, server, p, new { op = "list" }).Where(a => a.GetProperty("op").GetString() == "entry").ToList();
    private static object Deposit(string rid, uint reference, string? data = null) =>
        new { op = "deposit", rid, @ref = reference, data = data ?? Item, name = "Sword of Tests", quality = "rare", w = 1, h = 3 };

    private static void Deposited(ScriptHost host, Fake server, ScriptPlayer p, uint reference, string? rid = null)
    {
        rid ??= Rid();
        server.Items[(p.Slot, reference)] = new ScriptHeroItem(reference, 0x12A, "gone", Record);
        server.Sent.Clear();
        Assert.IsTrue(host.FireNet(Channel, p, true, JsonSerializer.SerializeToUtf8Bytes(Deposit(rid, reference))));
        Wait(host, server, () => Has(server, p.Slot, "result"));
        Assert.IsTrue(Ok(Answer(To(server, p.Slot), "result")), string.Join(" ", To(server, p.Slot)));
    }

    [TestMethod]
    public void OutsideClosedNetTheServerOffersNoStash()
    {
        using var folder = new Folder();
        var server = new Fake { Authoritative = false };
        var host = Start(folder, server);
        var info = Answer(Ask(host, server, server.Alice, new { op = "info" }), "info");
        Assert.IsFalse(info.GetProperty("closed").GetBoolean());
        server.Items[(1, 501)] = new ScriptHeroItem(501, 0x12A, "gone", Record);
        var answers = Ask(host, server, server.Alice, Deposit(Rid(), 501));
        Assert.IsFalse(Answer(answers, "info").GetProperty("closed").GetBoolean(), "a deposit gets the same 'not offered' answer");
        Assert.AreEqual(0, server.Saves);
        Assert.AreEqual(0, server.Claimed.Count, "the item was not claimed");
        // a player without a lobby identity has no account to keep a stash for, in Closed Net either
        server.Authoritative = true;
        Assert.IsFalse(Answer(Ask(host, server, server.Dan, new { op = "info" }), "info").GetProperty("closed").GetBoolean());
        host.Dispose();
    }

    [TestMethod]
    public void DepositCountsOnlyWhenTheItemLeftTheHeroAndTheHeroIsSaved()
    {
        using var folder = new Folder();
        var server = new Fake();
        var host = Start(folder, server);
        Assert.IsTrue(Answer(Ask(host, server, server.Alice, new { op = "info" }), "info").GetProperty("closed").GetBoolean());

        // still in the backpack: no answer, nothing stored; at the deadline refused with the keep flag
        server.Items[(1, 501)] = new ScriptHeroItem(501, 0x12A, "backpack", Record);
        server.Sent.Clear();
        host.FireNet(Channel, server.Alice, true, JsonSerializer.SerializeToUtf8Bytes(Deposit(Rid(), 501)));
        Settle(host, server);
        Assert.AreEqual(0, To(server, 1).Count);
        Wait(host, server, () => To(server, 1).Count > 0);
        var kept = Answer(To(server, 1), "result");
        Assert.IsFalse(Ok(kept));
        Assert.IsTrue(kept.GetProperty("keep").GetBoolean());
        Assert.AreEqual(0, Entries(host, server, server.Alice).Count);

        // data of another item: refused, not claimed
        server.Items[(1, 502)] = new ScriptHeroItem(502, 0x12A, "gone", new string('0', 260));
        var fake = Answer(Ask(host, server, server.Alice, Deposit(Rid(), 502)), "result");
        Assert.IsFalse(Ok(fake));
        StringAssert.Contains(fake.GetProperty("err").GetString(), "does not match");
        Assert.AreEqual(0, server.Saves);

        // a good deposit: the hero is saved first, then the entry is ready
        var good = Rid();
        Deposited(host, server, server.Alice, 503, good);
        Assert.AreEqual(1, server.Saves);
        Assert.IsTrue(server.Claimed.Contains((1, 503)));
        var list = Entries(host, server, server.Alice);
        Assert.AreEqual(1, list.Count);
        Assert.AreEqual(Item, list[0].GetProperty("data").GetString());
        // the same request again: the same answer, no second claim or save
        Assert.IsTrue(Ok(Answer(Ask(host, server, server.Alice, Deposit(good, 503)), "result")));
        Assert.AreEqual(1, server.Saves);
        // the same item under a new request id cannot be stashed twice
        Assert.IsFalse(Ok(Answer(Ask(host, server, server.Alice, Deposit(Rid(), 503)), "result")));
        Assert.AreEqual(1, Entries(host, server, server.Alice).Count);

        // the other hero of the account sees it, another account does not
        Assert.AreEqual(1, Entries(host, server, server.Carol).Count);
        Assert.AreEqual(0, Entries(host, server, server.Bob).Count);
        host.Dispose();
    }

    [TestMethod]
    public void AFailedHeroSaveKeepsTheItemPendingUntilASaveGoesThrough()
    {
        using var folder = new Folder();
        var server = new Fake();
        var host = Start(folder, server);
        server.Save = _ => Task.FromResult<string?>("the lobby refused the hero (result 11)");
        server.Items[(1, 510)] = new ScriptHeroItem(510, 0x12A, "gone", Record);
        server.Sent.Clear();
        host.FireNet(Channel, server.Alice, true, JsonSerializer.SerializeToUtf8Bytes(Deposit(Rid(), 510)));
        Wait(host, server, () => Has(server, 1, "result"));
        var failed = Answer(To(server, 1), "result");
        Assert.IsFalse(Ok(failed));
        Assert.IsTrue(failed.GetProperty("retry").GetBoolean());
        Assert.AreEqual(0, Entries(host, server, server.Alice).Count, "not in the stash yet");
        server.Save = _ => Task.FromResult<string?>(null);
        server.Sent.Clear();
        Wait(host, server, () => To(server, 1).Any(a => a.GetProperty("op").GetString() == "result" && Ok(a)), 6);
        Assert.IsTrue(Ok(Answer(To(server, 1), "result")), "the save is tried again on its own");
        Assert.AreEqual(1, Entries(host, server, server.Alice).Count);
        host.Dispose();
    }

    [TestMethod]
    public void WithdrawNeedsTheItemToArriveInTheHeroAndTheHeroSaved()
    {
        using var folder = new Folder();
        var server = new Fake();
        var host = Start(folder, server);
        Deposited(host, server, server.Alice, 520);
        var id = Entries(host, server, server.Alice).Single().GetProperty("id").GetInt32();
        server.Saves = 0;

        // a withdrawal reserves the entry and releases the data; the entry is not listed meanwhile for anyone else to take
        var rid = Rid();
        var release = Answer(Ask(host, server, server.Alice, new { op = "withdraw", rid, id }), "release");
        Assert.AreEqual(Item, release.GetProperty("data").GetString());
        Assert.AreEqual(0, Entries(host, server, server.Carol).Count, "reserved entries are not listed");
        Assert.IsFalse(Ok(Answer(Ask(host, server, server.Carol, new { op = "withdraw", rid = Rid(), id }), "result")), "a second withdrawal of the same entry is refused");

        // the item is not in the hero: no removal, retry answer at the deadline
        server.Sent.Clear();
        host.FireNet(Channel, server.Alice, true, JsonSerializer.SerializeToUtf8Bytes(new { op = "collected", rid, @ref = 900 }));
        Wait(host, server, () => Has(server, 1, "collected"));
        Assert.IsTrue(Answer(To(server, 1), "collected").GetProperty("retry").GetBoolean());
        Assert.AreEqual(0, server.Saves);

        // it arrived: saved, then the entry is gone for good (and for the other hero of the account too)
        server.Items[(1, 900)] = new ScriptHeroItem(900, 0x12A, "backpack", Record);
        var done = Answer(Ask(host, server, server.Alice, new { op = "collected", rid, @ref = 900 }), "collected");
        if (!Ok(done)) { Wait(host, server, () => Has(server, 1, "collected") && Ok(Answer(To(server, 1), "collected"))); done = Answer(To(server, 1), "collected"); }
        Assert.IsTrue(Ok(done), done.ToString());
        Assert.AreEqual(1, server.Saves);
        Assert.AreEqual(0, Entries(host, server, server.Alice).Count);
        Assert.IsTrue(Ok(Answer(Ask(host, server, server.Carol, new { op = "collected", rid, @ref = 900 }), "collected")), "a repeat of the confirmation is answered the same");
        Assert.AreEqual(1, server.Saves);
        Assert.IsFalse(Ok(Answer(Ask(host, server, server.Alice, new { op = "withdraw", rid = Rid(), id }), "result")), "the entry is gone");
        host.Dispose();
    }

    [TestMethod]
    public void AbortGivesAReservedEntryBack()
    {
        using var folder = new Folder();
        var server = new Fake();
        var host = Start(folder, server);
        Deposited(host, server, server.Alice, 530);
        var id = Entries(host, server, server.Alice).Single().GetProperty("id").GetInt32();
        var rid = Rid();
        Ask(host, server, server.Alice, new { op = "withdraw", rid, id });
        Assert.AreEqual(0, Entries(host, server, server.Alice).Count);
        Assert.IsTrue(Ok(Answer(Ask(host, server, server.Alice, new { op = "abort", rid }), "result")));
        Assert.AreEqual(1, Entries(host, server, server.Alice).Count);
        host.Dispose();
    }

    [TestMethod]
    public void TheStashSurvivesARestartAndHalfDoneWithdrawalsAreSettled()
    {
        using var folder = new Folder();
        var server = new Fake();
        var host = Start(folder, server);
        Deposited(host, server, server.Alice, 540);
        Deposited(host, server, server.Alice, 541);
        var ids = Entries(host, server, server.Alice).Select(e => e.GetProperty("id").GetInt32()).ToList();
        Assert.AreEqual(2, ids.Count);
        Ask(host, server, server.Alice, new { op = "withdraw", rid = Rid(), id = ids[0] });
        host.Dispose();

        // restart, no hero upload since the reservation: the entry is back
        Thread.Sleep(1100);
        var server2 = new Fake();
        var host2 = Start(folder, server2);
        Assert.AreEqual(2, Entries(host2, server2, server2.Carol).Count, "the stash is the server's: another hero of the account sees it after a restart");
        var rid = Rid();
        Ask(host2, server2, server2.Alice, new { op = "withdraw", rid, id = ids[0] });
        host2.Dispose();

        // restart again; the hero was uploaded after the reservation and has the item: the entry is gone
        Thread.Sleep(1100);
        var server3 = new Fake { UploadTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 5 };
        server3.Items[(1, 902)] = new ScriptHeroItem(902, 0x12A, "backpack", Record);
        var host3 = Start(folder, server3);
        Assert.AreEqual(1, Entries(host3, server3, server3.Alice).Count);
        host3.Dispose();
    }

    [TestMethod]
    public void AnIdenticalItemAlreadyInTheHeroIsNotTheArrival()
    {
        using var folder = new Folder();
        var server = new Fake();
        var host = Start(folder, server);
        Deposited(host, server, server.Alice, 560);
        var id = Entries(host, server, server.Alice).Single().GetProperty("id").GetInt32();
        server.Items[(1, 800)] = new ScriptHeroItem(800, 0x12A, "backpack", Record);          // the hero already owns an identical item
        var rid = Rid();
        server.Saves = 0;
        Ask(host, server, server.Alice, new { op = "withdraw", rid, id });
        // the client "reports" the old item as the arrival: refused, and the entry stays reserved
        var refused = Answer(Ask(host, server, server.Alice, new { op = "collected", rid, @ref = 800 }), "collected");
        Assert.IsFalse(Ok(refused));
        StringAssert.Contains(refused.GetProperty("err").GetString(), "before the withdrawal");
        Assert.AreEqual(0, server.Saves);
        // the real new item (another id) is accepted
        server.Items[(1, 801)] = new ScriptHeroItem(801, 0x12A, "backpack", Record);
        var done = Answer(Ask(host, server, server.Alice, new { op = "collected", rid, @ref = 801 }), "collected");
        if (!Ok(done)) { Wait(host, server, () => Has(server, 1, "collected") && Ok(Answer(To(server, 1), "collected"))); done = Answer(To(server, 1), "collected"); }
        Assert.IsTrue(Ok(done), done.ToString());
        host.Dispose();
    }

    [TestMethod]
    public void AtARestartIdenticalItemsAreCountedNotJustFound()
    {
        using var folder = new Folder();
        var server = new Fake();
        var host = Start(folder, server);
        Deposited(host, server, server.Alice, 570);
        var id = Entries(host, server, server.Alice).Single().GetProperty("id").GetInt32();
        server.Items[(1, 810)] = new ScriptHeroItem(810, 0x12A, "backpack", Record);          // one identical item before the withdrawal
        Ask(host, server, server.Alice, new { op = "withdraw", rid = Rid(), id });
        host.Dispose();

        // restart: a hero upload went out after the reservation, the saved hero has still ONE identical item: the withdrawal did not reach it
        Thread.Sleep(1100);
        var server2 = new Fake { UploadTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 5 };
        server2.Items[(1, 811)] = new ScriptHeroItem(811, 0x12A, "backpack", Record);
        var host2 = Start(folder, server2);
        Assert.AreEqual(1, Entries(host2, server2, server2.Alice).Count, "one identical item was there before: the entry is back");
        Ask(host2, server2, server2.Alice, new { op = "withdraw", rid = Rid(), id });
        host2.Dispose();

        // with the hero holding two now (and two before this reservation's count of one) the entry is gone
        Thread.Sleep(1100);
        var server3 = new Fake { UploadTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 5 };
        server3.Items[(1, 812)] = new ScriptHeroItem(812, 0x12A, "backpack", Record);
        server3.Items[(1, 813)] = new ScriptHeroItem(813, 0x12A, "backpack", Record);
        var host3 = Start(folder, server3);
        Assert.AreEqual(0, Entries(host3, server3, server3.Alice).Count, "two now, none before this reservation: it arrived");
        host3.Dispose();
    }

    [TestMethod]
    public void AReloadMidDepositLosesNothingAndDuplicatesNothing()
    {
        using var folder = new Folder();
        var server = new Fake();
        var host = Start(folder, server);
        // the hero save is out (never answered) when the mod is reloaded: the item is gone and claimed, the entry is pending
        var never = new TaskCompletionSource<string?>();
        server.Save = _ => never.Task;
        server.Items[(1, 580)] = new ScriptHeroItem(580, 0x12A, "gone", Record);
        var rid = Rid();
        server.Sent.Clear();
        host.FireNet(Channel, server.Alice, true, JsonSerializer.SerializeToUtf8Bytes(Deposit(rid, 580)));
        Wait(host, server, () => server.Saves > 0);
        Assert.AreEqual(1, server.Saves);
        Assert.IsTrue(host.Reload("shared_stash"));
        Settle(host, server);
        Assert.AreEqual(0, Entries(host, server, server.Alice).Count, "not in the stash yet: the save was not confirmed");
        // the client resends the same request: resumed from the stored pending entry, saved again, now answered
        server.Save = _ => Task.FromResult<string?>(null);
        server.Sent.Clear();
        host.FireNet(Channel, server.Alice, true, JsonSerializer.SerializeToUtf8Bytes(Deposit(rid, 580)));
        Wait(host, server, () => To(server, 1).Any(a => a.GetProperty("op").GetString() == "result" && Ok(a)));
        Assert.IsTrue(Ok(Answer(To(server, 1), "result")), string.Join(" ", To(server, 1)));
        Assert.AreEqual(1, Entries(host, server, server.Alice).Count, "exactly one entry");
        Assert.AreEqual(1, server.Claimed.Count, "the item was claimed once");
        // and once more after another reload: the same answer, still one entry
        Assert.IsTrue(host.Reload("shared_stash"));
        Settle(host, server);
        Assert.IsTrue(Ok(Answer(Ask(host, server, server.Alice, Deposit(rid, 580)), "result")));
        Assert.AreEqual(1, Entries(host, server, server.Alice).Count);
        host.Dispose();
    }

    [TestMethod]
    public void AReloadMidWithdrawalKeepsTheReservationAndTheConfirmation()
    {
        using var folder = new Folder();
        var server = new Fake();
        var host = Start(folder, server);
        Deposited(host, server, server.Alice, 590);
        var id = Entries(host, server, server.Alice).Single().GetProperty("id").GetInt32();
        var rid = Rid();
        Assert.AreEqual(Item, Answer(Ask(host, server, server.Alice, new { op = "withdraw", rid, id }), "release").GetProperty("data").GetString());
        Assert.IsTrue(host.Reload("shared_stash"));
        Settle(host, server);
        Assert.AreEqual(0, Entries(host, server, server.Carol).Count, "still reserved after the reload (and not a new session: nothing is released)");
        // the client resends the withdrawal: the same release; then the item arrived and is confirmed
        Assert.AreEqual(Item, Answer(Ask(host, server, server.Alice, new { op = "withdraw", rid, id }), "release").GetProperty("data").GetString());
        server.Items[(1, 910)] = new ScriptHeroItem(910, 0x12A, "backpack", Record);
        var done = Answer(Ask(host, server, server.Alice, new { op = "collected", rid, @ref = 910 }), "collected");
        if (!Ok(done)) { Wait(host, server, () => Has(server, 1, "collected") && Ok(Answer(To(server, 1), "collected"))); done = Answer(To(server, 1), "collected"); }
        Assert.IsTrue(Ok(done), done.ToString());
        Assert.AreEqual(0, Entries(host, server, server.Alice).Count);
        // a reload between the confirmation and a resend of it: still answered ok
        Assert.IsTrue(host.Reload("shared_stash"));
        Settle(host, server);
        Assert.IsTrue(Ok(Answer(Ask(host, server, server.Alice, new { op = "collected", rid, @ref = 910 }), "collected")));
        host.Dispose();
    }

    [TestMethod]
    public void TheTunablesAreDataAndAChangeOfTheRevApplies()
    {
        using var folder = new Folder();
        var server = new Fake();
        var host = Start(folder, server);
        var store = host.StoreOf("shared_stash");
        Assert.IsTrue(store.TryGet("config", out _), "the first run wrote the defaults to the storage");
        // a web-UI write (POST /api/mod-store) sets the key and bumps config_rev: no reload needed
        store.Set("config", new Dictionary<string, object?> { ["rate_per_minute"] = 2.0, ["max_items"] = 1.0 });
        store.Set("config_rev", 1.0);
        Wait(host, server, () => false, 1.5);
        Deposited(host, server, server.Alice, 600);
        // max_items = 1: the second deposit is refused as full (the item stays gone: it is claimed only after the check)
        server.Items[(1, 601)] = new ScriptHeroItem(601, 0x12A, "gone", Record);
        var full = Answer(Ask(host, server, server.Alice, Deposit(Rid(), 601)), "result");
        Assert.IsFalse(Ok(full));
        StringAssert.Contains(full.GetProperty("err").GetString(), "full");
        // rate_per_minute = 2: the deposit above and the next ones count; the third operation in the minute is refused
        var third = Answer(Ask(host, server, server.Alice, Deposit(Rid(), 601)), "result");
        Assert.IsFalse(Ok(third));
        StringAssert.Contains(third.GetProperty("err").GetString(), "too many");
        host.Dispose();
    }
}
