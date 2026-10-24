using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pax.Hero.Inventory;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class CreatureInteractionTests
{
    [TestMethod]
    public void PickupUsesSenderAndAllowsAbsentItemWhileContainerOpenUsesLocalActor()
    {
        var world = new GoldWorldReplica(); world.ConfigureObjectCategories(new Dictionary<uint, SacredItemCategory> { [1] = SacredItemCategory.Container });
        world.Apply(GoldGameEventDecoder.Decode(CreatureCommandPackets.Admission()));
        world.Apply(CreatureCommandPackets.Actor(2)); world.Apply(CreatureCommandPackets.Actor(3));
        world.Apply(Event(0x143, Words(999, 0x7fc00001), 3)); var work = world.CreatureCommands.Interactions.Pending.Single();
        Assert.AreEqual(3u, work.Subject.BaseState.ObjectReference); Assert.IsNull(work.Target); Assert.AreEqual(0x20cu, work.NativeEventId);
        world.Apply(Actor(container: true)); world.Apply(Event(0x18b, Words(500), 3)); work = world.CreatureCommands.Interactions.Pending[^1];
        Assert.AreEqual(2u, work.Subject.BaseState.ObjectReference); Assert.AreEqual(500u, work.Target!.BaseState.ObjectReference);
        Assert.AreEqual(0u, work.Target.BaseState.Flags & 0x4000, "Receipt does not assume that inventory opening succeeded.");
        world.Apply(Event(0x167, Words(2))); world.Apply(Event(0x18b, Words(500), 3));
        Assert.AreEqual(2, world.CreatureCommands.Interactions.Pending.Count);
    }

    [TestMethod]
    public void FxInputsPreserveUnsignedConversionsAndRetireSourceWithAnOwnedLifetime()
    {
        var world = new GoldWorldReplica(); world.Apply(CreatureCommandPackets.Actor(500, flags: 0x10));
        world.Apply(Event(0x14a, Words(500, 1, 2, 3, 4))); Assert.AreEqual(0, world.CreatureCommands.Interactions.Pending.Count);
        world.Apply(Event(0x14d, Words(500, uint.MaxValue, 0x80000000, 3, 0x7fc00001)));
        var work = world.CreatureCommands.Interactions.Pending.Single();
        Assert.AreEqual(0x4f800000u, BitConverter.SingleToUInt32Bits(work.FxCreation!.ProjectionInput!.Value.X));
        Assert.AreEqual(0x7fc00001u, work.FxCreation.Parameter);
        world.Apply(CreatureCommandPackets.Actor(500)); world.Apply(Event(0x14a, Words(500, uint.MaxValue, 0x80000000, 0x1234ffff, 0x12345678)));
        Assert.AreEqual(new GoldObjectPosition(-1, int.MinValue, 255, 0x5678), world.CreatureCommands.Interactions.Pending[^1].FxCreation!.Position);
        world.Apply(Event(0x14e, Words(500, 1, 2, 3))); work = world.CreatureCommands.Interactions.Pending[^1];
        Assert.AreEqual(0x10u, world.Objects[500].BaseState.Flags & 0x10); Assert.AreEqual(0u, work.Subject.BaseState.Flags & 0x10);
        world.Apply(CreatureCommandPackets.Actor(500)); Assert.AreNotEqual(work.Subject.InstanceVersion, world.Objects[500].InstanceVersion);
        Assert.AreEqual(0x30cu, work.FxCreation!.Type); Assert.IsNull(world.Objects[500].Fx, "A creation request is not a successfully allocated FX object.");
    }

    [TestMethod]
    public void InventoryAndDeathRequestsRevalidateWireAndPreserveBaselines()
    {
        var world = new GoldWorldReplica(); world.Apply(GoldGameEventDecoder.Decode(CreatureCommandPackets.Admission()));
        world.Apply(CreatureCommandPackets.Actor(2, flags: 0x10));
        world.Apply(Event(0x145, Words(123, 9))); Assert.AreEqual(0, world.CreatureCommands.Interactions.Pending.Count);
        world.Apply(GoldGameEventDecoder.Decode(CreatureInteractionPackets.Inventory())); var inventory = world.FindInventory(2)!;
        var msg = new GoldMessage(0x145, 0, 2, 0, Words(123, uint.MaxValue));
        world.Apply(new GoldInventoryInsertEvent(msg, 999, 999)); var work = world.CreatureCommands.Interactions.Pending.Single();
        CollectionAssert.AreEqual(new uint[] { 123, uint.MaxValue, 0 }, work.Arguments.ToArray()); Assert.AreSame(inventory, work.Inventory);
        Assert.AreEqual(0, inventory.QuestCount); Assert.AreEqual(1, world.FindInventory(2)!.QuestCount);
        world.Apply(Event(0x17d, Words(2, 77))); Assert.AreEqual(1, world.CreatureCommands.Interactions.Pending.Count);
        world.Apply(CreatureCommandPackets.Actor(2)); world.Apply(Event(0x17d, Words(2, 77))); work = world.CreatureCommands.Interactions.Pending[^1];
        Assert.AreEqual(0x208u, work.NativeEventId); Assert.AreEqual(77u, work.Arguments.Single());
        Assert.AreEqual(0u, world.Objects[2].BaseState.Flags & 0x10, "Death event execution is separate from source retirement.");
        world.Clear(); Assert.AreEqual(0, world.CreatureCommands.Interactions.Pending.Count);
    }

    [TestMethod]
    public void RegionRecordsWaitForGenerationAndRowBaselineAndKeepReceiptObjects()
    {
        var world = new GoldWorldReplica(); world.Apply(CreatureCommandPackets.Actor(500));
        var region = world.CreatureCommands.Interactions.RegionTypes;
        world.Apply(Event(0x189, CreatureInteractionPackets.Region())); Assert.AreEqual(1, region.WaitingForBaseline.Count);
        var captured = region.WaitingForBaseline.Single().ReceiptObjects.Single();
        world.Apply(Event(0x167, Words(500))); world.Apply(CreatureCommandPackets.Actor(500));
        region.ConfigureGeneration(7); Assert.AreEqual(1, region.WaitingForBaseline.Count, "A matching generation still requires the existing row.");
        region.Seed(3, new byte[172]); Assert.AreEqual(0, region.WaitingForBaseline.Count);
        Assert.AreEqual(1, region.Pending.Count); Assert.AreEqual(2, BinaryPrimitives.ReadInt32LittleEndian(region.Records[3].Span[8..]));
        Assert.AreSame(captured, region.Pending.Single().ReceiptObjects.Single()); Assert.AreNotEqual(captured.InstanceVersion, world.Objects[500].InstanceVersion);
        var record = region.Records[3]; world.Apply(Event(0x189, CreatureInteractionPackets.Region(count: 2)));
        Assert.AreEqual(3, BinaryPrimitives.ReadInt32LittleEndian(region.Records[3].Span[8..]));
        Assert.AreEqual(2, BinaryPrimitives.ReadInt32LittleEndian(record.Span[8..]), "Older table snapshots remain immutable.");
        world.Apply(Event(0x189, CreatureInteractionPackets.Region(count: -2)));
        Assert.AreEqual(2, BinaryPrimitives.ReadInt32LittleEndian(region.Records[3].Span[8..]), "Native signed division and negative append count are preserved.");
        region.ConfigureGeneration(8); world.Apply(Event(0x189, CreatureInteractionPackets.Region(4, 5)));
        Assert.AreEqual(2, BinaryPrimitives.ReadInt32LittleEndian(region.Records[4].Span[8..]), "Generation mismatch replaces without needing the old row.");
        Assert.ThrowsException<InvalidDataException>(() => Event(0x189, CreatureInteractionPackets.Region(count: 42)));
        world.Apply(Event(0x189, CreatureInteractionPackets.Region(-1, int.MaxValue))); Assert.AreEqual(1L, region.IgnoredRegionCount);
        world.Clear(); Assert.AreEqual(0, region.Records.Count); Assert.IsNull(region.GenerationWord);
    }

    [TestMethod]
    public void UnsafeNativeRegionAppendIsRejectedWithoutOverwritingAdjacentRows()
    {
        var world = new GoldWorldReplica(); var region = world.CreatureCommands.Interactions.RegionTypes;
        region.ConfigureGeneration(7); var full = new byte[172]; BinaryPrimitives.WriteInt32LittleEndian(full.AsSpan(8), 20); region.Seed(3, full);
        world.Apply(Event(0x189, CreatureInteractionPackets.Region(count: 2)));
        Assert.AreEqual(1L, region.UnsafeAppendCount); Assert.AreEqual(0, region.Pending.Count);
        CollectionAssert.AreEqual(full, region.Records[3].ToArray());
    }

    [TestMethod]
    public void InventoryQuestPairsUseFirstDuplicateAndZeroWordGatesAndNewSnapshotWins()
    {
        var world = new GoldWorldReplica(); world.Apply(GoldGameEventDecoder.Decode(CreatureCommandPackets.Admission()));
        world.Apply(CreatureCommandPackets.Actor(2)); world.Apply(GoldGameEventDecoder.Decode(CreatureInteractionPackets.Inventory(99, 9)));
        var baseline = world.FindInventory(2)!;
        world.Apply(Event(0x145, Words(123, 7))); world.Apply(Event(0x145, Words(123, 8)));
        Assert.AreEqual(2, world.FindInventory(2)!.QuestCount); Assert.AreEqual(1, baseline.QuestCount);
        world.Apply(Event(0x145, Words(777, 0))); world.Apply(Event(0x145, Words(888, 1)));
        Assert.AreEqual(3, world.FindInventory(2)!.QuestCount);
        Assert.AreEqual(2L, world.CreatureCommands.Interactions.InventoryRecords.SuppressedCount);
        world.Apply(GoldGameEventDecoder.Decode(CreatureInteractionPackets.Inventory())); Assert.AreEqual(0, world.FindInventory(2)!.QuestCount);
        world.Apply(Event(0x145, Words(888, 1))); Assert.AreEqual(1, world.FindInventory(2)!.QuestCount);
        world.Clear(); Assert.AreEqual(0L, world.CreatureCommands.Interactions.InventoryRecords.AppendedCount);
    }

    [TestMethod]
    public void SavedQuestPairAppendPreservesScrollsAndTrailingBytes()
    {
        var bytes = new byte[SacredSavedInventoryLayout.MinimumSize + 8 + 3];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4440), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4444), 0x12345678);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4448), 0x7fc00001); bytes[^3] = 0xA5; bytes[^2] = 0x5A; bytes[^1] = 1;
        var inventory = SacredSavedInventory.FromPayload(bytes);
        var appended = SacredSavedInventoryQuestRecords.Append(inventory, 123, 456);
        Assert.AreEqual(1, appended.QuestCount); Assert.AreEqual(2, appended.ScrollCount);
        Assert.AreEqual(0x7fc00001u, appended.GetScrollWord(1)); CollectionAssert.AreEqual(new byte[] { 0xA5, 0x5A, 1 }, appended.RemainingData.ToArray());
        var pair = SacredSavedInventoryQuestRecords.Read(appended, 0);
        Assert.AreEqual(123u, pair.KeyWord); Assert.AreEqual(456u, pair.Word04); CollectionAssert.AreEqual(bytes, inventory.ToPayload());
    }

    [TestMethod]
    public void DeferredRegionRecordsRetainGenerationAndReceiptOrder()
    {
        var world = new GoldWorldReplica(); var regions = world.CreatureCommands.Interactions.RegionTypes;
        regions.ConfigureGeneration(7); world.Apply(Event(0x189, CreatureInteractionPackets.Region(count: 2)));
        regions.ConfigureGeneration(8); world.Apply(Event(0x189, CreatureInteractionPackets.Region(count: 4, generation: 9)));
        Assert.AreEqual(2, regions.WaitingForBaseline.Count); Assert.AreEqual(0, regions.Records.Count);
        var baseline = new byte[172]; BinaryPrimitives.WriteInt32LittleEndian(baseline.AsSpan(8), 1);
        regions.Seed(3, baseline); Assert.AreEqual(2, regions.Pending.Count);
        Assert.AreEqual(7u, regions.Pending[0].ReceiptGenerationWord); Assert.AreEqual(8u, regions.Pending[1].ReceiptGenerationWord);
        Assert.AreEqual(2, BinaryPrimitives.ReadInt32LittleEndian(regions.Records[3].Span[8..]));
    }

    [TestMethod]
    public async Task FragmentedTcpReceivesAllTenAndMalformedTailRejectsWholeBatch()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        world.ConfigureObjectCategories(new Dictionary<uint, SacredItemCategory> { [1] = SacredItemCategory.Container });
        world.Apply(GoldGameEventDecoder.Decode(CreatureCommandPackets.Admission())); world.Apply(CreatureCommandPackets.Actor(2));
        world.Apply(Actor(container: true)); world.Apply(CreatureCommandPackets.Actor(501));
        world.Apply(GoldGameEventDecoder.Decode(CreatureInteractionPackets.Inventory()));
        await peer.SendAsync(new(0x33, 0, 2, 1, CreatureInteractionPackets.Buddy()), new(0x36, 0, 2, 2, CreatureInteractionPackets.Buddy()),
            new(0x143, 2, 2, 3, Words(500, 1)), new(0x145, 0, 2, 4, Words(123, 9)), new(0x18b, 0, 2, 5, Words(500)),
            new(0x14a, 0, 2, 6, Words(501, 1, 2, 3, 4)), new(0x14d, 0, 2, 7, Words(501, 1, 2, 3, 4)),
            new(0x17d, 0, 2, 8, Words(501, 77)), new(0x189, 0, 2, 9, CreatureInteractionPackets.Region()), new(0x14e, 0, 2, 10, Words(501, 1, 2, 3)));
        var received = await peer.ReceiveAsync(); GoldGameDispatcher.Dispatch(received.Messages, world.Apply);
        Assert.AreEqual(7, world.CreatureCommands.Interactions.Pending.Count); Assert.AreEqual(0x10u, world.Objects[501].BaseState.Flags & 0x10);
        Assert.AreEqual(1, world.Session.Lobby.Buddies.Buddies.Count); Assert.AreEqual(1, world.CreatureCommands.Interactions.RegionTypes.WaitingForBaseline.Count);
        var revision = world.Revision;
        await peer.SendAsync(new(0x36, 0, 2, 11, CreatureInteractionPackets.Buddy(status: 0)), new(0x189, 0, 2, 12, CreatureInteractionPackets.Region(count: 42)));
        received = await peer.ReceiveAsync(); Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(received.Messages, world.Apply));
        Assert.AreEqual(revision, world.Revision); Assert.AreEqual(2u, world.Session.Lobby.Buddies.Buddies.Single().Status);
    }
}
