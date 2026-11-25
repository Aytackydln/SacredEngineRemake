using System.Buffers.Binary;
using Sacred.GameServer.Auth;
using Sacred.GameServer.Session;
using Sacred.GameServer.Simulation;
using Sacred.Network.Gameplay;

namespace Sacred.GameServer.Tests;

/// <summary>The witness on a live authoritative server (Closed Net, fake lobby, strict items on): loot near a kill the server saw, an item
/// the player destroyed coming back, a gold pile explaining a gold increase, and a capture of the session.</summary>
public partial class AuthoritativeTests
{
    private static byte[] Positioned(byte[] state, int x, int y, ushort sector)
    {
        BinaryPrimitives.WriteInt32LittleEndian(state.AsSpan(8), x);
        BinaryPrimitives.WriteInt32LittleEndian(state.AsSpan(12), y);
        BinaryPrimitives.WriteUInt32LittleEndian(state.AsSpan(20), sector);
        return state;
    }

    private static byte[] CreatureState(uint reference, uint type, int x, int y, ushort sector)
    {
        var body = new byte[GoldObjectStateLayout.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(body, GoldObjectStateLayout.Marker);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(GoldObjectStateLayout.TypeOffset), type);
        var payload = new byte[24 + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, reference);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), type);
        body.CopyTo(payload.AsSpan(24));
        return Positioned(payload, x, y, sector);
    }

    private static byte[] Kill(uint victim, uint killer) => [.. Word(victim), .. Word(killer)];

    [TestMethod]
    public void WitnessExplainsLootNearAKillAndRefusesItFarAway()
    {
        StrictRun(StrictItemsMode.On, (host, player) =>
        {
            player.Send(0x16F, CreatureState(0x7001, 0x200, 10_000, 10_000, 44));
            player.Send(0x17D, Kill(0x7001, player.Slot));
            player.Send(0x16F, Positioned(ItemState(0x5101, 0x12A, 0x41), 10_300, 10_100, 44));
            Assert.AreEqual("world", ItemWhere(host, player, 0x5101), "loot next to the creature");
            player.Send(0x16F, Positioned(ItemState(0x5102, 0x12A, 0x42), 30_000, 30_000, 45));
            Assert.AreEqual("nil", ItemWhere(host, player, 0x5102, not: "world"), "an item far from any kill");
            Assert.AreEqual(1, host.StrictItemsRefused);
            Assert.AreEqual(1, host.Witness!.Explained[(int)ItemSource.Kill]);
        });
    }

    [TestMethod]
    public void WitnessLetsADestroyedItemComeBackOnce()
    {
        StrictRun(StrictItemsMode.On, (host, player) =>
        {
            player.Send(0x16F, CreatureState(0x7002, 0x200, 10_000, 10_000, 44));
            player.Send(0x17D, Kill(0x7002, player.Slot));
            player.Send(0x16F, Positioned(ItemState(0x5201, 0x12A, 0x51), 10_100, 10_000, 44));
            Assert.AreEqual("world", ItemWhere(host, player, 0x5201));
            player.Send(0x167, Word(0x5201));                                 // destroyed (picked up into another form, socketed, ...)
            Assert.AreEqual("gone", ItemWhere(host, player, 0x5201, not: "world"));
            player.Send(0x16F, ItemState(0x5202, 0x12A, 0x51));              // the same content under a new reference, no position
            Assert.AreEqual("world", ItemWhere(host, player, 0x5202, not: "gone"));
            player.Send(0x16F, Positioned(ItemState(0x5203, 0x12A, 0x51), 30_000, 30_000, 45));   // and again, far from any event: nothing left to rebuild it from (a positionless one would be trader stock)
            Assert.AreEqual("nil", ItemWhere(host, player, 0x5203, not: "world"));
            Assert.AreEqual(1, host.Witness!.Explained[(int)ItemSource.Rebuild]);
            Assert.AreEqual(1, host.StrictItemsRefused);
        });
    }

    [TestMethod]
    public void WitnessSourcesAreConfigurable()
    {
        StrictRun(StrictItemsMode.On, (host, player) =>
        {
            player.Send(0x16F, CreatureState(0x7003, 0x200, 10_000, 10_000, 44));
            player.Send(0x17D, Kill(0x7003, player.Slot));
            player.Send(0x16F, Positioned(ItemState(0x5301, 0x12A, 0x61), 10_100, 10_000, 44));
            Assert.AreEqual("nil", ItemWhere(host, player, 0x5301), "kills explain nothing with AUTH_ITEM_SOURCES=token,baseline");
        }, configure: c => c.ApplyLine("AUTH_ITEM_SOURCES=token,baseline"));
    }

    [TestMethod]
    public void RefusedItemsCountAsViolationsWhenConfigured()
    {
        StrictRun(StrictItemsMode.On, (host, player) =>
        {
            player.Send(0x16F, Positioned(ItemState(0x5401, 0x12A, 0x71), 30_000, 30_000, 45));
            Assert.AreEqual("nil", ItemWhere(host, player, 0x5401));
            player.Send(0x16F, Positioned(ItemState(0x5402, 0x12A, 0x72), 30_000, 30_000, 45));
            Assert.IsTrue(player.IsClosed(), "two refused items with AUTH_MAX_VIOLATIONS=2 remove the player");
        }, configure: c => { c.ApplyLine("AUTH_STRICT_VIOLATIONS=1"); c.ApplyLine("AUTH_MAX_VIOLATIONS=2"); });
    }

    private const string GoldScript = """
        server.on("chat", function(p, text)
            if text == "!gold" then local h = server.hero(p) server.save("gold", tostring(h and h.gold or "nil")) return true end
        end)
        """;

    private static byte[] HeroWithGold(byte slot, uint gold)
    {
        var values = GoldCreatureStateLayout.PrefixSize;
        var body = new byte[values + GoldCreatureValuesLayout.CompactMinimumSize + 64];
        BinaryPrimitives.WriteUInt32LittleEndian(body, GoldObjectStateLayout.Marker);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(GoldCreatureStateLayout.MarkerOffset), GoldCreatureStateLayout.Marker);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(values), GoldCreatureValuesLayout.Marker);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(values + GoldCreatureValuesLayout.CompactWord56Offset), 5);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(values + GoldCreatureValuesLayout.CompactWord46Offset), gold);
        var payload = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, slot);
        body.CopyTo(payload.AsSpan(8));
        return payload;
    }

    private static string? GoldOf(GameServerHost host, RawClient player, string? not)
    {
        player.Send(0x005, ChatLine("!gold"));
        return StoredOf(host, "gold", "gold", not);
    }

    [TestMethod]
    public void GoldLedgerSavesOnlyExplainedGold()
    {
        StrictRun(StrictItemsMode.Off, (host, player) =>
        {
            player.Send(0x16E, HeroWithGold(player.Slot, 1000));
            Assert.AreEqual("1000", GoldOf(host, player, null));
            player.Send(0x16E, HeroWithGold(player.Slot, 5000));             // +4000 from nowhere
            Assert.AreEqual("1000", GoldOf(host, player, "nil"), "not taken over into the server's hero");
            var pile = ItemState(0x5501, 0x140C, 0x00);
            BinaryPrimitives.WriteUInt32LittleEndian(pile.AsSpan(24 + GoldItemStateLayout.RecordsOffset + 8), 250);
            player.Send(0x16F, Positioned(pile, 10_000, 10_000, 44));
            player.Send(0x141, [.. Word(0x5501), .. Word(0)]);
            player.Send(0x16E, HeroWithGold(player.Slot, 5250));             // +250 from the pile
            Assert.AreEqual("1250", GoldOf(host, player, "1000"));
            Assert.AreEqual(1, host.Gold!.Unexplained);
            Assert.AreEqual(1, host.GoldClamped);
        }, scripts: new() { ["gold.lua"] = GoldScript }, configure: c => c.ApplyLine("AUTH_STRICT_GOLD=on"));
    }

    [TestMethod]
    public void ACaptureReplaysToTheSameDecisions()
    {
        var folder = Directory.CreateTempSubdirectory("cap-").FullName;
        try
        {
            int[]? live = null;
            StrictRun(StrictItemsMode.On, (host, player) =>
            {
                player.Send(0x16F, CreatureState(0x7101, 0x200, 10_000, 10_000, 44));
                player.Send(0x17D, Kill(0x7101, player.Slot));
                player.Send(0x16F, Positioned(ItemState(0x5601, 0x12A, 0x41), 10_300, 10_100, 44));
                Assert.AreEqual("world", ItemWhere(host, player, 0x5601));
                player.Send(0x16F, Positioned(ItemState(0x5602, 0x12A, 0x42), 30_000, 30_000, 45));
                Assert.AreEqual("nil", ItemWhere(host, player, 0x5602, not: "world"));
                live = (int[])host.Witness!.Explained.Clone();
            }, configure: c => c.CaptureDirectory = folder);
            var file = Directory.GetFiles(folder, "*.sgcap").Single();
            var types = new HashSet<uint>();
            var reader = CaptureReader.Open(file);
            while (reader.TryRead(out var record)) types.Add(record.Type);
            Assert.IsTrue(types.Contains(Sacred.GameServer.Wire.TinCatWire.LoginRequest), "the login is in the capture");

            var config = new Sacred.GameServer.Config.GameServerConfig { TickMilliseconds = 5 };
            config.Auth.Witness.WorldStartGraceMs = 0;                  // as in the live run: the items of this session come right after the world start
            config.Auth.StrictItems = StrictItemsMode.On;
            config.Flood.Enabled = false;
            var report = CaptureReplay.Run(config, CaptureReader.Open(file));
            Console.WriteLine(report.Describe());
            Assert.AreEqual(1, report.Connections);
            Assert.AreEqual(live![(int)ItemSource.Kill], report.Explained[(int)ItemSource.Kill]);
            Assert.AreEqual(1, report.Explained[(int)ItemSource.Kill]);
            Assert.AreEqual(1, report.Refused);

            // other strictness, same session: kills explain nothing
            var strict = new Sacred.GameServer.Config.GameServerConfig { TickMilliseconds = 5 };
            strict.Auth.Witness.WorldStartGraceMs = 0;
            strict.Auth.StrictItems = StrictItemsMode.On;
            strict.Flood.Enabled = false;
            strict.ApplyLine("AUTH_ITEM_SOURCES=token,baseline,trade,rebuild");
            Assert.AreEqual(2, CaptureReplay.Run(strict, CaptureReader.Open(file)).Refused);
        }
        finally { Directory.Delete(folder, true); }
    }

    [TestMethod]
    public void PositionUpdatesMoveObjectsInTheServersWorld()
    {
        StrictRun(StrictItemsMode.On, (host, player) =>
        {
            player.Send(0x16F, CreatureState(0x7201, 0x200, 10_000, 10_000, 44));
            var move = new byte[24];
            BinaryPrimitives.WriteUInt32LittleEndian(move, 0x7201);
            BinaryPrimitives.WriteInt32LittleEndian(move.AsSpan(4), 40_000);
            BinaryPrimitives.WriteInt32LittleEndian(move.AsSpan(8), 40_000);
            BinaryPrimitives.WriteUInt32LittleEndian(move.AsSpan(16), 47);
            player.Send(0x165, move);
            player.Send(0x17D, Kill(0x7201, player.Slot));
            player.Send(0x16F, Positioned(ItemState(0x5701, 0x12A, 0x41), 40_200, 40_000, 47));
            Assert.AreEqual("world", ItemWhere(host, player, 0x5701), "loot where the creature died, not where it was first seen");
            player.Send(0x16F, Positioned(ItemState(0x5702, 0x12A, 0x42), 10_100, 10_000, 44));
            Assert.AreEqual("nil", ItemWhere(host, player, 0x5702, not: "world"), "nothing died at the first position");
        });
    }

    private static byte[] LivingCreature(uint reference, uint type, int hp, int x, int y, ushort sector)
    {
        var body = new byte[445 + 64];
        BinaryPrimitives.WriteUInt32LittleEndian(body, GoldObjectStateLayout.Marker);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(GoldObjectStateLayout.TypeOffset), type);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(GoldCreatureStateLayout.MarkerOffset), GoldCreatureStateLayout.Marker);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(GoldCreatureStateLayout.PrefixSize), GoldCreatureValuesLayout.Marker);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(GoldCreatureStateLayout.PrefixSize + GoldCreatureValuesLayout.MaximumHitPointsOffset), hp);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(GoldCreatureStateLayout.PrefixSize + GoldCreatureValuesLayout.CurrentHitPointsOffset), hp);
        var payload = new byte[24 + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, reference);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), type);
        body.CopyTo(payload.AsSpan(24));
        return Positioned(payload, x, y, sector);
    }

    [TestMethod]
    public void HealthUpdatesBackKillReports()
    {
        StrictRun(StrictItemsMode.Off, (host, player) =>
        {
            player.Send(0x16F, LivingCreature(0x7301, 0x200, 100, 10_000, 10_000, 44));
            player.Send(0x16F, LivingCreature(0x7302, 0x200, 100, 10_000, 10_000, 44));
            player.Send(0x128, [.. Word(0x7302), .. Word(0)]);             // the second one is beaten down
            player.Send(0x17D, Kill(0x7302, player.Slot));
            player.Send(0x17D, Kill(0x7301, player.Slot));                 // the first still has 100 hit points
            var deadline = Environment.TickCount64 + 5000;
            while (host.KillsAlive == 0 && Environment.TickCount64 < deadline) Thread.Sleep(10);
            Assert.AreEqual(1, host.KillsAlive);
            Assert.AreEqual(1, host.WorldHitPointUpdates);
        });
    }

    /// <summary>The world a client builds while it starts (between its 0x016 and the world start 0x018) is explained by the WorldStart source;
    /// the same creation after the start is not (strict items on). A merchant purchase (0x16D, found with a real client: the state of the
    /// bought item, then 0x13F) is kept by the server, accepted provisionally, and removed when the hero gold does not pay for it.</summary>
    [TestMethod]
    public void WorldStartAndPurchaseAreWitnessSources()
    {
        StrictRun(StrictItemsMode.On, (host, player) =>
        {
            // StrictRun brings the player to the world start already (0x016 and 0x018 were sent): inside the grace, a creation is explained
            player.Send(0x16F, ItemState(0x6001, 0x12A, 0x41));
            Assert.AreEqual("world", ItemWhere(host, player, 0x6001), "world start source");
            Assert.AreEqual(0, host.StrictItemsRefused);
            Thread.Sleep(700);
            player.Send(0x16F, Positioned(ItemState(0x6002, 0x12A, 0x42), 30_000, 30_000, 45));
            Assert.AreEqual("nil", ItemWhere(host, player, 0x6002, not: "world"), "the grace is over");
            Assert.AreEqual(1, host.StrictItemsRefused);

            // a purchase: a 0x16D state with the short prefix
            var full = ItemState(0x6003, 0x12A, 0x43);
            var short8 = new byte[8 + full.Length - 24];
            full.AsSpan(0, 8).CopyTo(short8);
            full.AsSpan(24).CopyTo(short8.AsSpan(8));
            player.Send(0x16D, short8);
            Assert.AreEqual("world", ItemWhere(host, player, 0x6003, not: "nil"), "kept provisionally");
            var until = Environment.TickCount64 + 5000;
            while (Environment.TickCount64 < until && ItemWhere(host, player, 0x6003, not: "world") == "world") Thread.Sleep(50);
            Assert.AreEqual("nil", ItemWhere(host, player, 0x6003, not: "world"), "removed: no gold paid for it");
            Assert.AreEqual(2, host.StrictItemsRefused);
        }, configure: config =>
        {
            config.Auth.Witness.WorldStartGraceMs = 500;
            config.Auth.Witness.PurchaseWindowMs = 800;
        });
    }

    /// <summary>A quest event (0x145 quest pair) of the player explains the reward the client creates next; without it the same creation is refused.</summary>
    [TestMethod]
    public void AQuestEventExplainsTheRewardItem()
    {
        StrictRun(StrictItemsMode.On, (host, player) =>
        {
            player.Send(0x16F, ItemState(0x6101, 0x12A, 0x51));
            Assert.AreEqual("nil", ItemWhere(host, player, 0x6101), "no quest event");
            var pair = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(pair, 0x1234);
            BinaryPrimitives.WriteUInt32LittleEndian(pair.AsSpan(4), 1);
            player.Send(0x145, pair);
            player.Send(0x16F, ItemState(0x6102, 0x12A, 0x52));
            Assert.AreEqual("world", ItemWhere(host, player, 0x6102, not: "nil"), "after the quest event");
            Assert.AreEqual(1, host.Witness!.Explained[(int)ItemSource.Quest]);
        });
    }

    /// <summary>Trader stock (a positionless 0x16E item nobody holds, created when a hero comes near a merchant: found in a 53-minute real
    /// capture) is kept when it is created; it is judged when it enters the hero (0x13F into the hero's inventory): without a gold payment the
    /// server removes it, so it is never saved.</summary>
    [TestMethod]
    public void TraderStockIsJudgedWhenItEntersTheHero()
    {
        StrictRun(StrictItemsMode.On, (host, player) =>
        {
            Thread.Sleep(700);                                  // the world start window of the test run is over
            var full = ItemState(0x6201, 0x12A, 0x61);
            var short8 = new byte[8 + full.Length - 24];
            full.AsSpan(0, 8).CopyTo(short8);
            full.AsSpan(24).CopyTo(short8.AsSpan(8));
            player.Send(0x16E, short8);
            Assert.AreEqual("world", ItemWhere(host, player, 0x6201), "stock is not judged when it is created");
            Assert.AreEqual(0, host.StrictItemsRefused);
            var place = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(place, 0x6201);
            BinaryPrimitives.WriteUInt32LittleEndian(place.AsSpan(4), 5);
            BinaryPrimitives.WriteUInt32LittleEndian(place.AsSpan(8), (uint)player.Slot);
            player.Send(0x13F, place);                          // into the hero's inventory, no gold paid
            var until = Environment.TickCount64 + 5000;
            while (Environment.TickCount64 < until && ItemWhere(host, player, 0x6201, not: "x") != "nil") Thread.Sleep(50);
            Assert.AreEqual("nil", ItemWhere(host, player, 0x6201, not: "world"), "no payment: removed");
            Assert.AreEqual(1, host.StrictItemsRefused);
        }, configure: config =>
        {
            config.Auth.Witness.Sources = Sacred.GameServer.Simulation.WitnessOptions.AllSources;     // stock on (the other tests leave it out)
            config.Auth.Witness.WorldStartGraceMs = 300;
            config.Auth.Witness.PurchaseWindowMs = 800;
        });
    }

    [TestMethod]
    public void AnItemMadeWhileTheWorldStartsMustNotGoIntoTheHero()
    {
        StrictRun(StrictItemsMode.On, (host, player) =>
        {
            player.Send(0x16F, ItemState(0x6401, 0x12A, 0x71));
            Assert.AreEqual("world", ItemWhere(host, player, 0x6401), "a world object of the start");
            var place = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(place, 0x6401);
            BinaryPrimitives.WriteUInt32LittleEndian(place.AsSpan(4), 5);
            BinaryPrimitives.WriteUInt32LittleEndian(place.AsSpan(8), (uint)player.Slot);
            player.Send(0x13F, place);                          // into the hero's inventory inside the window
            var until = Environment.TickCount64 + 5000;
            while (Environment.TickCount64 < until && host.StrictItemsRefused < 1) Thread.Sleep(50);
            Assert.AreEqual(1, host.StrictItemsRefused);
            Assert.AreEqual("nil", ItemWhere(host, player, 0x6401, not: "world"), "removed: never saved");
        }, configure: config => config.Auth.Witness.WorldStartGraceMs = 5000);
    }

    /// <summary>A trader stock item whose stated price is far below the game's price is noted (AUTH_PRICE_CHECK) and, with on, judged by the computed one.</summary>
    [TestMethod]
    public void AStatedPriceFarBelowTheGamesPriceIsNoted()
    {
        StrictRun(StrictItemsMode.On, (host, player) =>
        {
            host.ItemPrices = ItemPrices.From((type, record) => 5000u);
            Thread.Sleep(700);
            var full = ItemState(0x6501, 0x12A, 0x81);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(full.AsSpan(24 + Sacred.Network.Gameplay.GoldItemStateLayout.RecordsOffset + 8), 3);   // states 3 gold
            var short8 = new byte[8 + full.Length - 24];
            full.AsSpan(0, 8).CopyTo(short8);
            full.AsSpan(24).CopyTo(short8.AsSpan(8));
            player.Send(0x16E, short8);
            Assert.AreEqual("world", ItemWhere(host, player, 0x6501), "stock is kept");
            Assert.AreEqual(1, host.PriceMismatches, "the stated price differs from the computed one");
        }, configure: config =>
        {
            config.Auth.Witness.Sources = Sacred.GameServer.Simulation.WitnessOptions.AllSources;
            config.Auth.Witness.PriceCheck = StrictItemsMode.On;
            config.Auth.Witness.WorldStartGraceMs = 300;
        });
    }
}
