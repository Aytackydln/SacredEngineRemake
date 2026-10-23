using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Packet")]
public sealed class MovementTriggerInsertionTests
{
    [TestMethod]
    public async Task TcpDuplicateInsertionsAppendAndEmptyNameBankPreservesRegistrations()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        world.MovementTriggers.ConfigureSectors(MovementTriggerInsertionChecks.Index());
        await peer.SendAsync(Message("Gate"), Message("Gate"), new GoldMessage(0x1b2, 0, 1, 0, new byte[40]));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        Assert.AreEqual(9, world.MovementTriggers.Sectors.Count);
        Assert.IsTrue(world.MovementTriggers.Sectors.Values.All(bank => bank.Records.Count == 2));
        Assert.AreEqual(2, world.MovementTriggers.Names.Count); Assert.AreEqual(2, world.MovementTriggers.AppliedInsertionCount);
        Assert.AreEqual(0, world.CreatureControls.PendingReplies);
    }

    [TestMethod]
    public async Task TcpUnsafeNameRejectsWholeBatchBeforeStateChanges()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        var bad = Message("Gate"); Array.Fill(bad.Payload, (byte)'X', 24, 64);
        await peer.SendAsync(Message("Gate"), bad);
        var received = await peer.ReceiveAsync();
        Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(received.Messages, world.Apply));
        Assert.AreEqual(0L, world.Revision); Assert.AreEqual(0, world.MovementTriggers.Names.Count);
    }

    [TestMethod]
    public void LateSectorLoadingPreservesInsertionBankAndDeletionOrder()
    {
        var early = new GoldWorldReplica(); var late = new GoldWorldReplica(); var index = MovementTriggerInsertionChecks.Index();
        early.MovementTriggers.ConfigureSectors(index);
        var bankRecord = GoldMovementTriggerPlanner.CreateReceived(GoldMovementTriggerInsertion.FromPayload(Message("Replacement").Payload), index.CreateGrid())
            .Single(piece => piece.Sector == 1).Record.ToPayload();
        foreach (var message in new[] { Message("Gate"), SectorBank(1, bankRecord), Message("Other"), Delete("Gate"), Message("After") })
        {
            early.Apply(GoldGameEventDecoder.Decode(message)); late.Apply(GoldGameEventDecoder.Decode(message));
        }
        Assert.AreEqual(3, late.MovementTriggers.PendingInsertionCount); Assert.AreEqual(1, late.MovementTriggerDeletions.Instructions.Count);
        late.MovementTriggers.ConfigureSectors(index);
        foreach (var sector in early.MovementTriggers.Sectors)
            CollectionAssert.AreEqual(sector.Value.Records.SelectMany(r => r.ToPayload()).ToArray(),
                late.MovementTriggers.Sectors[sector.Key].Records.SelectMany(r => r.ToPayload()).ToArray());
        Assert.AreEqual(0, late.MovementTriggers.PendingInsertionCount); Assert.AreEqual(0, late.MovementTriggerDeletions.Instructions.Count);
        Assert.AreEqual(early.MovementTriggers.RemovedRecordCount, late.MovementTriggers.RemovedRecordCount);
        CollectionAssert.AreEqual(new[] { "Replacement", "Other", "After" }, late.MovementTriggers.Sectors[1].Records.Select(r => r.Name).ToArray());
    }

    [TestMethod]
    public void WireOverridesForgedFieldsAndKeepsRawSurfaceAndTrailer()
    {
        var world = new GoldWorldReplica(); world.MovementTriggers.ConfigureSectors(MovementTriggerInsertionChecks.Index());
        var message = Message("Gate"); BinaryPrimitives.WriteInt32LittleEndian(message.Payload.AsSpan(12), -1);
        message.Payload[231] = 0xfe;
        world.Apply(new GoldMovementTriggerInsertionEvent(message, GoldMovementTriggerInsertion.FromPayload(Message("Forged").Payload)));
        Assert.IsTrue(world.MovementTriggers.Sectors.Values.All(bank => bank.Records.Single().Surface == 65535 && bank.Records.Single().Name == "Gate"));
        Assert.AreEqual((byte)0xfe, world.MovementTriggers.Names.Single().ToPayload()[127]);
        var copy = world.MovementTriggers.Names.Single().ToPayload(); copy[0] = 0;
        Assert.AreEqual("Gate", world.MovementTriggers.Names.Single().Name);
        Assert.ThrowsException<InvalidOperationException>(() => world.MovementTriggers.ConfigureSectors(MovementTriggerInsertionChecks.Index()));
    }

    [TestMethod]
    public void HoleStillAppendsNameAndNonemptyBankReplacesOnlyNameTable()
    {
        var world = new GoldWorldReplica(); world.MovementTriggers.ConfigureSectors(MovementTriggerInsertionChecks.Index([]));
        world.Apply(GoldGameEventDecoder.Decode(Message("Gate")));
        Assert.AreEqual(0, world.MovementTriggers.Sectors.Count); Assert.AreEqual(1, world.MovementTriggers.Names.Count);
        var name = new byte[128]; Encoding.Latin1.GetBytes("Bank").CopyTo(name, 0);
        world.Apply(GoldGameEventDecoder.Decode(ScriptResourceChecks.Bank(0x1b2, 1, [name])));
        Assert.AreEqual("Bank", world.MovementTriggers.Names.Single().Name);
        Assert.AreEqual(1, world.MovementTriggers.AppliedInsertionCount);
    }

    [TestMethod]
    public void DisconnectDropsPendingRoutingAndAllowsNewCampaign()
    {
        var world = new GoldWorldReplica(); world.Apply(GoldGameEventDecoder.Decode(Message("Old")));
        Assert.AreEqual(1, world.MovementTriggers.PendingInsertionCount);
        world.Clear(); world.MovementTriggers.ConfigureSectors(MovementTriggerInsertionChecks.Index());
        Assert.AreEqual(0, world.MovementTriggers.PendingInsertionCount); Assert.AreEqual(0, world.MovementTriggers.Names.Count);
        Assert.AreEqual(0, world.MovementTriggers.Sectors.Count); Assert.AreEqual(0, world.MovementTriggers.AppliedInsertionCount);
    }

    private static GoldMessage Message(string name)
    {
        var bytes = new byte[232];
        var words = new[] { -1, 60, 62, 4, 130, 129 };
        for (var i = 0; i < words.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i*4), words[i]);
        Encoding.Latin1.GetBytes(name).CopyTo(bytes, 24); Encoding.Latin1.GetBytes(name).CopyTo(bytes, 104);
        return new(0x1c3, 0, 1, 0, bytes);
    }
    private static GoldMessage Delete(string name) => new(GoldMovementTriggerDeletionLayout.Opcode, 0, 1, 0,
        GoldMovementTriggerDeletion.Create(0, -1, 0, 0, 0, Encoding.Latin1.GetBytes(name)).ToPayload());
    private static GoldMessage SectorBank(uint sector, byte[] record)
    {
        var bytes = new byte[48+record.Length]; BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), sector); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(44), 1);
        record.CopyTo(bytes, 48); return new(0x1b2, 0, 1, 0, bytes);
    }
}
