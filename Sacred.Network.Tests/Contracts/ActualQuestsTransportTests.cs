using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Packet")]
public sealed class ActualQuestsTransportTests
{
    [TestMethod]
    public async Task TcpSnapshotResetAndBankImportUpdateTheSameTable()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync();
        var initial = Enumerable.Range(0, 128).Select(i => (byte)i).ToArray();
        var reset = GoldActualQuestsCodec.CreateReset(2, 0x87654321);
        await peer.SendAsync(new(0x1ca, 0, 1, 0, initial), reset);
        var world = new GoldWorldReplica();
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        var expected = initial.ToArray();
        for (var slot = 0; slot < 16; slot++) BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(slot*8), 0x87654321);
        Assert.IsTrue(world.WorldBanks.ActualQuests.TryGetPayload(out var actual));
        CollectionAssert.AreEqual(expected, actual);
        var bank = new byte[45]; BinaryPrimitives.WriteUInt32LittleEndian(bank.AsSpan(32), 5);
        byte[] prefix = [4, 3, 2, 1, 0xff]; prefix.CopyTo(bank, 40);
        await peer.SendAsync(new GoldMessage(0x1b4, 0, 1, 1, bank));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        prefix.CopyTo(expected, 0);
        Assert.IsTrue(world.WorldBanks.ActualQuests.TryGetPayload(out actual));
        CollectionAssert.AreEqual(expected, actual);
        CollectionAssert.AreEqual(bank, world.WorldBanks.Current[0x1b4].ToPayload());
        Assert.AreEqual(0, world.CreatureControls.PendingReplies);
        Assert.AreEqual(0, world.Objects.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OversizedTableRejectsEntireTcpBatch(bool bankForm)
    {
        await using var peer = await GoldLoopbackPeer.StartAsync();
        var payload = new byte[bankForm ? 40+129 : 129];
        if (bankForm) BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(32), 129);
        await peer.SendAsync(new GoldMessage(0x1c9, 0, 1, 0, [1, 0, 0, 0]),
            new GoldMessage(bankForm ? (ushort)0x1b4 : (ushort)0x1ca, 0, 1, 1, payload));
        var world = new GoldWorldReplica();
        var messages = (await peer.ReceiveAsync()).Messages;
        Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(messages, world.Apply));
        Assert.AreEqual(0L, world.Revision);
        Assert.AreEqual(0, world.WorldBanks.ActualQuests.KnownByteCount);
        Assert.AreEqual(0, world.WorldBanks.States.Count);
    }
}
