using Sacred.GameServer.Auth;
using Sacred.GameServer.Config;
using Sacred.GameServer.Simulation;

namespace Sacred.GameServer.Tests;

/// <summary>The item witness, the gold ledger, their options and the capture file, without a server (Simulation/*).</summary>
[TestClass]
public class WitnessTests
{
    private const ItemSourceSet NoStock = WitnessOptions.AllSources & ~ItemSourceSet.Stock;     // most tests treat a positionless item as unexplained

    private static byte[] Content(params byte[] fills)
    {
        var content = new byte[fills.Length * ItemWitness.RecordBytes];
        for (var i = 0; i < fills.Length; i++) content.AsSpan(i * ItemWitness.RecordBytes, ItemWitness.RecordBytes).Fill(fills[i]);
        return content;
    }

    private static ItemFacts Facts(uint reference, byte[] content, int x = 0, int y = 0, ushort sector = 0, uint type = 0x12A, int level = 0) =>
        new(reference, type, level, 0, sector, x, y, ItemWitness.Fnv(content));

    [TestMethod]
    public void LootNearAFreshKillIsExplainedWithinItsBudget()
    {
        var options = new WitnessOptions { Sources = NoStock, LootPerKill = 2, LootRange = 500, LootWindowMs = 1000 };
        var w = new ItemWitness(options);
        var c = Content(1);
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(3, Facts(1, c, 1000, 1000), c, 0), "no kill yet");
        w.OnKill(0x7001, 5, 1000, 1000, 10, 100);
        Assert.AreEqual(ItemSource.Kill, w.Classify(3, Facts(2, c, 1200, 1100), c, 200));
        Assert.AreEqual(ItemSource.Kill, w.Classify(4, Facts(3, c, 900, 1000), c, 300), "any player may report the loot");
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(3, Facts(4, c, 1000, 1000), c, 400), "budget of the kill used up");
        w.OnKill(0x7002, 5, 1000, 1000, 10, 500);
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(3, Facts(5, c, 2000, 1000), c, 600), "out of range");
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(3, Facts(6, c, 1000, 1000), c, 1600), "too late");
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(3, Facts(7, c), c, 550), "an item without a position is no loot");
        w.OnKill(0x7002, 5, 1000, 1000, 10, 560);
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(3, Facts(8, c, 1000, 1000), c, 1700), "a second report of the same kill gives no new budget");
    }

    [TestMethod]
    public void LootLevelMarginIsCheckedWhenSet()
    {
        var w = new ItemWitness(new WitnessOptions { Sources = NoStock, LootLevelMargin = 3 });
        var c = Content(1);
        w.OnKill(0x7001, 5, 1000, 1000, 10, 0);
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(3, Facts(1, c, 1000, 1000, level: 14), c, 10));
        Assert.AreEqual(ItemSource.Kill, w.Classify(3, Facts(2, c, 1000, 1000, level: 13), c, 10));
    }

    [TestMethod]
    public void ContainerContentIsExplainedOncePerContainer()
    {
        var w = new ItemWitness(new WitnessOptions { Sources = NoStock, ContainerPerOpen = 1, ContainerRange = 300 });
        var c = Content(2);
        w.OnContainer(0x8001, 2, 7, 5000, 5000, 0);
        Assert.AreEqual(ItemSource.Container, w.Classify(2, Facts(1, c, 5100, 5000), c, 10));
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(2, Facts(2, c, 5100, 5000), c, 20));
        w.OnContainer(0x8001, 2, 7, 5000, 5000, 30);
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(2, Facts(3, c, 5100, 5000), c, 40), "opening it again explains nothing more");
    }

    [TestMethod]
    public void TradedItemsAreExplainedForTheReceiverOnly()
    {
        var w = new ItemWitness(new WitnessOptions { Sources = NoStock, TradeWindowMs = 1000 });
        var c = Content(9, 3);
        w.OnTradeReceive(4, ItemWitness.Fnv(c), 0);
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(5, Facts(1, c), c, 10), "not for another player");
        Assert.AreEqual(ItemSource.Trade, w.Classify(4, Facts(2, c), c, 20));
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(4, Facts(3, c), c, 30), "once");
        w.OnTradeReceive(4, ItemWitness.Fnv(c), 100);
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(4, Facts(4, c), c, 1200), "expired");
    }

    [TestMethod]
    public void DestroyedItemsMayComeBackRebuiltButNotTwice()
    {
        var w = new ItemWitness(new WitnessOptions { Sources = NoStock });
        var sword = Content(0x11, 0xA0);             // one value record + data block
        var gem = Content(0x22, 0xB0);
        w.OnDestroyed(1, 0x12A, sword, 0);
        w.OnDestroyed(1, 0x300, gem, 0);
        // picked up again unchanged
        Assert.AreEqual(ItemSource.Rebuild, w.Classify(1, Facts(1, sword), sword, 10));
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(1, Facts(2, sword), sword, 20), "the remnants were used");

        // socketing: the sword's and the gem's records, and a changed data block of the sword's type
        w.OnDestroyed(1, 0x12A, sword, 30);
        var socketed = Content(0x11, 0x22, 0xA1);
        Assert.AreEqual(ItemSource.Rebuild, w.Classify(1, Facts(3, socketed, type: 0x12A), socketed, 40));

        // a record nobody destroyed
        w.OnDestroyed(1, 0x12A, sword, 50);
        var forged = Content(0x11, 0x33, 0xA0);
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(1, Facts(4, forged), forged, 60));
        // a new data block of another type is no rebuild
        var other = Content(0x11, 0xA2);
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(1, Facts(5, other, type: 0x999), other, 70));
        // another player cannot use them
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(2, Facts(6, sword), sword, 80));
    }

    [TestMethod]
    public void GroundItemsOfASectorTakenOverAreExplainedBriefly()
    {
        var w = new ItemWitness(new WitnessOptions { Sources = NoStock, SectorWindowMs = 1000 });
        var c = Content(5);
        w.OnSectorOwned(6, 1234, 0);
        Assert.AreEqual(ItemSource.Sector, w.Classify(6, Facts(1, c, 10, 10, 1234), c, 500));
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(6, Facts(2, c, 10, 10, 999), c, 500), "another sector");
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(6, Facts(3, c, 10, 10, 1234), c, 1500), "too late");
    }

    [TestMethod]
    public void SourcesCanBeSwitchedOff()
    {
        var options = new WitnessOptions { Sources = NoStock };
        Assert.IsTrue(WitnessOptions.TryParseSources("token, baseline,trade", out var sources));
        options.Sources = sources;
        var w = new ItemWitness(options);
        var c = Content(1);
        w.OnKill(0x7001, 5, 1000, 1000, 10, 0);
        w.OnDestroyed(1, 0x12A, c, 0);
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(1, Facts(1, c, 1000, 1000), c, 10), "kill and rebuild are off");
        Assert.IsFalse(WitnessOptions.TryParseSources("kill,magic", out _));
        Assert.IsTrue(WitnessOptions.TryParseSources("none", out var none));
        Assert.AreEqual((ItemSourceSet)0, none);
    }

    [TestMethod]
    public void AKillAfterTheItemIsCountedAsLate()
    {
        var w = new ItemWitness(new WitnessOptions { Sources = NoStock });
        var c = Content(1);
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(1, Facts(1, c, 1000, 1000), c, 0));
        w.OnKill(0x7001, 5, 1050, 1000, 10, 40);
        Assert.AreEqual(1, w.ExplainedLate);
        Assert.AreEqual(1, w.Explained[(int)ItemSource.Unexplained]);
    }

    [TestMethod]
    public void ClassifyingDoesNotAllocate()
    {
        var w = new ItemWitness(new WitnessOptions { Sources = NoStock });
        var c = Content(1, 2);
        var facts = Facts(1, c, 1000, 1000);
        for (var i = 0; i < 10; i++) { w.OnKill((uint)(0x7000 + i), 5, 1000, 1000, 1, i); w.Classify(1, facts, c, i); w.OnDestroyed(1, 0x12A, c, i); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            w.OnKill((uint)(0x8000 + i), 5, 1000, 1000, 1, 100 + i);
            w.OnDestroyed(1, 0x12A, c, 100 + i);
            w.OnTradeReceive(2, 77, 100 + i);
            w.Classify(1, facts, c, 100 + i);
            w.Classify(3, Facts(2, c), c, 100 + i);
        }
        Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    // ---- gold ---------------------------------------------------------------------------------------------------------

    [TestMethod]
    public void GoldIncreasesNeedACredit()
    {
        var options = new WitnessOptions { Sources = NoStock, StrictGold = StrictItemsMode.On, GoldWindowMs = 1000 };
        var ledger = new GoldLedger(options);
        Assert.AreEqual(0, ledger.Report(1, 500, 0), "the first report starts the ledger");
        Assert.AreEqual(500, ledger.Allowed(1));
        ledger.Credit(1, 200, 10);
        Assert.AreEqual(0, ledger.Report(1, 650, 20), "covered by the pile");
        Assert.AreEqual(650, ledger.Allowed(1));
        Assert.AreEqual(100, ledger.Report(1, 800, 30), "50 credit left, 100 unexplained");
        Assert.AreEqual(700, ledger.Allowed(1));
        Assert.AreEqual(0, ledger.Report(1, 750, 40), "spending 50 is fine");
        Assert.AreEqual(650, ledger.Allowed(1), "the spent gold comes off the explained figure");
        Assert.AreEqual(0, ledger.Report(1, 750, 50), "same report again: nothing new");
        ledger.Credit(1, 1000, 100);
        Assert.AreEqual(1000, ledger.Report(1, 1750, 1200), "the credit expired");
        Assert.AreEqual(2, ledger.Unexplained);
        Assert.AreEqual(1100, ledger.UnexplainedGold);
    }

    [TestMethod]
    public void GoldLogModeAcceptsUnexplainedIncreases()
    {
        var ledger = new GoldLedger(new WitnessOptions { Sources = NoStock, StrictGold = StrictItemsMode.Log });
        ledger.Report(2, 0, 0);
        Assert.AreEqual(300, ledger.Report(2, 300, 10));
        Assert.AreEqual(300, ledger.Allowed(2));
        ledger.Forget(2);
        Assert.IsFalse(ledger.Known(2));
    }

    // ---- options ------------------------------------------------------------------------------------------------------

    [TestMethod]
    public void WitnessKeysApplyThroughTheConfig()
    {
        var config = new GameServerConfig();
        config.ApplyLine("AUTH_ITEM_SOURCES=token,trade");
        config.ApplyLine("AUTH_LOOT_RANGE=900");
        config.ApplyLine("AUTH_STRICT_GOLD=on");
        config.ApplyLine("AUTH_STRICT_VIOLATIONS=1");
        config.ApplyLine("AUTH_STRICT_ITEMS=log");
        config.ApplyLine("CAPTURE=caps");
        Assert.AreEqual(ItemSourceSet.Token | ItemSourceSet.Trade, config.Auth.Witness.Sources);
        Assert.AreEqual(900, config.Auth.Witness.LootRange);
        Assert.AreEqual(StrictItemsMode.On, config.Auth.Witness.StrictGold);
        Assert.IsTrue(config.Auth.Witness.CountViolations);
        Assert.AreEqual(StrictItemsMode.Log, config.Auth.StrictItems);
        Assert.AreEqual("caps", config.CaptureDirectory);
        config.ApplyLine("AUTH_ITEM_SOURCES=bogus");
        Assert.AreEqual(ItemSourceSet.Token | ItemSourceSet.Trade, config.Auth.Witness.Sources, "an unknown source keeps the old list");
    }

    [TestMethod]
    public void TheCommandLineKnowsTheWitnessOptions()
    {
        foreach (var name in new[] { "auth-item-sources", "auth-loot-window-ms", "auth-loot-range", "auth-loot-per-kill", "auth-loot-level-margin",
                     "auth-container-window-ms", "auth-container-range", "auth-container-per-open", "auth-trade-window-ms", "auth-rebuild-window-ms",
                     "auth-sector-window-ms", "auth-strict-gold", "auth-gold-window-ms", "auth-strict-violations", "capture" })
            Assert.IsTrue(CommandLine.Options.Any(o => o.Name == name), name);
    }

    // ---- capture ------------------------------------------------------------------------------------------------------

    [TestMethod]
    public void CaptureFilesRoundTripAndTheWriterDoesNotAllocate()
    {
        var folder = Directory.CreateTempSubdirectory("capture-").FullName;
        try
        {
            var writer = CaptureWriter.Open(folder, 1000)!;
            Assert.IsNotNull(writer);
            var payload = new byte[300];
            for (var i = 0; i < 20; i++) writer.Append(1000 + i, 1, 7, 0x10, payload);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 5000; i++) { payload[0] = (byte)i; writer.Append(1100 + i, (ushort)(1 + i % 3), (uint)(0x160 + i % 16), 42, payload.AsSpan(0, i % 300)); }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            var big = new byte[3 << 20];                                    // larger than one buffer
            big[^1] = 0x5A;
            writer.Append(9000, 2, 0x1B2, 43, big);
            writer.Dispose();
            Assert.IsTrue(File.Exists(Path.Combine(folder, ".gitignore")));
            Assert.IsTrue(allocated < 1024, $"the capture allocated {allocated} bytes");
            var reader = CaptureReader.Open(writer.Path);
            var count = 0; var sawBig = false;
            while (reader.TryRead(out var record))
            {
                if (count == 20) { Assert.AreEqual(100u, record.AtMs); Assert.AreEqual(1, record.Connection); Assert.AreEqual(0x160u, record.Type); Assert.AreEqual(42u, record.Sender); Assert.AreEqual(0, record.Payload.Length); }
                if (record.Type == 0x1B2) { sawBig = true; Assert.AreEqual(big.Length, record.Payload.Length); Assert.AreEqual(0x5A, record.Payload[^1]); }
                count++;
            }
            Assert.AreEqual(20 + 5000 + 1, count);
            Assert.IsTrue(sawBig);
        }
        finally { Directory.Delete(folder, true); }
    }

    [TestMethod]
    public void OpcodeTrailNamesTheLastMessages()
    {
        var trail = new OpcodeTrail();
        for (var i = 0; i < 20; i++) trail.Add(3, (ushort)(0x100 + i), i * 10);
        var text = trail.Describe(3, 200);
        StringAssert.StartsWith(text, "0x104 -160ms");
        StringAssert.EndsWith(text, "0x113 -10ms");
        trail.Forget(3);
        Assert.AreEqual("", trail.Describe(3, 200));
    }

    [TestMethod]
    public void TheWorldAClientBuildsWhileItStartsIsExplainedUntilTheWorldStartPlusGrace()
    {
        var options = new WitnessOptions { Sources = NoStock, WorldStartGraceMs = 1000, WorldStartMaxMs = 10_000 };
        var w = new ItemWitness(options);
        var c = Content(1);
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(3, Facts(1, c), c, 0), "before the upload begins");
        w.OnWorldStart(3, 100);
        Assert.AreEqual(ItemSource.WorldStart, w.Classify(3, Facts(2, c), c, 200), "a held (positionless) item of the world");
        Assert.AreEqual(ItemSource.WorldStart, w.Classify(3, Facts(3, c, 500, 500, 7), c, 300), "a ground item of the world");
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(4, Facts(4, c), c, 300), "only the player who is starting");
        w.OnWorldEntered(3, 5000);
        Assert.AreEqual(ItemSource.WorldStart, w.Classify(3, Facts(5, c), c, 5900), "inside the grace");
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(3, Facts(6, c), c, 6200), "after the grace");
        // a world start that never completes closes by itself; a second begin after the start does not reopen (the host checks InGame)
        w.OnWorldStart(5, 0);
        Assert.AreEqual(ItemSource.WorldStart, w.Classify(5, Facts(7, c), c, 9000));
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(5, Facts(8, c), c, 10_500), "the maximum");
        w.Forget(3);
        w.OnWorldStart(3, 20_000);
        w.Forget(3);
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(3, Facts(9, c), c, 20_100), "forgotten with the player");
        // the source can be left out
        var off = new ItemWitness(new WitnessOptions { Sources = WitnessOptions.AllSources & ~ItemSourceSet.WorldStart & ~ItemSourceSet.Stock });
        off.OnWorldStart(3, 0);
        Assert.AreEqual(ItemSource.Unexplained, off.Classify(3, Facts(10, c), c, 10), "AUTH_ITEM_SOURCES without worldstart");
        Assert.IsTrue(WitnessOptions.TryParseSources("token,worldstart,purchase", out var set));
        Assert.AreEqual(ItemSourceSet.Token | ItemSourceSet.WorldStart | ItemSourceSet.Purchase, set);
    }

    [TestMethod]
    public void AMerchantPurchaseIsAcceptedOnlyWhenTheHeroGoldPaysForIt()
    {
        var options = new WitnessOptions { Sources = NoStock, PurchaseWindowMs = 1000, PurchasePercent = 50 };
        var w = new ItemWitness(options);
        var c = Content(1);
        ItemFacts Bought(uint reference, uint price) => new(reference, 0x12A, 0, 0, 0, 0, 0, ItemWitness.Fnv(c), Purchase: true, Price: price);
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(3, Facts(1, c), c, 0), "an ordinary creation is not a purchase");
        Assert.AreEqual(ItemSource.Purchase, w.Classify(3, Bought(10, 1000), c, 100));
        w.OnPurchase(3, 10, 1000, 100);
        w.OnGoldSpent(3, 400, 300);                         // less than 50 % of the price: pays nothing
        var unpaid = new uint[ItemWitness.PurchaseRing];
        Assert.AreEqual(0, w.TakeUnpaid(3, 900, unpaid), "inside the window");
        Assert.AreEqual(1, w.TakeUnpaid(3, 1300, unpaid), "not paid in time");
        Assert.AreEqual(10u, unpaid[0]);
        Assert.AreEqual(0, w.TakeUnpaid(3, 1400, unpaid), "reported once");

        w.OnPurchase(3, 11, 1000, 2000);
        w.OnPurchase(3, 12, 400, 2000);
        w.OnGoldSpent(3, 1100, 2100);                       // pays the older one (500 needed) and then the other (200 needed)
        Assert.AreEqual(0, w.TakeUnpaid(3, 9000, unpaid), "both paid");

        w.OnPurchase(4, 13, 1000, 3000);
        w.OnGoldSpent(3, 5000, 3100);                       // another player's gold pays nothing
        Assert.AreEqual(1, w.TakeUnpaid(4, 9000, unpaid));
        var off = new ItemWitness(new WitnessOptions { Sources = WitnessOptions.AllSources & ~ItemSourceSet.Purchase });
        Assert.AreEqual(ItemSource.Unexplained, off.Classify(3, Bought(14, 1), c, 0), "AUTH_ITEM_SOURCES without purchase");
    }

    [TestMethod]
    public void ARewardIsExplainedByAQuestEventOfTheSamePlayerWithinItsWindowAndBudget()
    {
        var w = new ItemWitness(new WitnessOptions { Sources = NoStock, QuestWindowMs = 1000, QuestPerEvent = 2 });
        var c = Content(1);
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(3, Facts(1, c), c, 0), "no quest event yet");
        w.OnQuestEvent(3, 100);
        Assert.AreEqual(ItemSource.Quest, w.Classify(3, Facts(2, c), c, 200), "a held reward");
        Assert.AreEqual(ItemSource.Quest, w.Classify(3, Facts(3, c, 500, 500, 7), c, 300), "a reward on the ground");
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(3, Facts(4, c), c, 400), "budget of the event used up");
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(4, Facts(5, c), c, 400), "another player's event is needed");
        w.OnQuestEvent(3, 5000);
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(3, Facts(6, c), c, 6200), "too late");
        w.Forget(3);
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(3, Facts(7, c), c, 5100), "forgotten with the player");
        Assert.IsTrue(WitnessOptions.TryParseSources("quest", out var set));
        Assert.AreEqual(ItemSourceSet.Quest, set);
    }

    [TestMethod]
    public void TraderStockIsNotJudgedWhenCreatedButWhenItEntersAHeroAndTheGoldMustPayForIt()
    {
        var w = new ItemWitness(new WitnessOptions { PurchaseWindowMs = 1000, PurchasePercent = 50 });
        var c = Content(1);
        ItemFacts Stock(uint reference, uint price) => new(reference, 0x12A, 2, 0, 0, 0, 0, ItemWitness.Fnv(c), Purchase: false, Price: price);
        Assert.AreEqual(ItemSource.Stock, w.Classify(3, Stock(10, 900), c, 0), "positionless, held by nobody");
        Assert.AreEqual(ItemSource.Unexplained, w.Classify(3, Facts(11, c, 500, 500, 7), c, 0), "a ground item is judged by the loot sources");
        w.OnStock(3, 10, 900);
        w.OnStock(3, 12, 1500);
        Assert.IsFalse(w.TakeStock(4, 10, out _), "stock of another player's client");
        Assert.IsTrue(w.TakeStock(3, 10, out var price));
        Assert.AreEqual(900u, price);
        Assert.IsFalse(w.TakeStock(3, 10, out _), "once");
        // it entered a hero: the payment check
        w.OnPurchase(3, 10, price, 100);
        var unpaid = new uint[ItemWitness.PurchaseRing];
        Assert.AreEqual(1, w.TakeUnpaid(3, 1500, unpaid), "no gold left the hero");
        Assert.AreEqual(10u, unpaid[0]);
        Assert.IsTrue(w.TakeStock(3, 12, out price));
        w.OnPurchase(3, 12, price, 2000);
        w.OnGoldSpent(3, 800, 2100);                        // 50 % of 1500 is 750
        Assert.AreEqual(0, w.TakeUnpaid(3, 9000, unpaid), "paid");
        var off = new ItemWitness(new WitnessOptions { Sources = WitnessOptions.AllSources & ~ItemSourceSet.Stock });
        Assert.AreEqual(ItemSource.Unexplained, off.Classify(3, Stock(13, 1), c, 0), "AUTH_ITEM_SOURCES without stock");
    }

    [TestMethod]
    public void AWorldStartItemPutIntoTheHeroWhileTheWindowIsOpenIsTold()
    {
        var w = new ItemWitness(new WitnessOptions { WorldStartGraceMs = 1000 });
        var c = Content(1);
        w.OnWorldStart(3, 0);
        Assert.AreEqual(ItemSource.WorldStart, w.Classify(3, Facts(1, c), c, 100));
        w.OnWorldStartItem(3, 1);
        Assert.IsFalse(w.WorldStartItemAnchored(3, 2, 200), "not a world start item");
        Assert.IsFalse(w.WorldStartItemAnchored(4, 1, 200), "another player");
        Assert.IsTrue(w.WorldStartItemAnchored(3, 1, 200), "put into the hero inside the window");
        Assert.IsFalse(w.WorldStartItemAnchored(3, 1, 300), "once");
        w.OnWorldStartItem(3, 5);
        w.OnWorldEntered(3, 400);
        Assert.IsFalse(w.WorldStartItemAnchored(3, 5, 2000), "the window is closed (a later pick-up of a ground item is normal)");
    }

    [TestMethod]
    public void ThePriceCheckToleratesTheKnownDeviationOfHonestItemsButNotAClearlyLowerPrice()
    {
        Assert.IsFalse(ItemPrices.Deviates(11046, 11045), "off by one (rounding)");
        Assert.IsFalse(ItemPrices.Deviates(1264, 1167), "+8 %, as magic items come out");
        Assert.IsFalse(ItemPrices.Deviates(1505, 1694), "-11 %");
        Assert.IsTrue(ItemPrices.Deviates(1, 3000), "a price a cheating client states");
        Assert.IsTrue(ItemPrices.Deviates(4776, 6513), "-27 %: reported, still paid for by the share rule");
        var prices = ItemPrices.From((type, record) => type == 0x12A ? 5000u : null);
        Assert.IsTrue(prices.TryExpected(0x12A, new byte[130], out var price));
        Assert.AreEqual(5000u, price);
        Assert.IsFalse(prices.TryExpected(0x999, new byte[130], out _), "no price for other kinds");
        Assert.IsFalse(prices.TryExpected(0x12A, new byte[10], out _), "not a value record");
    }
}
