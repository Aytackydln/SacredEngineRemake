using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Packet")]
public sealed class ScriptRegionTransportTests
{
    [TestMethod]
    public async Task TcpPreservesNativeNewKeyDuplicationAndRawFields()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        await peer.SendAsync(Message(2, 7, 8), Message(uint.MaxValue, 7, 7));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        var regions = world.WorldBanks.ScriptRegions;
        Assert.AreEqual(2, regions.Regions[7].Count); Assert.AreEqual(2, regions.Regions[8].Count);
        Assert.AreEqual(-1, regions.Regions[7][0].FirstX); Assert.AreEqual(8u, regions.Regions[7][0].Timestamp);
        Assert.AreEqual(0, world.CreatureControls.PendingReplies);
    }

    [TestMethod]
    public void BankReplacesEvenWhenEmptyAndRepeatedKeysUseLastVector()
    {
        var world = new GoldWorldReplica(); world.Apply(GoldGameEventDecoder.Decode(Message(1, 7, 8)));
        var a = Message(1, 1, 1).Payload[12..]; var b = a.ToArray(); b[0] = 42;
        world.Apply(GoldGameEventDecoder.Decode(ScriptRegionChecks.Bank((7, a), (7, b), (9, []))));
        Assert.AreEqual(2, world.WorldBanks.ScriptRegions.Regions.Count);
        Assert.AreEqual(-214, world.WorldBanks.ScriptRegions.Regions[7].Single().FirstX);
        Assert.AreEqual(0, world.WorldBanks.ScriptRegions.Regions[9].Count);
        world.Apply(GoldGameEventDecoder.Decode(ScriptRegionChecks.Bank()));
        Assert.AreEqual(0, world.WorldBanks.ScriptRegions.Regions.Count);
    }

    [TestMethod]
    public void NativeRemovalComparesLocalHeaderAndSkipsTheSwappedDuplicate()
    {
        var world = new GoldWorldReplica(); var needle = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(needle.AsSpan(12), 99);
        var ordinary = Message(1, 1, 1).Payload[12..];
        world.Apply(GoldGameEventDecoder.Decode(ScriptRegionChecks.Bank((7, [..needle, ..ordinary, ..needle]))));
        world.Apply(GoldGameEventDecoder.Decode(Message(0, 7, 7)));
        var rows = world.WorldBanks.ScriptRegions.Regions[7];
        Assert.AreEqual(2, rows.Count); CollectionAssert.AreEqual(needle, rows[0].ToPayload());
        CollectionAssert.AreEqual(ordinary, rows[1].ToPayload());
    }

    [TestMethod]
    public void RemovalsVisitBothKeysIgnoreSuppliedRectangleAndDoNotCreateMissingKeys()
    {
        var world = new GoldWorldReplica(); var ordinary = Message(1, 1, 1).Payload[12..];
        world.Apply(GoldGameEventDecoder.Decode(ScriptRegionChecks.Bank((7, [..new byte[32], ..ordinary]), (8, new byte[32]))));
        world.Apply(GoldGameEventDecoder.Decode(Message(0, 7, 8)));
        world.Apply(GoldGameEventDecoder.Decode(Message(1, 9, 9)));
        var state = world.WorldBanks.ScriptRegions;
        Assert.AreEqual(1, state.Regions[7].Count); Assert.AreEqual(0, state.Regions[8].Count);
        Assert.AreEqual(1, state.Regions[9].Count);
        world.Apply(GoldGameEventDecoder.Decode(Message(0, 99, 100)));
        Assert.AreEqual(3, state.Regions.Count);
    }

    [TestMethod]
    public async Task TcpTruncatedRecordRejectsWholeBatch()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        var bad = Message(1, 7, 8); bad = bad with { Payload = bad.Payload[..43] };
        await peer.SendAsync(Message(1, 7, 8), bad); var received = await peer.ReceiveAsync();
        Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(received.Messages, world.Apply));
        Assert.AreEqual(0L, world.Revision); Assert.AreEqual(0, world.WorldBanks.ScriptRegions.Regions.Count);
    }

    [TestMethod]
    public void WireFieldsWinOverForgedMetadataExportsAreOwnedAndDisconnectClearsState()
    {
        var world = new GoldWorldReplica(); var wire = Message(1, 7, 8);
        world.Apply(new GoldScriptRegionEvent(wire, 0, 99, 99, GoldScriptRegionRecord.FromPayload(new byte[32])));
        var record = world.WorldBanks.ScriptRegions.Regions[7].Single(); var bytes = record.ToPayload(); bytes[0] = 0;
        Assert.AreEqual(-1, record.FirstX); Assert.IsFalse(world.WorldBanks.ScriptRegions.Regions.ContainsKey(99));
        world.Apply(GoldGameEventDecoder.Decode(Message(0, 7, 8))); world.Clear();
        Assert.AreEqual(0, world.WorldBanks.ScriptRegions.Regions.Count);
    }

    private static GoldMessage Message(uint mode, uint first, uint second)
    {
        var bytes = new byte[44]; var words = new[] { mode, first, second, uint.MaxValue, 2u, 3u, 4u, 5u, 6u, 7u, 8u };
        for (var i = 0; i < words.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i*4), words[i]);
        return new(0x1c5, 0, 1, 0, bytes);
    }
}
