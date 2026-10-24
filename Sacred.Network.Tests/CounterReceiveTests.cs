using System.Collections.Frozen;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.GameRes;
using Sacred.Core.Pak.Items;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;
using static Sacred.Network.Tests.Infrastructure.CounterReceivePackets;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class CounterReceiveTests
{
    [TestMethod]
    public void MissingBaselinesPreserveOrderAndReceiptTimeCallbackLookup()
    {
        var world = new GoldWorldReplica(); var counters = world.WorldBanks.Counters;
        world.Apply(Event(0x1a1, Words(17))); world.Apply(Event(0x19f, Words(17)));
        Assert.AreEqual(2, counters.PendingCount);
        world.WorldBanks.Functions.Configure(Functions("unused", "Done", "Other"));
        var replacement = Functions("Changed")[4..]; world.Apply(Event(0x1c4, [..Words(1), ..replacement]));
        var baseline = Bank(0x1b1, (7, new[] { Record(17, 1) })); world.Apply(GoldGameEventDecoder.Decode(baseline));
        Assert.AreEqual(0, counters.PendingCount); Assert.AreEqual(0u, counters.Kills.Single().Remaining);
        Assert.IsTrue(counters.Callbacks.Single().TryResolve(out var index)); Assert.AreEqual(1u, index);
        Assert.AreEqual(2, counters.Messages.Recent.Count);
        Assert.AreEqual(10u, counters.Messages.Recent[1].Completed, "The later notice observes the earlier decrement.");
        world.Apply(Event(0x1a1, Words(17)));
        Assert.AreEqual(uint.MaxValue, counters.Kills.Single().Remaining, "Network counters retain completed rows and wrap on the next notification.");
        Assert.IsTrue(counters.Callbacks[1].TryResolve(out var later)); Assert.IsNull(later);
        CollectionAssert.AreEqual(Record(17, 1), world.WorldBanks.Current[0x1b1].Sections[7].Records[0].Data.ToArray(), "Raw bank stays immutable.");
        world.Clear(); Assert.AreEqual(0, counters.PendingCount); Assert.AreEqual(0, counters.Callbacks.Count);
    }

    [TestMethod]
    public void CollectionUsesFileCategoriesFirstMappingAndArrivalInstances()
    {
        var world = new GoldWorldReplica();
        world.ConfigureObjectCategories(new Dictionary<uint, SacredItemCategory> { [200] = (SacredItemCategory)2 });
        world.Apply(Actor()); var oldInstance = world.Objects[500].InstanceVersion;
        var bank = Bank(0x1b1, (8, new[] { Record(200, 2) }), (9, new[] { Words(200, 17, 123), Words(200, 18, 456) }));
        world.Apply(GoldGameEventDecoder.Decode(bank));
        var message = new GoldMessage(0x1a2, 0, 2, 0, Words(500, 17));
        world.Apply(new GoldCounterCollectEvent(message, 999, 18, true));
        var counters = world.WorldBanks.Counters;
        Assert.AreEqual(17u, counters.Collected.Single().TargetResource);
        Assert.AreEqual(2u, counters.Collected.Single().Remaining, "1A2 remaps and attempts collection without decrementing.");
        world.Apply(Event(0x167, Words(500))); world.Apply(Actor());
        Assert.AreNotEqual(oldInstance, world.Objects[500].InstanceVersion);
        Assert.AreEqual(oldInstance, counters.CollectionAttempts.Single().Source!.InstanceVersion);
        Assert.AreEqual(500u, counters.CollectionAttempts.Single().Event.ObjectReference);
        world.Apply(Event(0x1a3, Words(500, 17)));
        Assert.AreEqual(1u, counters.Collected.Single().Remaining);
    }

    [TestMethod]
    public void EmptyOrNonCreatureCollectedTablesDoNotWaitForIrrelevantMappings()
    {
        var empty = new GoldWorldReplica(); empty.WorldBanks.Counters.ConfigureBaseline([], [], []);
        empty.Apply(Event(0x1a3, Words(500, 17))); Assert.AreEqual(0, empty.WorldBanks.Counters.PendingCount);
        var world = new GoldWorldReplica(); world.ConfigureObjectCategories(new Dictionary<uint, SacredItemCategory>());
        world.Apply(GoldGameEventDecoder.Decode(Bank(0x1b1, (8, new[] { Record(17, 2) }))));
        world.Apply(Event(0x1a3, Words(500, 17))); Assert.AreEqual(0, world.WorldBanks.Counters.PendingCount);
        Assert.AreEqual(1u, world.WorldBanks.Counters.Collected.Single().Remaining);
    }

    [TestMethod]
    public void HudRequestsUseLateResourcesAndBoundOnlyDiagnosticHistory()
    {
        var world = new GoldWorldReplica(); world.ConfigureObjectResources(new Dictionary<uint, uint> { [17] = 1234 });
        var counters = world.WorldBanks.Counters;
        counters.ConfigureBaseline([GoldCounterRecord.FromPayload(Record(17, uint.MaxValue, 0))], [], []);
        world.Apply(Event(0x19f, Words(17)));
        var progress = counters.Messages.Recent.Single(); Assert.IsNull(counters.Messages.Resolve(progress));
        counters.Messages.ConfigureResources(new GameResStore(new Dictionary<uint, string>
        { [1234] = "Target", [SacredResourceHash.Compute("COUNT_MIDDLE")] = "of", [12345] = "Notice" }.ToFrozenDictionary()));
        Assert.AreEqual("Target:1 of 0", counters.Messages.Resolve(progress));
        for (var i = 0; i < 150; i++) world.Apply(Event(0x19e, Words(16, 3, 12345)));
        Assert.AreEqual(151L, counters.Messages.ReceivedCount); Assert.AreEqual(GoldChatReplica.HistoryLimit, counters.Messages.Recent.Count);
        Assert.AreEqual("Notice", counters.Messages.Resolve(counters.Messages.Recent.Last()));
        world.Clear(); Assert.AreEqual(0L, counters.Messages.ReceivedCount);
    }

    [TestMethod]
    public async Task FragmentedTcpAppliesBankThenCountersAndRejectsMalformedBatchAtomically()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        var baseline = Bank(0x1b1, (7, new[] { Record(17, 2) }));
        await peer.SendAsync(baseline, new(0x1a1, 0, 2, 1, Words(17)), new(0x19f, 0, 2, 2, Words(17)));
        var received = await peer.ReceiveAsync(); GoldGameDispatcher.Dispatch(received.Messages, world.Apply);
        Assert.AreEqual(1u, world.WorldBanks.Counters.Kills.Single().Remaining);
        Assert.AreEqual(9u, world.WorldBanks.Counters.Messages.Recent.Single().Completed);
        await peer.SendAsync(new(0x1a1, 0, 2, 3, Words(17)), new(0x1a3, 0, 2, 4, new byte[7]));
        received = await peer.ReceiveAsync();
        Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(received.Messages, world.Apply));
        Assert.AreEqual(1u, world.WorldBanks.Counters.Kills.Single().Remaining);
    }
}
