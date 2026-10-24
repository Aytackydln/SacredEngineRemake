using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.GameRes;
using Sacred.Core.Pak.Items;
using Sacred.Network.Gameplay;
using Sacred.Network.Tests.Contracts;
using Sacred.Network.Tests.Infrastructure;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class QuestDeltaTests
{
    [TestMethod]
    public void NestedCountsAndDeclaredBodyAreValidatedBeforeAnyEventIsDelivered()
    {
        void Reject(ushort opcode, byte[] body)
        {
            var deliveries = 0;
            Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(
                [new(0x170, 0, 0, 0, Words(500, 1, 2, 3)), new(opcode, 0, 0, 0, body)], _ => deliveries++));
            Assert.AreEqual(0, deliveries);
        }
        // Both vectors in database records, and all nested header vector/count boundaries.
        foreach (var opcode in new ushort[] { 0x1b9, 0x1bc })
            foreach (var offset in new[] { 28, 32 })
            {
                var body = new byte[36]; BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(offset), uint.MaxValue); Reject(opcode, body);
            }
        foreach (var offset in new[] { 92, 96, 100, 104 })
        {
            var body = new byte[108]; BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(offset), uint.MaxValue); Reject(0x1bb, body);
        }
        var child = new byte[136];
        BinaryPrimitives.WriteUInt32LittleEndian(child.AsSpan(104), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(child.AsSpan(108), 6); // exhausts the mandatory 24 fixed bytes
        Reject(0x1bb, child);
        Reject(0x1be, [..Words(0, uint.MaxValue), ..new byte[32]]);
        Reject(0x1be, [..Words(0, 28), ..new byte[32]]); // inner stream cannot borrow four bytes from the tail
        Reject(0x1b8, [..Words(0), ..new byte[100], ..Words(26, 0)]);
        foreach (var opcode in new ushort[] { 0x1ba, 0x1bb, 0x1bc, 0x1bd, 0x15d })
        {
            var packet = PacketCatalog.All.Single(row => row.Opcode == opcode).Message();
            BinaryPrimitives.WriteUInt32LittleEndian(packet.Payload, uint.MaxValue); Reject(opcode, packet.Payload);
        }
    }

    [TestMethod]
    public void PartialServerEntryPreservesFullFieldsAndReceiptTimeFunctionNames()
    {
        var world = new GoldWorldReplica(); var entries = world.WorldBanks.Quests.Entries;
        var vectors = new byte[4 + 3 * 84]; BinaryPrimitives.WriteUInt32LittleEndian(vectors, 3);
        Encoding.Latin1.GetBytes("zero").CopyTo(vectors, 4);
        Encoding.Latin1.GetBytes("OnQuest").CopyTo(vectors, 88);
        Encoding.Latin1.GetBytes("ONQUEST").CopyTo(vectors, 172); // duplicate hash selects the first function
        var body = new byte[292];
        var hash = SacredResourceHash.ComputeDefaultCLocale("onquest"u8);
        foreach (var offset in GoldQuestLayout.EntryCallbackOffsets) BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(offset), hash);
        world.Apply(Event(0x1ba, [..Words(0), ..body]));
        var retained = entries.Find(0)!;
        Assert.IsNull(retained.ResolveCallback(0));
        world.WorldBanks.Functions.Configure(vectors);
        Assert.AreEqual(1u, retained.ResolveCallback(0));
        var rename = vectors.AsSpan(88, 84).ToArray(); rename.AsSpan(0, 64).Clear(); Encoding.Latin1.GetBytes("Changed").CopyTo(rename, 0);
        world.Apply(Event(0x1c4, [..Words(1), ..rename]));
        Assert.AreEqual(1u, retained.ResolveCallback(0), "A later function update cannot change the earlier packet's reference mapping.");
        world.Apply(Event(0x1ba, [..Words(0), ..body]));
        Assert.AreEqual(2u, entries.Find(0)!.ResolveCallback(0));
        var header = new byte[40]; BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 2);
        world.Apply(Event(0x1b3, [..header, ..Words(123, 456)]));
        Assert.IsTrue(entries.Find(0)!.HasFullRecord);
        Assert.AreEqual(123u, entries.Find(0)!.RuntimeWord);
        Assert.AreEqual(2u, entries.Find(0)!.ResolveCallback(0));
        Assert.IsFalse(entries.Find(1)!.HasFullRecord);
        Assert.AreEqual(456u, entries.Find(1)!.RuntimeWord);
        Assert.IsNull(entries.Find(1)!.ToPayload());
        world.Apply(Event(0x1b3, new byte[40]));
        Assert.AreEqual(2u, entries.Count, "Native empty bank sections preserve these vectors.");
    }

    [TestMethod]
    public void PendingProgressAndHeaderEntriesPreserveArrivalOrderAndDoNotInventBaselines()
    {
        var world = new GoldWorldReplica(); var quests = world.WorldBanks.Quests;
        world.Apply(Event(0x1b8, [..Words(0, 0), ..new byte[96], ..Words(1, uint.MaxValue)]));
        world.Apply(Event(0x1be, [..Words(0, 32), ..new byte[32], ..Words(7)]));
        Assert.AreEqual(2, quests.PendingCount);
        world.Apply(GoldGameEventDecoder.Decode(Bank(0x1b4, (1, new[] { new byte[104] }))));
        Assert.AreEqual(2, quests.PendingCount, "A known empty entry vector cannot satisfy an out-of-range progress index.");
        var header = new byte[108]; BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(88), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(92), 0xfeffffff);
        world.Apply(GoldGameEventDecoder.Decode(Bank(0x1b4, (1, new[] { header }))));
        Assert.AreEqual(0, quests.PendingCount);
        Assert.AreEqual(7u, BinaryPrimitives.ReadUInt32LittleEndian(quests.Headers.Find(0)!.Vectors[0].Data),
            "Later 1BE overwrites the earlier progress result.");
        Assert.AreEqual(33, quests.Headers.Find(0)!.Vectors[0].Count);
        world.Clear(); Assert.AreEqual(0, quests.PendingCount);
    }

    [TestMethod]
    public void InsertRequiresAnActiveCreatureAndPreservesUnrelatedLayers()
    {
        var world = new GoldWorldReplica();
        world.Apply(Actor()); var actor = world.Objects[500]; var source = actor.Creature!.ToPayload();
        world.Apply(new GoldQuestInsertEvent(new(0x1b9, 0, 2, 0, [..Words(500), ..new byte[32]]), 999,
            ((GoldQuestInsertEvent)Event(0x1b9, [..Words(500), ..new byte[32]])).Record));
        var updated = world.Objects[500];
        Assert.AreEqual(0, updated.Physical!.QuestNumber);
        Assert.AreEqual(0, updated.Creature!.PhysicalState.QuestNumber);
        Assert.AreEqual(0xa0000200u, updated.Creature.Word200);
        Assert.AreEqual(actor.InstanceVersion, updated.InstanceVersion);
        CollectionAssert.AreEqual(source, actor.Creature.ToPayload());
        world.Apply(Event(0x1b9, [..Words(500), ..new byte[32]]));
        Assert.AreEqual(2u, world.WorldBanks.Quests.Database.Count);
        Assert.AreEqual(1, world.Objects[500].Physical!.QuestNumber);
        world.Apply(Actor(0x10)); world.Apply(Event(0x1b9, [..Words(500), ..new byte[32]]));
        Assert.AreEqual(2u, world.WorldBanks.Quests.Database.Count);
        world.Apply(Actor(physicalOnly: true)); world.Apply(Event(0x1b9, [..Words(500), ..new byte[32]]));
        Assert.AreEqual(2u, world.WorldBanks.Quests.Database.Count);
    }

    [TestMethod]
    public void ObjectRequestsRetainArrivalInstancesAndDoNotAssumeVirtualSuccess()
    {
        var world = new GoldWorldReplica(); world.ConfigureObjectCategories(new Dictionary<uint, SacredItemCategory> { [1] = SacredItemCategory.Container });
        world.Apply(Actor(container: true));
        world.Apply(Event(0x18d, Words(500)));
        Assert.AreEqual(0, world.ObjectPresentation.Pending.Count);
        world.Apply(Actor(0x4000, container: true));
        world.Apply(Event(0x18d, Words(500)));
        var first = world.ObjectPresentation.Pending.Single();
        Assert.AreEqual(0u, first.Object.BaseState.Flags & 0x4000);
        world.Apply(Event(0x170, Words(500, 0xffffffff, 0x7fc00001, 0x80000000)));
        Assert.AreEqual(2, world.ObjectPresentation.Pending.Count);
        Assert.AreEqual(0u, world.Objects[500].Physical!.Word64);
        Assert.AreNotEqual(0u, world.Objects[500].BaseState.Flags & 0x02000000);
        world.Apply(Event(0x167, Words(500)));
        world.Apply(Actor(container: true));
        Assert.AreNotEqual(first.Object.InstanceVersion, world.Objects[500].InstanceVersion, "Queued callbacks retain the previous lifetime.");
        world.Clear(); Assert.AreEqual(0, world.ObjectPresentation.Pending.Count);
    }

    [TestMethod]
    public async Task ActualTcpReceiptAppliesQuestTablesAndObjectGuardsInOrder()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica(); world.Apply(Actor());
        await peer.SendAsync(new(0x1b9, 0, 2, 1, [..Words(500), ..new byte[32]]),
            new(0x1b8, 0, 2, 2, [..Words(uint.MaxValue), ..new byte[100], ..Words(1, 0)]),
            new(0x170, 0, 2, 3, Words(500, 0xaa, 0x3f800000, 0x3f800000)));
        var batch = await peer.ReceiveAsync(); GoldGameDispatcher.Dispatch(batch.Messages, world.Apply);
        Assert.AreEqual(1u, world.WorldBanks.Quests.Database.Count);
        Assert.AreEqual(0, world.WorldBanks.Quests.PendingCount);
        Assert.AreEqual(1, world.ObjectPresentation.Pending.Count);
        Assert.AreEqual(0, world.Objects[500].Physical!.QuestNumber);
    }
}
