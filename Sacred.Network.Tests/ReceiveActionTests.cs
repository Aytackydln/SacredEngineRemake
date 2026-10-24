using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.Pak.Items;
using Sacred.Network.Gameplay;
using Sacred.Network.Tests.Infrastructure;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class ReceiveActionTests
{
    [TestMethod]
    public void NamedInvocationUsesReceiptTimeFunctionVectorAcrossLoadingAndReplacement()
    {
        var world = new GoldWorldReplica();
        world.Apply(Event(0x110, Encoding.ASCII.GetBytes("done\0")));
        var first = world.WorldBanks.Invocations.Requests.Single(); Assert.IsFalse(first.TryResolve(out _));
        var rename = ReceiveActionPackets.Functions("Renamed")[4..];
        world.Apply(Event(0x1c4, [..Words(1), ..rename]));
        world.WorldBanks.Functions.Configure(ReceiveActionPackets.Functions("unused", "Done", "DONE"));
        Assert.IsTrue(first.TryResolve(out var index)); Assert.AreEqual(1u, index);
        world.Apply(Event(0x110, Encoding.ASCII.GetBytes("done\0")));
        Assert.IsTrue(world.WorldBanks.Invocations.Requests[1].TryResolve(out index)); Assert.AreEqual(2u, index);
        world.Apply(Event(0x110, Encoding.ASCII.GetBytes("Missing\0")));
        Assert.IsTrue(world.WorldBanks.Invocations.Requests[2].TryResolve(out index)); Assert.IsNull(index);
        world.Clear(); Assert.AreEqual(0, world.WorldBanks.Invocations.Requests.Count);
    }

    [TestMethod]
    public void EmptyAndOversizedNamesResolveSlotZeroWithoutAFunctionBaseline()
    {
        var world = new GoldWorldReplica();
        foreach (var name in new[] { "", new string('x', 64), new string('x', 65) })
            world.Apply(Event(0x110, Encoding.Latin1.GetBytes(name + "\0")));
        foreach (var request in world.WorldBanks.Invocations.Requests)
        { Assert.IsTrue(request.TryResolve(out var index)); Assert.AreEqual(0u, index); }
        var missing = Event(0x110, Encoding.ASCII.GetBytes(new string('x', 63) + "\0"));
        world.Apply(missing); Assert.IsFalse(world.WorldBanks.Invocations.Requests[^1].TryResolve(out _));
    }

    [TestMethod]
    public void PublicRecordMetadataCannotOverrideOwnedWireOperands()
    {
        var world = new GoldWorldReplica(); world.Apply(ReceiveActionPackets.Actor());
        world.Apply(new GoldCreatureTimedControlEvent(new(0x178, 0, 2, 0, Words(500, 42, uint.MaxValue)), 999, 1, 2));
        var actor = world.Objects[500]; Assert.AreEqual(42u, actor.Creature!.Word24D); Assert.AreEqual(uint.MaxValue, actor.Creature.Word259);
        Assert.AreEqual(500u, actor.TimedControl!.ObjectReference);
        world.Apply(new GoldRegionCountdownEvent(new(0x17e, 0, 2, 0, Words(5000, 0x123401ff)), 1, 2));
        Assert.AreEqual(5000u, world.Session.RegionCountdowns.Regions[255].Remaining);
        Assert.IsFalse(world.Session.RegionCountdowns.Regions[255].WarningIssued);
        world.Apply(new GoldScriptInvokeEvent(new(0x110, 0, 2, 0, [0]), "Fake"));
        Assert.AreEqual("", world.WorldBanks.Invocations.Requests.Single().Event.FunctionName);
    }

    [TestMethod]
    public void TimedControlCancelsRouteAndAddsBothFlagsWithoutInventingMotion()
    {
        var world = new GoldWorldReplica(); world.Apply(ReceiveActionPackets.Actor()); var before = world.Objects[500];
        world.Apply(Event(0x178, Words(500, 7, 0xdeadbeef)));
        var actor = world.Objects[500]; var creature = actor.Creature!;
        Assert.AreEqual(before.InstanceVersion, actor.InstanceVersion); Assert.IsTrue(creature.IsSleeping);
        Assert.AreEqual(0xa0060010u, creature.Word200); Assert.AreEqual(7u, creature.Word24D); Assert.AreEqual(0xdeadbeefu, creature.Word259);
        Assert.AreEqual(0u, creature.MovementPathIndex); Assert.AreEqual(0u, creature.MovementPathCount);
        Assert.AreEqual(before.MovementVersion + 1, actor.MovementVersion); Assert.IsNull(actor.Sleep);
        Assert.AreEqual(5u, before.Creature!.MovementPathIndex, "Earlier object snapshots stay immutable.");
        Assert.AreEqual(-3f, actor.TimedControl!.ResetElapsed(0));
        world.Apply(ReceiveActionPackets.Actor()); Assert.IsNull(world.Objects[500].TimedControl);
        Assert.AreEqual(5u, world.Objects[500].Creature!.MovementPathIndex, "A later full snapshot wins.");
    }

    [TestMethod]
    public void CreatureRequestsBindOriginalLifetimeAndFileCategory()
    {
        var world = new GoldWorldReplica();
        world.ConfigureObjectCategories(new Dictionary<uint, SacredItemCategory> { [1] = (SacredItemCategory)32 });
        world.Apply(ReceiveActionPackets.Actor()); var before = world.Objects[500];
        world.Apply(Event(0x13d, Words(500))); world.Apply(Event(0x1d2, Words(500, 222)));
        var requests = world.CreatureActions.Requests.ToArray(); Assert.AreEqual((byte)32, requests[0].MarkerCategory);
        Assert.AreEqual((ushort)1, requests[0].MarkerKind); Assert.AreEqual(-32, requests[0].MarkerOffsetX);
        Assert.AreEqual(10u, requests[1].DeferredScriptCommand); Assert.AreEqual(222u, ((GoldCreatureTypeChangeEvent)requests[1].Event).ObjectType);
        Assert.AreEqual(1u, world.Objects[500].BaseState.ObjectType, "Model/values replacement must execute before publishing an invented new creature.");
        world.Apply(Event(0x167, Words(500))); world.Apply(Event(0x13d, Words(500))); world.Apply(Event(0x1d2, Words(500, 333)));
        Assert.AreEqual(2, world.CreatureActions.Requests.Count); Assert.AreEqual(2L, world.CreatureActions.MissingOrInactiveCount);
        world.Apply(ReceiveActionPackets.Actor()); Assert.AreNotEqual(before.InstanceVersion, world.Objects[500].InstanceVersion);
        Assert.AreEqual(before.InstanceVersion, requests[1].Object.InstanceVersion);
        world.Clear(); Assert.AreEqual(0, world.CreatureActions.Requests.Count); Assert.AreEqual(0L, world.CreatureActions.TimedControlCount);
    }

    [TestMethod]
    public void RegionWarningsLatchOnceAndUseAllNineRecordsAfterCurrentWrite()
    {
        var world = new GoldWorldReplica(); var timers = world.Session.RegionCountdowns;
        foreach (var index in Enumerable.Range(1, 9)) world.Apply(Event(0x17e, Words(999, (uint)index)));
        Assert.AreEqual(9, timers.Warnings.Count); Assert.AreEqual("UI_REGION_FINALBATTLE", timers.Warnings[7].ResourceKey);
        Assert.AreEqual("UI_REGION_ANCARIA", timers.Warnings[8].ResourceKey);
        foreach (var remaining in new uint[] { 0, 1000, uint.MaxValue, 1, 999 }) world.Apply(Event(0x17e, Words(remaining, 0xffffff01)));
        Assert.AreEqual(9, timers.Warnings.Count); Assert.AreEqual(0xffffff01u, timers.Regions[1].SelectorWord);
        Assert.IsTrue(timers.Regions[1].WarningIssued, "Zero and subsequent timer receipts do not reset +34.");
        world.Clear(); Assert.AreEqual(0, timers.Regions.Count); Assert.AreEqual(0, timers.Warnings.Count);
        world.Apply(Event(0x17e, Words(1, 0x123400ff))); Assert.IsTrue(timers.Regions.ContainsKey(255));
        Assert.AreEqual(3u, timers.Warnings.Single().Flags);
    }

    [TestMethod]
    public async Task FragmentedTcpAppliesFiveReceivesAndLateMalformedNameRejectsWholeBatch()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica(); world.Apply(ReceiveActionPackets.Actor());
        await peer.SendAsync(new(0x110, 0, 2, 1, [0]), new(0x13d, 0, 2, 2, Words(500)), new(0x178, 0, 2, 3, Words(500, 17, 42)),
            new(0x17e, 0, 2, 4, Words(10, 0xff)), new(0x1d2, 0, 2, 5, Words(500, 222)));
        var received = await peer.ReceiveAsync(); GoldGameDispatcher.Dispatch(received.Messages, world.Apply);
        Assert.AreEqual(1, world.WorldBanks.Invocations.Requests.Count); Assert.AreEqual(3, world.CreatureActions.Requests.Count);
        Assert.AreEqual(42u, world.Objects[500].Creature!.Word259); Assert.AreEqual(10u, world.Session.RegionCountdowns.Regions[255].Remaining);
        var revision = world.Revision;
        await peer.SendAsync(new(0x178, 0, 2, 6, Words(500, 9, 9)), new(0x17e, 0, 2, 7, Words(5000, 0xff)), new(0x110, 0, 2, 8, [65, 66]));
        received = await peer.ReceiveAsync(); Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(received.Messages, world.Apply));
        Assert.AreEqual(revision, world.Revision); Assert.AreEqual(42u, world.Objects[500].Creature!.Word259);
        Assert.AreEqual(10u, world.Session.RegionCountdowns.Regions[255].Remaining);
    }
}
