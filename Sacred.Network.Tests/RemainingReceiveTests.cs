using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.Pak.Creature;
using Sacred.Network.Gameplay;
using Sacred.Network.Tests.Infrastructure;
using static Sacred.Network.Tests.Infrastructure.RemainingReceivePackets;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class RemainingReceiveTests
{
    [TestMethod]
    public void PartialFxRetainsBaseAndReplacesFullFxLayerForBothMarkers()
    {
        var world = new GoldWorldReplica(); world.Apply(FxObject()); var before = world.Objects[500];
        foreach (var marker in new uint[] { 0xBAADC1FE, 0xBAADC0DE })
        {
            var bytes = PartialFx(1); Put(bytes, 8, marker); Put(bytes, 12, 0x60); Put(bytes, 8 + 178, 17);
            world.Apply(Event(0x14B, bytes)); var after = world.Objects[500];
            Assert.AreEqual(before.BaseState, after.BaseState); Assert.AreEqual(0x60u, after.Fx!.FxType);
            Assert.AreEqual(17u, after.Fx.WearerReference); CollectionAssert.AreEqual(new uint[] { 500 }, after.Fx.References.ToArray());
            Assert.AreEqual(before.Fx!.FxType, 0x20u, "Previous immutable snapshot survives.");
        }
    }
    [TestMethod]
    public void PartialFxSkipsMissingWrongTypeAndInactiveTargets()
    {
        var world = new GoldWorldReplica(); world.Apply(Event(0x14B, PartialFx())); Assert.AreEqual(0, world.Objects.Count);
        world.Apply(LobbyLifecyclePackets.Actor(500)); var actor = world.Objects[500]; world.Apply(Event(0x14B, PartialFx()));
        Assert.AreSame(actor, world.Objects[500]);
        world.Clear(); world.Apply(FxObject()); world.Apply(Event(0x166, Words(500))); var inactive = world.Objects[500];
        world.Apply(Event(0x14B, PartialFx())); Assert.AreSame(inactive, world.Objects[500]);
    }
    [TestMethod]
    public void FxBatchBoundsAndTargetSuppressionPreserveNativeParameters()
    {
        var world = new GoldWorldReplica(); var bytes = Batch(20);
        Put(bytes, 8 + 0x6E0, 0x7FC00001); Put(bytes, 8 + 0x6E0 + 4, BitConverter.SingleToUInt32Bits(-4.9f));
        Put(bytes, 8 + 0x6E0 + 8, BitConverter.SingleToUInt32Bits(float.NegativeInfinity));
        world.Apply(Event(0x181, bytes)); var work = world.ReceiveWork.FxWork;
        Assert.AreEqual(19, work.Count); Assert.AreEqual(0, work[0].Parameter); Assert.AreEqual(-4, work[1].Parameter); Assert.AreEqual(0, work[2].Parameter);
        Put(bytes, 8 + 0x750, 21); Assert.ThrowsException<InvalidDataException>(() => Event(0x181, bytes));
        world.Clear(); world.Apply(LobbyLifecyclePackets.Actor(3, flags: 0x10)); world.Apply(Event(0x181, Batch(1)));
        Assert.AreEqual(0x368u, world.ReceiveWork.FxWork.Single().FxType, "Final target lookup does not reject inactive objects.");
    }
    [TestMethod]
    public void StartupDataRetainsLatchButReplacesBufferOnEmptyReceipt()
    {
        var world = new GoldWorldReplica(); world.Apply(Event(0x132, StartData()));
        Assert.IsTrue(world.ReceiveWork.StartPositionsReady);
        world.Apply(Event(0x132, [..new byte[560], ..Words(0x08000000)]));
        Assert.IsTrue(world.ReceiveWork.StartPositionsReady); Assert.IsTrue(world.ReceiveWork.StartPositions!.DecodedData.IsEmpty);
        var compressed = LobbyLifecyclePackets.Compressed(new byte[] { 1, 2, 3 });
        world.Apply(Event(0x132, [..new byte[560], ..compressed]));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, world.ReceiveWork.StartPositions!.DecodedData.ToArray());
    }
    [TestMethod]
    public void TradeSenderAndNoopBranchesDoNotNeedUnusedFields()
    {
        var world = new GoldWorldReplica(); var t = world.ReceiveWork.Trade;
        world.Apply(Event(0x198, Words(3), 2)); Assert.AreEqual(0, t.Requests.Count);
        world.Apply(Event(0x198, Words(2, 0x20), 2)); Assert.AreEqual(2u, t.ActivePartner);
        world.Apply(Event(0x198, Words(2, 0x21), 2)); Assert.AreEqual(1, t.Requests.Count);
        world.Apply(Event(0x198, Words(2, uint.MaxValue), 2)); Assert.AreEqual(1, t.Requests.Count);
        Assert.ThrowsException<InvalidDataException>(() => Event(0x198, Words(2), 2));
    }
    [TestMethod]
    public void TradeInventoriesAreBoundedByFrameDespiteNativeOverread()
    {
        var world = new GoldWorldReplica(); world.ReceiveWork.Trade.PanelAvailable = true;
        var e = (GoldTradeEvent)Event(0x198, [..Words(2, 0x26, 2, 500, 501), 1, 2, 3], 2);
        world.Apply(e); CollectionAssert.AreEqual(new uint[] { 500, 501 }, e.References.ToArray());
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, world.ReceiveWork.Trade.InventoryData.ToArray());
        Assert.ThrowsException<InvalidDataException>(() => Event(0x198, Words(2, 0x26, uint.MaxValue), 2));
        world.Apply(Event(0x198, [..Words(2, 0x2F, 100, 200), 7, 8], 2));
        Assert.AreEqual(100u, world.ReceiveWork.Trade.Amount); Assert.AreEqual(200u, world.ReceiveWork.Trade.SecondAmount);
        CollectionAssert.AreEqual(new byte[] { 7, 8 }, world.ReceiveWork.Trade.InventoryData.ToArray());
    }
    [TestMethod]
    public void InterpreterCollectionsRetainOrderDuplicatesAndSuffix()
    {
        var bytes = Words(1); // Prefix byte count, then six collection encodings.
        bytes = [..bytes, 0xA5, ..Words(1, 9, 1, 10, 11, 12, 2, 7, 8, 7, 9, 0, 1, 99),
            ..new byte[28], ..Words(1, 1, 2, 3, 4, 1, 42, 2, 50, 51), 0xEF];
        var e = (GoldInterpreterStateEvent)Event(0x1AA, bytes);
        Assert.AreEqual(12, e.Vectors1320.Single().Records.Length); Assert.AreEqual(16, e.Map132C.Length);
        Assert.AreEqual(32, e.Map1344.Length); Assert.AreEqual(16, e.Vector1350.Length);
        Assert.AreEqual(8, e.Vectors135C.Single().Records.Length); Assert.AreEqual((byte)0xEF, e.RemainingData.Span[0]);
        Assert.ThrowsException<InvalidDataException>(() => Event(0x1AA, Words(uint.MaxValue)));
        Assert.ThrowsException<InvalidDataException>(() => Event(0x1AA, [..Words(0, uint.MaxValue), ..new byte[32]]));
    }
    [TestMethod]
    public void ResourceTransfersPublishOwnedTablesAndBannerResetWithoutFiles()
    {
        var world = new GoldWorldReplica(); var bytes = CreatureTable(); var entry = Event(0x1AB, bytes); Array.Fill(bytes, (byte)0xA5); world.Apply(entry);
        Assert.AreEqual(42u, world.ReceiveWork.CreatureTable!.Records[0].ItemType);
        Assert.AreEqual(SacredCreatureClass.Monster, world.ReceiveWork.CreatureTable.Records[0].Class);
        world.Apply(Event(0x1AC, [1, 2, 3])); Assert.IsFalse(world.ReceiveWork.Banner!.ClearsBanner);
        world.Apply(Event(0x1AC, [])); Assert.IsTrue(world.ReceiveWork.Banner!.ClearsBanner);
        var bad = CreatureTable(); Put(bad, 4, uint.MaxValue); Assert.ThrowsException<InvalidDataException>(() => Event(0x1AB, bad));
        world.Clear(); Assert.IsNull(world.ReceiveWork.CreatureTable); Assert.IsNull(world.ReceiveWork.Banner);
    }
    [TestMethod]
    public void ReconciliationRequiresAllocationBaselineAndClearsAllRetainedWork()
    {
        var world = new GoldWorldReplica(); world.Apply(Event(0x197, [])); Assert.AreEqual(1L, world.ReceiveWork.Reconciliation.PendingCount);
        Assert.AreEqual(0, world.ReceiveWork.Reconciliation.Replies.Count);
        world.ReceiveWork.Reconciliation.ConfigureReferences([]); world.Apply(Event(0x197, [1]));
        Assert.AreEqual(GoldReferenceReconciliationReplica.Capacity * 8, world.ReceiveWork.Reconciliation.Replies.Single().Payload.Length);
        world.Apply(Event(0x16A, Controlled())); Assert.AreEqual(100, world.Objects[500].BaseState.X);
        world.Clear(); Assert.AreEqual(0, world.ReceiveWork.ControlledObjects.Count); Assert.AreEqual(0, world.ReceiveWork.Reconciliation.Replies.Count);
    }
}
