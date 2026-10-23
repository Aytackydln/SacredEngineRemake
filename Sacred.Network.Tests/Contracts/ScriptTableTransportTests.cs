using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Packet")]
public sealed class ScriptTableTransportTests
{
    [TestMethod]
    public async Task TcpReplacementsClearOneTableAndPreserveOtherTablesAndTailBytes()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync();
        var world = new GoldWorldReplica();
        await peer.SendAsync(new GoldMessage(0x1d3, 0, 1, 0, new byte[152]), new GoldMessage(0x1d4, 0, 2, 1, new byte[76]),
            new GoldMessage(0x1d5, 0, 3, 2, Enumerable.Range(0, 25).Select(i => (byte)i).ToArray()));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        var tables = world.WorldBanks.ScriptTables.Tables;
        Assert.AreEqual(3, tables.Count); Assert.AreEqual(2, tables[0x1d3].Count);
        Assert.AreEqual(1, tables[0x1d4].Count); Assert.AreEqual(2, tables[0x1d5].Count);
        Assert.AreEqual((byte)24, tables[0x1d5].RemainingData[0]);
        var retained = tables[0x1d5];
        await peer.SendAsync(new GoldMessage(0x1d3, 2, 4, 3, []), new GoldMessage(0x1d4, 3, 5, 4, [0xa5, 0x5a]));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        Assert.AreEqual(0, tables[0x1d3].Count); Assert.AreEqual(0, tables[0x1d3].RemainingData.Length);
        Assert.AreEqual(0, tables[0x1d4].Count);
        CollectionAssert.AreEqual(new byte[] { 0xa5, 0x5a }, tables[0x1d4].RemainingData.ToArray());
        Assert.AreSame(retained, tables[0x1d5]);
        Assert.AreEqual(0, world.CreatureControls.PendingReplies);
        Assert.AreEqual(0, world.Objects.Count);
    }

    [TestMethod]
    public async Task MalformedSiblingPacketPreventsPartialTableReplacement()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync();
        await peer.SendAsync(new GoldMessage(0x1d3, 0, 1, 0, new byte[76]), new GoldMessage(0x1cf, 0, 1, 1, new byte[59]));
        var received = await peer.ReceiveAsync(); var world = new GoldWorldReplica();
        Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(received.Messages, world.Apply));
        Assert.AreEqual(0L, world.Revision); Assert.AreEqual(0, world.WorldBanks.ScriptTables.Tables.Count);
    }
}
