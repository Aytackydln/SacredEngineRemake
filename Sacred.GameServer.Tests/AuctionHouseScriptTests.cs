using System.Text;
using System.Text.Json;
using Sacred.GameServer.Scripting;
using Sacred.GameServer.Web;

namespace Sacred.GameServer.Tests;

/// <summary>scripts-examples/auction_house.lua (the Sacred Gear auction house's market in the server) against a fake server: selling,
/// browsing, buying by a second player, collecting, cancelling, expiry, repeated request ids, a failed durable write, a restart, the
/// page and JSON (character names only), and the Closed Net checks (the item or gold seen leaving/arriving, the hero saved).</summary>
[TestClass]
public class AuctionHouseScriptTests
{
    private const uint Channel = 0xA0C7;
    // gear.inv.export of an unsocketed item of type 0x12A: u32 type, u16 n = 0, n + 1 = one 130-byte record (the item's own), then the rest
    private static readonly string Record = string.Concat(Enumerable.Range(0, 130).Select(i => (i % 251).ToString("x2")));
    private static readonly string Item = "2a010000" + "0000" + Record + "deadbeef";
    // the same with n = 2 sockets: the two socketed entries and the item's own record (3 records)
    private static readonly string Socketed = "2a010000" + "0200" + string.Concat(Enumerable.Range(0, 3).Select(k => string.Concat(Enumerable.Range(0, 130).Select(i => ((i + k * 7) % 251).ToString("x2"))))) + "deadbeef";
    private static string RecordsOf(string data, int n) => data.Substring(12, (n + 1) * 260);

    private sealed class Folder : IDisposable
    {
        public readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sacred-ah-" + Guid.NewGuid().ToString("N"));
        public string Scripts => System.IO.Path.Combine(Path, "scripts");
        public string Data => System.IO.Path.Combine(Path, "data");
        public Folder() { Directory.CreateDirectory(Scripts); }
        public void Write(string name, string source) { File.WriteAllText(System.IO.Path.Combine(Scripts, name), source); ExampleAssets.CopyTo(Scripts); }
        public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
    }

    private sealed class Fake : IScriptServer
    {
        public long Now;
        public readonly List<(int Slot, string Text)> Told = new();
        public readonly List<(int Slot, JsonElement Message)> Sent = new();
        public readonly List<ScriptPlayer> All = [new ScriptPlayer(1, "Alice", "name:Alice", 2, 10, true, true, 0), new ScriptPlayer(2, "Bob", "name:Bob", 1, 12, true, false, 0)];
        public long NowMs => Now;
        public IReadOnlyList<ScriptPlayer> Players() => All;
        public ScriptPlayer? Player(int slot) => All.FirstOrDefault(p => p.Slot == slot);
        public void Broadcast(string text) { }
        public void Message(int slot, string text) => Told.Add((slot, text));
        public void Kick(int slot, string reason) { }
        public ScriptDrop? DropItem(int slot, in ScriptLootRequest request, out string? error) { error = "no"; return null; }
        public ScriptDrop? DropGold(int slot, uint amount, out string? error) { error = "no"; return null; }
        public uint NetRegister(string name, bool serverOnly) { Assert.AreEqual("auction", name); Assert.IsTrue(serverOnly); return Channel; }
        public bool NetSupports(int slot) => true;
        public int NetSend(int slot, uint channel, bool json, byte[] data, out string? error)
        {
            error = null;
            Sent.Add((slot, JsonDocument.Parse(data).RootElement.Clone()));
            return 1;
        }
        // Closed Net
        public bool Authoritative;
        public readonly Dictionary<int, long> Gold = new() { [1] = 0, [2] = 0 };
        public readonly Dictionary<(int, uint), ScriptHeroItem> Items = new();
        public readonly HashSet<(int, uint)> Claimed = new();
        public Func<int, Task<string?>> Save = _ => Task.FromResult<string?>(null);
        public int Saves;
        public double? UploadTime;
        public bool HeroSavedByServer(int slot) => Authoritative;
        public ScriptHero? Hero(int slot) => Authoritative
            ? new ScriptHero(new HeroInfo(slot, Player(slot)!.Name, null, 1, 10, 0, Gold[slot], 0, [], [], null, [], 0, [], [], [], true, 0), []) : null;
        public ScriptHeroItem? HeroItem(int slot, uint reference) => Items.TryGetValue((slot, reference), out var item) ? item : null;
        public IReadOnlyList<ScriptHeroItem> HeroItems(int slot) => Items.Where(i => i.Key.Item1 == slot && i.Value.Where != "gone").Select(i => i.Value).ToList();
        public bool ClaimGoneItem(int slot, uint reference) => Claimed.Add((slot, reference));
        public Task<string?> SaveHeroNow(int slot) { Saves++; return Save(slot); }
        public double? HeroUploadTime(int slot) => UploadTime;
        public readonly List<(long Id, int Slot, uint Type, string Records, double Ttl, string Label)> Minted = new();
        public readonly List<long> Revoked = new();
        public bool NoTokens;
        public long MintItemToken(int slot, uint type, string records, double ttlSeconds, string label)
        {
            if (NoTokens || !Authoritative) return 0;
            var id = Minted.Count + 1;
            Minted.Add((id, slot, type, records, ttlSeconds, label));
            return id;
        }
        public bool RevokeItemToken(long id) { Revoked.Add(id); return true; }
        public ScriptPlayer Alice => All[0];
        public ScriptPlayer Bob => All[1];
    }

