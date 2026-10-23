using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Packet")]
public sealed class PacketTransportTests
{
    public static IEnumerable<object[]> Cases() => PacketCatalog.Rows();
    [TestMethod, DynamicData(nameof(Cases), DynamicDataSourceType.Method)]
    public async Task FragmentedTcpDispatchesTheExpectedEvent(string id)
    {
        var packet = PacketCatalog.Get(id);
        await using var peer = await GoldLoopbackPeer.StartAsync();
        await peer.SendAsync(packet.Message());
        var received = await peer.ReceiveAsync();
        var events = new List<GoldGameEvent>(); GoldGameDispatcher.Dispatch(received.Messages, events.Add);
        Assert.AreEqual(1, events.Count, id); Assert.AreEqual(packet.EventType, events[0].GetType(), id);
        packet.Verify?.Invoke(events[0]);
        CollectionAssert.AreEqual(packet.Payload(), events[0].Message.Payload, id);
    }

    [TestMethod]
    public async Task MalformedTypedBatchFromTcpHasNoPartialDelivery()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync();
        await peer.SendAsync(new GoldMessage(0x1cb, 0, 1, 1, new byte[24]), new GoldMessage(0x1cc, 0, 1, 2, new byte[23]));
        var received = await peer.ReceiveAsync(); var world = new GoldWorldReplica();
        Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(received.Messages, world.Apply));
        Assert.AreEqual(0L, world.Revision); Assert.AreEqual(0, world.WorldBanks.Spatial.MapIcons.Count);
    }
}
