using System.Collections.Frozen;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.GameRes;
using Sacred.GameplayHost.World;
using Sacred.Network.Gameplay;
using Sacred.Network.Tests.Infrastructure;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class ConsumerDeliveryTests
{
    [TestMethod]
    public void IndependentConsumersPreserveRepeatedReceiptsAndDeferredHeads()
    {
        var world = new GoldWorldReplica(); world.Apply(Actor());
        var first = world.Consumers.CreateCursor("first", _ => true);
        var second = world.Consumers.CreateCursor("second", _ => true);
        Assert.ThrowsException<InvalidOperationException>(() => new GoldWorldReplica().Consumers.TryPeek(first, out _));
        for (var i = 0; i < 2; i++) world.Apply(Event(0x11F, Words(500, 42)));
        Assert.IsTrue(world.Consumers.TryPeek(first, out var head));
        world.Consumers.Report(first, head, GoldConsumerOutcome.Deferred, "waiting");
        Assert.IsTrue(world.Consumers.TryPeek(first, out var same)); Assert.AreSame(head, same);
        world.Consumers.Report(first, head, GoldConsumerOutcome.Deferred, "waiting");
        Assert.AreEqual(1, world.Consumers.Reports.Count, "Unchanged waits must not flood the console.");
        var observed = new List<long>();
        while (world.Consumers.TryPeek(second, out var work))
        { observed.Add(work.Order); world.Consumers.Report(second, work, GoldConsumerOutcome.Consumed, "done"); }
        CollectionAssert.AreEqual(new long[] { 1, 2 }, observed);
        Assert.IsTrue(world.Consumers.TryPeek(first, out same)); Assert.AreSame(head, same);
        world.Consumers.Report(first, same, GoldConsumerOutcome.Consumed, "ready");
        Assert.IsTrue(world.Consumers.TryPeek(first, out var next)); Assert.AreEqual(2L, next.Order);
    }

    [TestMethod]
    public void BoundedHistoryCannotSilentlyDropConsumerWork()
    {
        var world = new GoldWorldReplica(); world.Apply(Actor());
        var cursor = world.Consumers.CreateCursor("slow", _ => true);
        for (var i = 0; i <= world.Consumers.Capacity; i++) world.Apply(Event(0x11F, Words(500, 42)));
        Assert.AreEqual(256, world.Combat.Recent.Count);
        Assert.IsFalse(world.Consumers.TryPeek(cursor, out _));
        Assert.AreEqual(1L, cursor.Lost);
        Assert.AreEqual(1L, world.Consumers.Diagnostics.Faults["slow"]);
        world.Clear(); world.Apply(Actor()); world.Apply(Event(0x11F, Words(500, 43)));
        Assert.IsTrue(world.Consumers.TryPeek(cursor, out var current));
        Assert.AreEqual(world.Consumers.Session, current.Session); Assert.AreEqual(0L, cursor.Lost);
    }

    [TestMethod]
    public void TextConsumptionSurvivesOldHistoryEvictionMovementAndSourceRemoval()
    {
        var world = new GoldWorldReplica(); world.Apply(Actor());
        var text = new GameplayReceivedTexts(world);
        world.Apply(Event(0x11F, Words(500, 42)));
        for (var i = 0; i < 300; i++) world.Apply(Event(0x128, Words(500, 100)));
        world.Apply(Event(0x167, Words(500))); world.Apply(Actor());
        text.Advance(0);
        var displayed = text.Pool.Entries.Single();
        Assert.AreEqual(42u, displayed.TextId); Assert.AreEqual(300, displayed.X); Assert.AreEqual(400, displayed.Y);
        text.Advance(0); Assert.AreEqual(1, text.Pool.Entries.Count());
        world.Clear(); text.Advance(0); Assert.AreEqual(0, text.Pool.Entries.Count());
    }

    [TestMethod]
    public void DamageAndResourceTextShareSlotsWithoutApplyingHealthTwice()
    {
        var world = new GoldWorldReplica(); world.Apply(Actor()); world.Apply(Event(0x128, Words(500, 100)));
        var text = new GameplayReceivedTexts(world);
        for (var i = 0; i < 31; i++) world.Apply(Event(0x11F, Words(500, 42)));
        world.Apply(Event(0x17A, Words(500, 12, 1, 2, 0, 0)));
        world.Apply(Event(0x128, Words(500, 90)));
        text.Advance(0);
        Assert.AreEqual(32, text.Pool.Entries.Count());
        Assert.AreEqual(12, text.Pool.Entries.Single(entry => entry.Resource == 0).Digit);
        Assert.AreEqual(90, world.Objects[500].CurrentHitPoints);
        world.Apply(Event(0x11F, Words(500, 0x42F))); text.Advance(0);
        Assert.AreEqual(32, text.Pool.Entries.Count()); Assert.IsFalse(text.Pool.Entries.Any(entry => entry.Resource == 0));
        world.Apply(Event(0x11F, Words(500, 42))); text.Advance(0); Assert.AreEqual(1L, text.Pool.Dropped);
    }

    [TestMethod]
    public void HudDefersMissingResourcesAndConsumesDuplicatesAndCompletionOnce()
    {
        var world = new GoldWorldReplica(); var hud = new GameplayReceivedHud(world);
        world.Apply(Event(0x19E, Words(16, 3, 123)));
        world.Apply(Event(0x19E, Words(16, 3, 123)));
        hud.Advance(0, GameResStore.Empty); Assert.AreEqual(0, hud.Notices.Count);
        var resources = new GameResStore(new Dictionary<uint, string> { [123] = "Notice", [GoldCounterLayout.KillCompleteText] = "Done" }.ToFrozenDictionary());
        world.WorldBanks.Counters.ConfigureBaseline([GoldCounterRecord.FromPayload(CounterReceivePackets.Record(17, 1))], [], []);
        world.Apply(Event(0x1A1, Words(17)));
        hud.Advance(0, resources); Assert.AreEqual(3, hud.Notices.Count);
        CollectionAssert.AreEqual(new[] { "Notice", "Notice", "Done" }, hud.Notices.Select(notice => notice.Text).ToArray());
        hud.Advance(0, resources); Assert.AreEqual(3, hud.Notices.Count);
        Assert.AreEqual(0u, world.WorldBanks.Counters.Kills.Single().Remaining);
        hud.Advance(7, resources); Assert.AreEqual(3, hud.Notices.Count);
        hud.Advance(1.01f, resources); Assert.AreEqual(0, hud.Notices.Count);
    }

    [TestMethod]
    public void RegionLatchProducesOneLocalizedHudNoticeAndDragCommandClears()
    {
        var world = new GoldWorldReplica(); world.Apply(Actor());
        var drag = new GameplayReceivedDragDrop(world); drag.Update();
        drag.Current = new(1, 2, 500, 3);
        world.Apply(Event(0x13D, Words(500))); drag.Update();
        Assert.IsNull(drag.Current); Assert.AreEqual(1L, drag.Cleared);
        drag.Current = new(1, 2, 500, 3); drag.Update(); Assert.IsNotNull(drag.Current);
        var hud = new GameplayReceivedHud(world);
        world.Apply(Event(0x17E, Words(999, 1))); world.Apply(Event(0x17E, Words(900, 1)));
        var resources = new GameResStore(new Dictionary<uint, string>
        { [SacredResourceHash.Compute("UI_REGION_FINALBATTLE")] = "Warning" }.ToFrozenDictionary());
        hud.Advance(0, resources); Assert.AreEqual("Warning", hud.Notices.Single().Text);
        world.Clear(); hud.Advance(0, resources); drag.Update();
        Assert.AreEqual(0, hud.Notices.Count); Assert.IsNull(drag.Current);
    }

    [TestMethod]
    public void MarkingAndTracingDoNotClearGameplayAndRetainRoutingMetadata()
    {
        var world = new GoldWorldReplica(); world.Apply(Actor()); var lines = new List<string>();
        world.Consumers.Diagnostics.Log = lines.Add; world.Consumers.Diagnostics.Trace = "texts";
        world.Consumers.Diagnostics.SetMark("npc");
        var bytes = Words(500, 42);
        world.Apply(GoldGameEventDecoder.Decode(new(0x11F, 3, 2, 65535, bytes)));
        Assert.AreEqual(1, world.Objects.Count); Assert.AreEqual(1L, world.Consumers.Diagnostics.Opcodes[0x11F]);
        Assert.IsTrue(lines.Any(line => line.Contains("sender 3, origin 2, sequence 65535")));
        var cursor = world.Consumers.CreateCursor("routing", _ => true);
        Assert.IsTrue(world.Consumers.TryPeek(cursor, out var work));
        Assert.AreEqual((byte)3, work.Sender); Assert.AreEqual((byte)2, work.Origin); Assert.AreEqual(65535u, work.Sequence);
        Assert.ThrowsException<InvalidOperationException>(() => world.Consumers.Report(cursor,
            work with { Order = work.Order + 1 }, GoldConsumerOutcome.Consumed, "wrong"));
    }

    [TestMethod]
    public async Task FragmentedTcpRunsMixedConsumersAndMalformedTailPublishesNoWork()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica(); world.Apply(Actor());
        var text = new GameplayReceivedTexts(world); var hud = new GameplayReceivedHud(world);
        await peer.SendAsync(new(0x11F, 0, 2, 0, Words(500, 42)), new(0x19E, 0, 2, 1, Words(16, 3, 123)));
        var received = await peer.ReceiveAsync(); GoldGameDispatcher.Dispatch(received.Messages, world.Apply);
        var resources = new GameResStore(new Dictionary<uint, string> { [123] = "Notice" }.ToFrozenDictionary());
        text.Advance(0); hud.Advance(0, resources);
        Assert.AreEqual(1, text.Pool.Entries.Count()); Assert.AreEqual("Notice", hud.Notices.Single().Text);
        var before = world.Consumers.Published;
        Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(
            [new(0x11F, 0, 2, 2, Words(500, 42)), new(0x19E, 0, 2, 3, [0])], world.Apply));
        Assert.AreEqual(before, world.Consumers.Published);
    }
}