    private static string Source(Func<string, string>? edit = null)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Sacred.GameServer", "scripts-examples", "auction_house.lua");
            if (File.Exists(candidate)) { var text = File.ReadAllText(candidate); return edit is null ? text : edit(text); }
        }
        throw new FileNotFoundException("auction_house.lua");
    }

    private static (ScriptHost Host, Fake Server) Start(Folder folder, Fake? server = null, Func<string, string>? edit = null)
    {
        server ??= new Fake();
        if (!File.Exists(Path.Combine(folder.Scripts, "auction_house.lua"))) folder.Write("auction_house.lua", Source(edit));
        var host = new ScriptHost(new ScriptOptions { Directory = folder.Scripts, DataDirectory = folder.Data }, server);
        host.Load();
        Assert.IsNull(host.Files.Single().Error, host.Files.Single().Error);
        Settle(host, server);
        return (host, server);
    }

    /// <summary>Lets the durable writes finish and runs the timers a little.</summary>
    private static void Settle(ScriptHost host, Fake server, int pumps = 2)
    {
        for (var i = 0; i < pumps; i++)
        {
            host.WaitDurable();
            host.Pump(server.Now += 600);
        }
        host.WaitDurable();
    }

    private static int _rid;
    private static string Rid() => "r" + Interlocked.Increment(ref _rid);

    private static void Send(ScriptHost host, ScriptPlayer player, object message) =>
        Assert.IsTrue(host.FireNet(Channel, player, true, JsonSerializer.SerializeToUtf8Bytes(message)));

    /// <summary>Sends a request and returns the answers to its sender once everything is written.</summary>
    private static List<JsonElement> Ask(ScriptHost host, Fake server, ScriptPlayer player, object message)
    {
        server.Sent.Clear();
        Send(host, player, message);
        Settle(host, server);
        return server.Sent.Where(s => s.Slot == player.Slot).Select(s => s.Message).ToList();
    }

    private static JsonElement Answer(List<JsonElement> answers, string op) => answers.Last(a => a.GetProperty("op").GetString() == op);
    private static bool Ok(JsonElement answer) => answer.TryGetProperty("ok", out var ok) && ok.GetBoolean();
    private static double Wallet(JsonElement answer) => answer.GetProperty("wallet").GetDouble();

    private static object Sell(string rid, int price = 1000, string name = "Sword of Tests", uint? reference = null, string? data = null) => reference is { } r
        ? new { op = "sell", rid, data = data ?? Item, price, days = 3, @ref = r, meta = new { name, quality = "rare", category = "weapon", level = 12, text = name + "\nDamage 10-20" } }
        : new { op = "sell", rid, data = data ?? Item, price, days = 3, meta = new { name, quality = "rare", category = "weapon", level = 12, text = name + "\nDamage 10-20" } };

    [TestMethod]
    public void SellBrowseBuyCollectAndCancel()
    {
        using var folder = new Folder();
        var (host, server) = Start(folder);
        var hello = Answer(Ask(host, server, server.Alice, new { op = "hello" }), "info");
        Assert.AreEqual("server", hello.GetProperty("where").GetString());
        Assert.IsFalse(hello.TryGetProperty("authoritative", out _), "LAN: the client's journal and crash check apply");

        Assert.AreEqual(100, Wallet(Answer(Ask(host, server, server.Alice, new { op = "deposit", rid = Rid(), gold = 100 }), "result")));
        // the answer waits for the durable write
        server.Sent.Clear();
        Send(host, server.Alice, Sell("s1"));
        Assert.AreEqual(0, server.Sent.Count, "no answer before the write is on disk");
        Settle(host, server);
        var sold = Answer(server.Sent.Select(s => s.Message).ToList(), "result");
        Assert.IsTrue(Ok(sold), sold.ToString());
        Assert.AreEqual(15, sold.GetProperty("fee").GetDouble(), "0.5 % a day, 3 days, of 1000");
        var id = sold.GetProperty("id").GetDouble();

        var page = Answer(Ask(host, server, server.Bob, new { op = "browse", q = new { text = "damage sword" }, sort = "price" }), "page");
        Assert.AreEqual(1, page.GetProperty("total").GetDouble());
        Assert.AreEqual("Alice", page.GetProperty("rows")[0].GetProperty("seller").GetString(), "the seller's character name");

        var refused = Answer(Ask(host, server, server.Bob, new { op = "buy", rid = Rid(), id, price = 1000 }), "result");
        Assert.IsFalse(Ok(refused), "no gold in the wallet");
        Ask(host, server, server.Bob, new { op = "deposit", rid = Rid(), gold = 5000 });
        server.Told.Clear();
        var buyRid = Rid();
        var answers = Ask(host, server, server.Bob, new { op = "buy", rid = buyRid, id, price = 1000 });
        Assert.IsTrue(Ok(Answer(answers, "result")));
        Assert.AreEqual(4000, Wallet(Answer(answers, "result")));
        var notice = server.Sent.Single(s => s.Slot == 1).Message;
        Assert.AreEqual("sold", notice.GetProperty("kind").GetString(), "the seller is told over the channel");
        Assert.IsTrue(server.Told.Single().Text.Contains("sold to Bob for 1000 gold (950 after the cut)"), "and in chat");
        Assert.AreEqual(4000, Wallet(Answer(Ask(host, server, server.Bob, new { op = "buy", rid = buyRid, id, price = 1000 }), "result")), "a repeated rid: the same answer, paid once");
        Assert.AreEqual(85 + 950, Wallet(Answer(Ask(host, server, server.Alice, new { op = "mine" }), "mine")));

        var mail = Answer(Ask(host, server, server.Bob, new { op = "mail" }), "mail").GetProperty("entries");
        Assert.AreEqual(1, mail.GetArrayLength());
        var collectRid = Rid();
        var deliver = Answer(Ask(host, server, server.Bob, new { op = "collect", rid = collectRid, mid = mail[0].GetProperty("mid").GetDouble() }), "deliver");
        Assert.AreEqual(Item, deliver.GetProperty("data").GetString(), "the item's own data");
        Assert.IsTrue(Answer(Ask(host, server, server.Bob, new { op = "collected", rid = collectRid }), "collected").GetProperty("removed").GetBoolean());
        Assert.AreEqual(0, Answer(Ask(host, server, server.Bob, new { op = "mail" }), "mail").GetProperty("entries").GetArrayLength());

        // cancel: the item goes back to the seller's mailbox, the fee stays paid
        var second = Answer(Ask(host, server, server.Alice, Sell(Rid(), 200)), "result");
        Assert.IsTrue(Ok(second));
        Assert.IsTrue(Ok(Answer(Ask(host, server, server.Alice, new { op = "cancel", rid = Rid(), id = second.GetProperty("id").GetDouble() }), "result")));
        var back = Answer(Ask(host, server, server.Alice, new { op = "mail" }), "mail").GetProperty("entries");
        Assert.AreEqual("cancelled", back[0].GetProperty("reason").GetString());
        Assert.AreEqual(0, Answer(Ask(host, server, server.Bob, new { op = "browse" }), "page").GetProperty("total").GetDouble());
    }

    [TestMethod]
    public void AFailedDurableWriteIsRefusedAndUndone()
    {
        using var folder = new Folder();
        var (host, server) = Start(folder);
        Ask(host, server, server.Alice, new { op = "deposit", rid = Rid(), gold = 100 });
        var store = host.StoreOf("auction_house");
        store.FailDurableWrites = () => "disk full";
        var answer = Answer(Ask(host, server, server.Alice, Sell("f1")), "result");
        Assert.IsFalse(Ok(answer));
        StringAssert.Contains(answer.GetProperty("err").GetString(), "could not save");
        store.FailDurableWrites = null;
        Assert.AreEqual(0, Answer(Ask(host, server, server.Bob, new { op = "browse" }), "page").GetProperty("total").GetDouble(), "nothing listed");
        Assert.AreEqual(100, Wallet(Answer(Ask(host, server, server.Alice, new { op = "mine" }), "mine")), "no fee taken");
        // the same rid again after the disk recovered: a fresh attempt (the refusal of a failed write is not remembered)
        Assert.IsTrue(Ok(Answer(Ask(host, server, server.Alice, Sell("f1")), "result")));
    }

    [TestMethod]
    public void ARestartKeepsTheMarketAndExpiredListingsGoBack()
    {
        using var folder = new Folder();
        var (host, server) = Start(folder, edit: text => text.Replace("max_per_seller = 20,", "max_per_seller = 40,"));
        Ask(host, server, server.Alice, new { op = "deposit", rid = Rid(), gold = 500 });
        for (var i = 0; i < 30; i++) Assert.IsTrue(Ok(Answer(Ask(host, server, server.Alice, Sell(Rid(), 100 + i, "Item " + i)), "result")), "listing " + i);
        host.FlushStores(wait: true);
        var file = Path.Combine(folder.Data, "auction_house.json");
        var json = File.ReadAllText(file);
        StringAssert.Contains(json, "\"listings.2\"", "listings are kept in chunks");

        // a restart: everything is back
        var (again, server2) = Start(folder, new Fake());
        var page = Answer(Ask(again, server2, server2.Bob, new { op = "browse" }), "page");
        Assert.AreEqual(30, page.GetProperty("total").GetDouble());
        // expiry: one listing's time is up (edited in the file while the server is down), the start sweep sends it to the mailbox
        again.FlushStores(wait: true);
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
        root["listings.1"]![0]!["ends"] = 1;
        File.WriteAllText(file, root.ToJsonString());
        server2.Told.Clear();
        var (third, server3) = Start(folder, new Fake());
        Assert.AreEqual(29, Answer(Ask(third, server3, server3.Bob, new { op = "browse" }), "page").GetProperty("total").GetDouble());
        var mail = Answer(Ask(third, server3, server3.Alice, new { op = "mail" }), "mail").GetProperty("entries");
        Assert.AreEqual(1, mail.GetArrayLength());
        Assert.AreEqual("expired", mail[0].GetProperty("reason").GetString());
        Assert.IsTrue(server3.Told.Any(t => t.Slot == 1 && t.Text.Contains("did not sell")), "the seller (online) is told in chat");
    }

    [TestMethod]
    public void ThePageAndTheJsonShowCharacterNamesOnly()
    {
        using var folder = new Folder();
        var (host, server) = Start(folder);
        Ask(host, server, server.Alice, new { op = "deposit", rid = Rid(), gold = 100 });
        var listed = Answer(Ask(host, server, server.Alice, Sell(Rid(), 777, "</script><b>Evil</b>")), "result");
        Ask(host, server, server.Bob, new { op = "deposit", rid = Rid(), gold = 1000 });
        Ask(host, server, server.Bob, new { op = "buy", rid = Rid(), id = listed.GetProperty("id").GetDouble(), price = 777 });
        Ask(host, server, server.Alice, Sell(Rid(), 55, "Ring of Tests"));
        var uids = new[] { host.Uid("name:Alice"), host.Uid("name:Bob") };

        var listings = host.HandleRequest("auction_house", "listings", [], publicOnly: true);
        Assert.AreEqual(200, listings.Status, listings.Body);
        var rows = JsonDocument.Parse(listings.Body).RootElement;
        Assert.AreEqual(1, rows.GetArrayLength());
        Assert.AreEqual("Ring of Tests", rows[0].GetProperty("name").GetString());
        Assert.AreEqual("Alice", rows[0].GetProperty("seller").GetString());
        Assert.IsTrue(rows[0].GetProperty("left").GetDouble() > 3 * 86400 - 60);

        var sales = host.HandleRequest("auction_house", "recent-sales", [], publicOnly: true);
        var sale = JsonDocument.Parse(sales.Body).RootElement[0];
        Assert.AreEqual("Bob", sale.GetProperty("buyer").GetString());
        Assert.AreEqual(777, sale.GetProperty("price").GetDouble());

        var page = host.HandleRequest("auction_house", "", [], publicOnly: true);
        Assert.AreEqual(200, page.Status);
        StringAssert.Contains(page.Body, "Ring of Tests");
        Assert.IsFalse(page.Body.Contains("</script><b>Evil"), "player text cannot close the script element");
        foreach (var body in new[] { listings.Body, sales.Body, page.Body })
            foreach (var uid in uids) Assert.IsFalse(body.Contains(uid), "no uids on the web");
    }

    [TestMethod]
    public void AuctionCoreBlockIsUnedited()
    {
        var text = Source().Replace("\r", "");
        var header = text.IndexOf("\n-- >>> core.lua", StringComparison.Ordinal);
        var stop = text.IndexOf("\n-- <<< core.lua", StringComparison.Ordinal);
        Assert.IsTrue(header > 0 && stop > header);
        var line = text[(header + 1)..text.IndexOf('\n', header + 1)];
        var start = text.IndexOf('\n', text.IndexOf('\n', header + 1) + 1) + 1;     // after the "Regenerate:" line
        var block = text[start..(stop + 1)];
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(block)) { hash ^= b; hash *= 16777619u; }
        StringAssert.EndsWith(line, "fnv1a " + hash.ToString("x8"), "edit core.lua in sacred-gear-mods and regenerate (tools/inline_core.lua)");
    }

    // ---- Closed Net -------------------------------------------------------------------------------------------------------------

    private static Func<string, string> FastChecks => text => text.Replace("verify_seconds = 30,", "verify_seconds = 1,").Replace("fee_per_day = 0.5,", "fee_per_day = 0,");

    /// <summary>Pumps (real time passes: the checks use the server clock) until <paramref name="done"/> or about <paramref name="seconds"/>.</summary>
    private static void Wait(ScriptHost host, Fake server, Func<bool> done, double seconds = 3)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!done() && DateTime.UtcNow < until) { Thread.Sleep(50); host.Pump(server.Now += 600); host.WaitDurable(); }
    }

    private static List<JsonElement> To(Fake server, int slot) => server.Sent.Where(s => s.Slot == slot).Select(s => s.Message).ToList();

    [TestMethod]
    public void ClosedNetSaleOfASocketedItemComparesAllItsRecords()
    {
        using var folder = new Folder();
        var server = new Fake { Authoritative = true };
        var (host, _) = Start(folder, server, FastChecks);
        Ask(host, server, server.Alice, new { op = "hello" });
        Assert.AreEqual(3 * 260, RecordsOf(Socketed, 2).Length);
        server.Items[(1, 701)] = new ScriptHeroItem(701, 0x12A, "gone", RecordsOf(Socketed, 2));
        server.Sent.Clear();
        Send(host, server.Alice, Sell("s1", reference: 701, data: Socketed));
        Wait(host, server, () => To(server, 1).Count > 0);
        var answer = Answer(To(server, 1), "result");
        Assert.IsTrue(Ok(answer), "n = 2: " + answer);
        // the server's copy of only the item's own record is not that item
        server.Items[(1, 702)] = new ScriptHeroItem(702, 0x12A, "gone", RecordsOf(Item, 0));
        server.Sent.Clear();
        Send(host, server.Alice, Sell("s2", reference: 702, data: Socketed));
        Wait(host, server, () => To(server, 1).Count > 0);
        Assert.IsFalse(Ok(Answer(To(server, 1), "result")), "a socketed export does not match an unsocketed item");
    }

    [TestMethod]
    public void ClosedNetSaleRefusesAnExportWithTooManySocketsOrTooFewRecords()
    {
        using var folder = new Folder();
        var server = new Fake { Authoritative = true };
        var (host, _) = Start(folder, server, FastChecks);
        Ask(host, server, server.Alice, new { op = "hello" });
        var nine = "2a010000" + "0900" + string.Concat(Enumerable.Repeat(Record, 10));
        var shortOne = "2a010000" + "0200" + Record + Record;          // n = 2 needs 3 records
        var n = 710u;
        foreach (var bad in new[] { nine, shortOne })
        {
            server.Items[(1, n)] = new ScriptHeroItem(n, 0x12A, "gone", Record);
            server.Sent.Clear();
            Send(host, server.Alice, Sell("bad" + n, reference: n, data: bad));
            Wait(host, server, () => To(server, 1).Count > 0);
            Assert.IsFalse(Ok(Answer(To(server, 1), "result")), "refused: " + bad.Length);
            n++;
        }
    }

    [TestMethod]
    public void ClosedNetSaleNeedsTheItemGoneAndTheHeroSaved()
    {
        using var folder = new Folder();
        var server = new Fake { Authoritative = true };
        var (host, _) = Start(folder, server, FastChecks);
        Assert.IsTrue(Answer(Ask(host, server, server.Alice, new { op = "hello" }), "info").GetProperty("authoritative").GetBoolean());
        server.Items[(1, 501)] = new ScriptHeroItem(501, 0x12A, "backpack", Record);
        server.Sent.Clear();
        Send(host, server.Alice, Sell("c1", reference: 501));
        Settle(host, server);
        Assert.AreEqual(0, To(server, 1).Count, "no answer while the item is still in the hero");
        server.Items[(1, 501)] = new ScriptHeroItem(501, 0x12A, "gone", Record);   // the client destroyed it (0x167)
        Wait(host, server, () => To(server, 1).Count > 0);
        var answer = Answer(To(server, 1), "result");
        Assert.IsTrue(Ok(answer), answer.ToString());
        Assert.AreEqual(1, server.Saves, "the hero was saved before the listing went up");
        Assert.IsTrue(server.Claimed.Contains((1, 501)));
        Assert.AreEqual(1, Answer(Ask(host, server, server.Bob, new { op = "browse" }), "page").GetProperty("total").GetDouble());
        // a resend of the same rid: the same answer, no second check or save
        Assert.IsTrue(Ok(Answer(Ask(host, server, server.Alice, Sell("c1", reference: 501)), "result")));
        Assert.AreEqual(1, server.Saves);

        // other data than the item that left: refused
        server.Items[(1, 502)] = new ScriptHeroItem(502, 0x12A, "gone", new string('0', 260));
        var fake = Answer(Ask(host, server, server.Alice, Sell("c2", reference: 502)), "result");
        Assert.IsFalse(Ok(fake));
        StringAssert.Contains(fake.GetProperty("err").GetString(), "does not match");

        // still in the inventory when the time is up: refused, and the client must not give it back (keep)
        server.Items[(1, 503)] = new ScriptHeroItem(503, 0x12A, "backpack", Record);
        server.Sent.Clear();
        Send(host, server.Alice, Sell("c3", reference: 503));
        Wait(host, server, () => To(server, 1).Count > 0);
        var kept = Answer(To(server, 1), "result");
        Assert.IsFalse(Ok(kept));
        Assert.IsTrue(kept.GetProperty("keep").GetBoolean());

        // the lobby refuses the save: refused, the client gives the item back (no keep)
        server.Save = _ => Task.FromResult<string?>("the lobby refused the hero (result 11)");
        server.Items[(1, 504)] = new ScriptHeroItem(504, 0x12A, "gone", Record);
        server.Sent.Clear();
        Send(host, server.Alice, Sell("c4", reference: 504));
        Wait(host, server, () => To(server, 1).Count > 0);
        var unsaved = Answer(To(server, 1), "result");
        Assert.IsFalse(Ok(unsaved));
        Assert.IsFalse(unsaved.TryGetProperty("keep", out _));
        Assert.AreEqual(1, Answer(Ask(host, server, server.Bob, new { op = "browse" }), "page").GetProperty("total").GetDouble(), "still one listing");
        // without the item's id this server refuses
        Assert.IsFalse(Ok(Answer(Ask(host, server, server.Alice, Sell("c5")), "result")));
    }

    [TestMethod]
    public void ClosedNetDepositAndDeliveries()
    {
        using var folder = new Folder();
        var server = new Fake { Authoritative = true };
        var (host, _) = Start(folder, server, FastChecks);
        server.Gold[1] = 1000;
        // a deposit is prepared (the server notes the hero's gold), then the gold leaves the hero
        Assert.AreEqual("prepared", Ask(host, server, server.Alice, new { op = "prepare", rid = "g1", gold = 300 }).Single().GetProperty("op").GetString());
        server.Sent.Clear();
        Send(host, server.Alice, new { op = "deposit", rid = "g1", gold = 300 });
        Settle(host, server);
        Assert.AreEqual(0, To(server, 1).Count, "the server has not seen the gold leave yet");
        server.Gold[1] = 700;
        Wait(host, server, () => To(server, 1).Count > 0);
        Assert.AreEqual(300, Wallet(Answer(To(server, 1), "result")));
        Assert.AreEqual(1, server.Saves);
        Assert.IsFalse(Ok(Answer(Ask(host, server, server.Alice, new { op = "deposit", rid = "g2", gold = 50 }), "result")), "a deposit that was not prepared");

        // a gold withdrawal: removed from the mailbox only after the server saw the gold arrive and saved the hero
        var withdrawRid = "w1";
        var deliver = Answer(Ask(host, server, server.Alice, new { op = "withdraw", rid = withdrawRid, gold = 100 }), "deliver");
        Assert.AreEqual(100, deliver.GetProperty("gold").GetDouble());
        server.Sent.Clear();
        Send(host, server.Alice, new { op = "collected", rid = withdrawRid });
        Settle(host, server);
        Assert.AreEqual(0, To(server, 1).Count, "not arrived yet");
        server.Gold[1] = 800;
        Wait(host, server, () => To(server, 1).Count > 0);
        Assert.IsTrue(Answer(To(server, 1), "collected").GetProperty("removed").GetBoolean());
        Assert.AreEqual(2, server.Saves);

        // an item from the mailbox: the new item (its id) must be in the hero with the delivery's records
        Ask(host, server, server.Alice, new { op = "prepare", rid = "g3", gold = 100 });
        server.Gold[1] = 700;
        Send(host, server.Alice, new { op = "deposit", rid = "g3", gold = 100 });
        server.Items[(1, 600)] = new ScriptHeroItem(600, 0x12A, "gone", Record);
        Send(host, server.Alice, Sell("s1", reference: 600));
        Wait(host, server, () => Answer(Ask(host, server, server.Bob, new { op = "browse" }), "page").GetProperty("total").GetDouble() == 1);
        var id = Answer(Ask(host, server, server.Bob, new { op = "browse" }), "page").GetProperty("rows")[0].GetProperty("id").GetDouble();
        Ask(host, server, server.Alice, new { op = "cancel", rid = Rid(), id });
        var mid = Answer(Ask(host, server, server.Alice, new { op = "mail" }), "mail").GetProperty("entries")[0].GetProperty("mid").GetDouble();
        Assert.AreEqual("deliver", Ask(host, server, server.Alice, new { op = "collect", rid = "i1", mid }).Last().GetProperty("op").GetString());
        server.Items[(1, 601)] = new ScriptHeroItem(601, 0x12A, "backpack", Record);       // the client made the item
        server.Sent.Clear();
        Send(host, server.Alice, new { op = "collected", rid = "i1", @ref = 601 });
        Wait(host, server, () => To(server, 1).Count > 0);
        Assert.IsTrue(Answer(To(server, 1), "collected").GetProperty("removed").GetBoolean());
        Assert.AreEqual(0, Answer(Ask(host, server, server.Alice, new { op = "mail" }), "mail").GetProperty("entries").GetArrayLength());
    }

    [TestMethod]
    public void ClosedNetADeliveryLostInACrashIsReleasedOnlyWithoutASaveSince()
    {
        using var folder = new Folder();
        var server = new Fake { Authoritative = true, UploadTime = null };
        var (host, _) = Start(folder, server, FastChecks);
        server.Gold[1] = 1000;
        Ask(host, server, server.Alice, new { op = "prepare", rid = "g1", gold = 500 });
        server.Gold[1] = 500;
        Send(host, server.Alice, new { op = "deposit", rid = "g1", gold = 500 });
        Wait(host, server, () => To(server, 1).Any(m => m.GetProperty("op").GetString() == "result"));
        Ask(host, server, server.Alice, new { op = "withdraw", rid = "w1", gold = 200 });
        Ask(host, server, server.Alice, new { op = "withdraw", rid = "w2", gold = 100 });
        // the client crashes before the gold reached any save; it rejoins
        host.FirePlayer("leave", server.Alice);
        Thread.Sleep(1100);
        host.FirePlayer("join", server.Alice);
        server.Sent.Clear();
        Send(host, server.Alice, new { op = "collected", rid = "w1" });
        Wait(host, server, () => To(server, 1).Count > 0);
        var released = Answer(To(server, 1), "collected");
        Assert.IsTrue(released.GetProperty("released").GetBoolean(), released.ToString());
        var mail = Answer(Ask(host, server, server.Alice, new { op = "mail" }), "mail").GetProperty("entries");
        Assert.IsTrue(mail.EnumerateArray().Any(m => m.GetProperty("gold").GetDouble() == 200 && !m.TryGetProperty("delivering", out _)), "free to collect again");
        // a save went out since the delivery: the server cannot tell, the entry stays reserved (never twice)
        server.UploadTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        server.Sent.Clear();
        Send(host, server.Alice, new { op = "collected", rid = "w2" });
        Wait(host, server, () => To(server, 1).Count > 0);
        // a gold delivery whose gold the server never saw rise, in a hero saved since: credited back to the wallet (never twice)
        Assert.IsTrue(Answer(To(server, 1), "collected").GetProperty("refunded").GetBoolean());
        Assert.AreEqual(500 - 300 + 100, Wallet(Answer(Ask(host, server, server.Alice, new { op = "mine" }), "mine")));
    }

    // ---- tokens, the gold check, hot reload, rules as data ------------------------------------------------------------------------

    private static Func<string, string> Edit(params (string From, string To)[] edits) =>
        text => edits.Aggregate(FastChecks(text), (t, e) => { Assert.IsTrue(t.Contains(e.From), e.From); return t.Replace(e.From, e.To); });

    /// <summary>Alice sells an item (Closed Net) and cancels the listing: one item entry in her mailbox; returns its mid.</summary>
    private static double MailItem(ScriptHost host, Fake server, uint reference)
    {
        server.Items[(1, reference)] = new ScriptHeroItem(reference, 0x12A, "gone", Record);
        Send(host, server.Alice, Sell(Rid(), 100, reference: reference));
        Wait(host, server, () => Answer(Ask(host, server, server.Bob, new { op = "browse" }), "page").GetProperty("total").GetDouble() == 1);
        var id = Answer(Ask(host, server, server.Bob, new { op = "browse" }), "page").GetProperty("rows")[0].GetProperty("id").GetDouble();
        Ask(host, server, server.Alice, new { op = "cancel", rid = Rid(), id });
        return Answer(Ask(host, server, server.Alice, new { op = "mail" }), "mail").GetProperty("entries")[0].GetProperty("mid").GetDouble();
    }

    /// <summary>Alice puts 300 gold into her wallet (Closed Net); her hero ends with 700.</summary>
    private static void Fund(ScriptHost host, Fake server)
    {
        server.Gold[1] = 1000;
        Ask(host, server, server.Alice, new { op = "prepare", rid = "fund", gold = 300 });
        server.Gold[1] = 700;
        server.Sent.Clear();
        Send(host, server.Alice, new { op = "deposit", rid = "fund", gold = 300 });
        Wait(host, server, () => To(server, 1).Any(m => m.GetProperty("op").GetString() == "result"));
        Assert.AreEqual(300, Wallet(Answer(To(server, 1), "result")));
    }

    [TestMethod]
    public void TheInfoReplyIsClosedOnlyWhereTheServerHoldsTheHero()
    {
        using var folder = new Folder();
        var (host, server) = Start(folder);
        var lan = Answer(Ask(host, server, server.Alice, new { op = "hello" }), "info");
        Assert.IsFalse(lan.GetProperty("closed").GetBoolean());
        server.Authoritative = true;
        var closed = Answer(Ask(host, server, server.Alice, new { op = "hello" }), "info");
        Assert.IsTrue(closed.GetProperty("closed").GetBoolean());
        Assert.IsFalse(closed.GetProperty("rules").GetProperty("allow_special").GetBoolean());
    }

    [TestMethod]
    public void ADeliveredItemGetsATokenReusedForTheSameRidAndConsumedByTheArrival()
    {
        using var folder = new Folder();
        var server = new Fake { Authoritative = true };
        var (host, _) = Start(folder, server, FastChecks);
        var mid = MailItem(host, server, 600);
        Assert.AreEqual(0, server.Minted.Count, "nothing is delivered yet");
        Assert.AreEqual("deliver", Ask(host, server, server.Alice, new { op = "collect", rid = "i1", mid }).Last().GetProperty("op").GetString());
        Assert.AreEqual(1, server.Minted.Count);
        var token = server.Minted[0];
        Assert.AreEqual("auction:i1", token.Label);
        Assert.AreEqual(120, token.Ttl);
        Assert.AreEqual(0x12Au, token.Type);
        Assert.AreEqual(Record, token.Records, "a token for exactly this item");
        Ask(host, server, server.Alice, new { op = "collect", rid = "i1", mid });
        Assert.AreEqual(1, server.Minted.Count, "a repeated rid reuses the token while it is valid");
        server.Items[(1, 601)] = new ScriptHeroItem(601, 0x12A, "backpack", Record);
        server.Sent.Clear();
        Send(host, server.Alice, new { op = "collected", rid = "i1", @ref = 601 });
        Wait(host, server, () => To(server, 1).Count > 0);
        Assert.IsTrue(Answer(To(server, 1), "collected").GetProperty("removed").GetBoolean());
        Assert.IsTrue(server.Claimed.Contains((1, 601)), "claim_item consumes the token");
        Assert.AreEqual(0, server.Revoked.Count, "a consumed token is not revoked");
        host.Dispose();
    }

    [TestMethod]
    public void AnExpiredTokenIsReplacedAnAbandonedDeliveryRevokesIt()
    {
        using var folder = new Folder();
        var server = new Fake { Authoritative = true };
        var (host, _) = Start(folder, server, Edit(("token_seconds = 120,", "token_seconds = 1,")));
        var mid = MailItem(host, server, 600);
        Ask(host, server, server.Alice, new { op = "collect", rid = "i1", mid });
        Ask(host, server, server.Alice, new { op = "collect", rid = "i1", mid });
        Assert.AreEqual(2, server.Minted.Count, "a token that is about to expire is not reused");
        CollectionAssert.AreEqual(new long[] { 1 }, server.Revoked, "the replaced one is revoked");
        host.FirePlayer("leave", server.Alice);
        CollectionAssert.AreEqual(new long[] { 1, 2 }, server.Revoked, "the player left with the delivery open");
        host.Dispose();
    }

    [TestMethod]
    public void ADeliveredItemIsReleasedAndItsTokenRevokedWhenItNeverReachedASavedHero()
    {
        using var folder = new Folder();
        var server = new Fake { Authoritative = true, UploadTime = null };
        var (host, _) = Start(folder, server, FastChecks);
        var mid = MailItem(host, server, 600);
        Ask(host, server, server.Alice, new { op = "collect", rid = "i1", mid });
        host.FirePlayer("leave", server.Alice);
        Thread.Sleep(1100);
        host.FirePlayer("join", server.Alice);
        server.Sent.Clear();
        Send(host, server.Alice, new { op = "collected", rid = "i1" });
        Wait(host, server, () => To(server, 1).Count > 0);
        Assert.IsTrue(Answer(To(server, 1), "collected").GetProperty("released").GetBoolean());
        CollectionAssert.Contains(server.Revoked, 1L);
        host.Dispose();
    }

    [TestMethod]
    public void WithoutATokenTheDeliveryWorksAsBefore()
    {
        using var folder = new Folder();
        var server = new Fake { Authoritative = true, NoTokens = true };
        var (host, _) = Start(folder, server, FastChecks);
        var mid = MailItem(host, server, 600);
        Assert.AreEqual("deliver", Ask(host, server, server.Alice, new { op = "collect", rid = "i1", mid }).Last().GetProperty("op").GetString());
        server.Items[(1, 601)] = new ScriptHeroItem(601, 0x12A, "backpack", Record);
        server.Sent.Clear();
        Send(host, server.Alice, new { op = "collected", rid = "i1", @ref = 601 });
        Wait(host, server, () => To(server, 1).Count > 0);
        Assert.IsTrue(Answer(To(server, 1), "collected").GetProperty("removed").GetBoolean());
        host.Dispose();
    }

    [TestMethod]
    public void AGoldWithdrawalCountsWhenTheHeroSpentGoldBeforeTheReport()
    {
        using var folder = new Folder();
        var server = new Fake { Authoritative = true };
        var (host, _) = Start(folder, server, FastChecks);
        Fund(host, server);
        Assert.AreEqual("deliver", Ask(host, server, server.Alice, new { op = "withdraw", rid = "w1", gold = 100 }).Last().GetProperty("op").GetString());
        server.Gold[1] = 500;                       // the hero spends 200 before the gold is added
        Wait(host, server, () => false, 1.3);       // the server notes the low point
        server.Gold[1] = 600;                       // the client adds the 100: below the gold at the withdrawal (700) + 100, still +100
        server.Sent.Clear();
        Send(host, server.Alice, new { op = "collected", rid = "w1" });
        Wait(host, server, () => To(server, 1).Count > 0);
        Assert.IsTrue(Answer(To(server, 1), "collected").GetProperty("removed").GetBoolean(), string.Join(" ", To(server, 1)));
        Assert.AreEqual(200, Wallet(Answer(Ask(host, server, server.Alice, new { op = "mine" }), "mine")));
        host.Dispose();
    }

    [TestMethod]
    public void AGoldWithdrawalTheServerNeverSawIsCreditedBackAfterTheTimeout()
    {
        using var folder = new Folder();
        var server = new Fake { Authoritative = true };
        var (host, _) = Start(folder, server, Edit(("gold_timeout = 120,", "gold_timeout = 1,")));
        Fund(host, server);
        Ask(host, server, server.Alice, new { op = "withdraw", rid = "w1", gold = 100 });
        Assert.AreEqual(200, Wallet(Answer(Ask(host, server, server.Alice, new { op = "mine" }), "mine")));
        var saves = server.Saves;
        server.Sent.Clear();
        Send(host, server.Alice, new { op = "collected", rid = "w1" });                  // the client claims it, the gold never came
        Wait(host, server, () => To(server, 1).Any(m => m.GetProperty("op").GetString() == "collected" && m.TryGetProperty("refunded", out _)), 6);
        var answer = Answer(To(server, 1), "collected");
        Assert.IsTrue(answer.GetProperty("refunded").GetBoolean(), string.Join(" ", To(server, 1)));
        Assert.IsTrue(server.Saves > saves, "the hero was saved before the verdict");
        Assert.AreEqual(300, Wallet(Answer(Ask(host, server, server.Alice, new { op = "mine" }), "mine")));
        Assert.AreEqual(0, Answer(Ask(host, server, server.Alice, new { op = "mail" }), "mail").GetProperty("entries").GetArrayLength());
        Assert.IsTrue(Answer(Ask(host, server, server.Alice, new { op = "collected", rid = "w1" }), "collected").GetProperty("refunded").GetBoolean(), "never paid twice");
        host.Dispose();
    }

    [TestMethod]
    public void ATimedOutGoldWithdrawalIsKeptWhenTheSavedHeroShowsTheIncrease()
    {
        using var folder = new Folder();
        var server = new Fake { Authoritative = true };
        var (host, _) = Start(folder, server, Edit(("gold_timeout = 120,", "gold_timeout = 1,")));
        Fund(host, server);
        Ask(host, server, server.Alice, new { op = "withdraw", rid = "w1", gold = 100 });
        server.Gold[1] = 800;
        server.Sent.Clear();
        Send(host, server.Alice, new { op = "collected", rid = "w1" });
        Wait(host, server, () => To(server, 1).Count > 0);
        Assert.IsTrue(Answer(To(server, 1), "collected").GetProperty("removed").GetBoolean());
        Assert.AreEqual(200, Wallet(Answer(Ask(host, server, server.Alice, new { op = "mine" }), "mine")));
        host.Dispose();
    }

    [TestMethod]
    public void AReloadInTheMiddleOfAnItemDeliveryKeepsTheDeliveryAndTheToken()
    {
        using var folder = new Folder();
        var server = new Fake { Authoritative = true };
        var (host, _) = Start(folder, server, FastChecks);
        var mid = MailItem(host, server, 600);
        Ask(host, server, server.Alice, new { op = "collect", rid = "i1", mid });
        Assert.AreEqual(1, server.Minted.Count);
        Assert.IsTrue(host.Reload("auction_house"));
        Settle(host, server);
        var again = Ask(host, server, server.Alice, new { op = "collect", rid = "i1", mid }).Last();
        Assert.AreEqual("deliver", again.GetProperty("op").GetString());
        Assert.AreEqual(1, server.Minted.Count, "the token (alive in the server) is reused after the reload");
        server.Items[(1, 601)] = new ScriptHeroItem(601, 0x12A, "backpack", Record);
        server.Sent.Clear();
        Send(host, server.Alice, new { op = "collected", rid = "i1", @ref = 601 });
        Wait(host, server, () => To(server, 1).Count > 0);
        Assert.IsTrue(Answer(To(server, 1), "collected").GetProperty("removed").GetBoolean());
        Assert.AreEqual(0, Answer(Ask(host, server, server.Alice, new { op = "mail" }), "mail").GetProperty("entries").GetArrayLength());
        host.Dispose();
    }

    [TestMethod]
    public void AReloadInTheMiddleOfAGoldDeliveryAndOfADepositLosesNothing()
    {
        using var folder = new Folder();
        var server = new Fake { Authoritative = true };
        var (host, _) = Start(folder, server, FastChecks);
        server.Gold[1] = 1000;
        Ask(host, server, server.Alice, new { op = "prepare", rid = "d1", gold = 300 });
        Assert.IsTrue(host.Reload("auction_house"));
        Settle(host, server);
        server.Gold[1] = 700;                       // the client takes the gold out, then sends the deposit to the reloaded script
        server.Sent.Clear();
        Send(host, server.Alice, new { op = "deposit", rid = "d1", gold = 300 });
        Wait(host, server, () => To(server, 1).Any(m => m.GetProperty("op").GetString() == "result"));
        Assert.AreEqual(300, Wallet(Answer(To(server, 1), "result")), "the prepared deposit survived the reload");
        Ask(host, server, server.Alice, new { op = "withdraw", rid = "w1", gold = 100 });
        Assert.IsTrue(host.Reload("auction_house"));
        Settle(host, server);
        server.Gold[1] = 800;
        server.Sent.Clear();
        Send(host, server.Alice, new { op = "collected", rid = "w1" });
        Wait(host, server, () => To(server, 1).Count > 0);
        Assert.IsTrue(Answer(To(server, 1), "collected").GetProperty("removed").GetBoolean(), string.Join(" ", To(server, 1)));
        host.Dispose();
    }

    [TestMethod]
    public void TheRulesAreDataSeededOnceAndAppliedWithoutAReload()
    {
        using var folder = new Folder();
        var (host, server) = Start(folder);
        var rules = JsonDocument.Parse(host.HandleRequest("auction_house", "rules", [], false).Body).RootElement;
        Assert.AreEqual(5, rules.GetProperty("sale_cut").GetDouble());
        Assert.AreEqual(3, rules.GetProperty("durations").GetArrayLength());
        var put = host.StoreRequest("auction_house", null, """{"mod":"auction_house","set":{"config":{"sale_cut":10,"max_per_seller":1,"allow_special":true,"durations":[2],"sweep_seconds":20}}}""");
        Assert.AreEqual(200, put.Status, put.Body);
        double Cut() => Answer(Ask(host, server, server.Alice, new { op = "hello" }), "info").GetProperty("rules").GetProperty("sale_cut").GetDouble();
        Wait(host, server, () => Cut() == 10, 6);
        var info = Answer(Ask(host, server, server.Alice, new { op = "hello" }), "info").GetProperty("rules");
        Assert.AreEqual(10, info.GetProperty("sale_cut").GetDouble());
        Assert.AreEqual(1, info.GetProperty("max_per_seller").GetDouble());
        Assert.IsTrue(info.GetProperty("allow_special").GetBoolean());
        Assert.AreEqual(1, info.GetProperty("durations").GetArrayLength());
        Assert.IsFalse(Ok(Answer(Ask(host, server, server.Alice, Sell(Rid())), "result")), "3 days are no longer offered");
        StringAssert.Contains(host.HandleRequest("auction_house", "", [], true).Body, "sale_cut");
        // a reload keeps them (the storage is the truth; the script file's values only seed)
        Assert.IsTrue(host.Reload("auction_house"));
        Settle(host, server);
        Assert.AreEqual(10, Cut());
        // an invalid value is ignored (back to the script's default for that rule)
        host.StoreRequest("auction_house", null, """{"mod":"auction_house","set":{"config":{"sale_cut":-5,"durations":["x"]}}}""");
        Wait(host, server, () => Cut() == 5, 6);
        Assert.AreEqual(5, Cut());
    }
}
