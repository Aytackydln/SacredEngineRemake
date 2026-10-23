using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Packet")]
public sealed class ScriptGroupTransportTests
{
    [TestMethod]
    public async Task TcpPreservesGroupMembershipOrderWithoutEchoOrObjectRequirement()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        await peer.SendAsync(Message(0, 42, 500), Message(0, 42, 501), Message(0, 42, 502), Message(0, 42, 500), Message(1, 42, 500));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        CollectionAssert.AreEqual(new uint[] { 502, 501 }, world.WorldBanks.ScriptGroups.Groups[42].ToArray());
        Assert.AreEqual(0, world.Objects.Count); Assert.AreEqual(0, world.CreatureControls.PendingReplies);
        Assert.AreEqual(0L, peer.Connection.Diagnostics.SentMessages); Assert.IsFalse(peer.Connection.IsStopping);
    }

    [TestMethod]
    public void ConditionalLengthsMatchTheFieldsActuallyReadByGold()
    {
        foreach (var words in new uint[][] { [uint.MaxValue, 1], [17, 0, 2], [0, 0, uint.MaxValue], [0, 0, 0, 0, 0], [0, 0, 1, 0, 0] })
        {
            var bytes = ScriptGroupChecks.Words(words); var message = new GoldMessage(0x18f, 0, 0, 0, bytes);
            for (var size = 0; size < bytes.Length; size++)
                Assert.ThrowsException<InvalidDataException>(() => GoldGameEventDecoder.Decode(message with { Payload = bytes[..size] }));
            var entry = (GoldScriptGroupEvent)GoldGameEventDecoder.Decode(message);
            Assert.AreEqual(words[0], entry.Destination);
            Assert.AreEqual(words.Length == 5, entry.Group.HasValue);
        }
        Assert.ThrowsException<InvalidDataException>(() => GoldScriptGroupCodec.Decode(Message(0, 42, 500) with { Opcode = 0x190 }));
    }

    [TestMethod]
    public void NonemptyBankReplacesGroupsButEmptyBankRetainsThemAndFirstEqualKeyWins()
    {
        var world = new GoldWorldReplica(); world.Apply(GoldGameEventDecoder.Decode(Message(0, 99, 500)));
        world.Apply(GoldGameEventDecoder.Decode(ScriptGroupChecks.Bank((42, [1, 1, 2]), (42, [9]), (7, []))));
        Assert.AreEqual(2, world.WorldBanks.ScriptGroups.Groups.Count);
        Assert.IsFalse(world.WorldBanks.ScriptGroups.Groups.ContainsKey(99));
        world.Apply(GoldGameEventDecoder.Decode(ScriptGroupChecks.Bank()));
        CollectionAssert.AreEqual(new uint[] { 1, 1, 2 }, world.WorldBanks.ScriptGroups.Groups[42].ToArray());
        world.Apply(GoldGameEventDecoder.Decode(Message(1, 42, 1)));
        CollectionAssert.AreEqual(new uint[] { 2, 1 }, world.WorldBanks.ScriptGroups.Groups[42].ToArray());
        Assert.AreEqual(0, world.WorldBanks.ScriptGroups.Groups[7].Count);
    }

    [TestMethod]
    public void InsertsCanExceed1000ButAnyRemovalThenResetsTheGroup()
    {
        var world = new GoldWorldReplica();
        world.Apply(GoldGameEventDecoder.Decode(ScriptGroupChecks.Bank((42, Enumerable.Range(0, 1000).Select(i => (uint)i).ToArray()))));
        world.Apply(GoldGameEventDecoder.Decode(Message(0, 42, uint.MaxValue)));
        Assert.AreEqual(1001, world.WorldBanks.ScriptGroups.Groups[42].Count);
        world.Apply(GoldGameEventDecoder.Decode(Message(1, 42, 12345)));
        Assert.AreEqual(0, world.WorldBanks.ScriptGroups.Groups[42].Count);
        world.Apply(GoldGameEventDecoder.Decode(Message(1, 99, 0)));
        Assert.IsFalse(world.WorldBanks.ScriptGroups.Groups.ContainsKey(99));
    }

    [TestMethod]
    public async Task TcpTruncatedActiveOperationRejectsTheEntireBatch()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        var bad = Message(1, 42, 500); bad = bad with { Payload = bad.Payload[..19] };
        await peer.SendAsync(Message(0, 42, 500), bad);
        var batch = await peer.ReceiveAsync();
        Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(batch.Messages, world.Apply));
        Assert.AreEqual(0, world.WorldBanks.ScriptGroups.Groups.Count); Assert.AreEqual(0L, world.Revision);
    }

    [TestMethod]
    public void WireWinsOverForgedMetadataAndPublicViewsCannotMutateTheReplica()
    {
        var world = new GoldWorldReplica(); var message = Message(0, 42, 500);
        world.Apply(new GoldScriptGroupEvent(message, 0, 1, 1, 99, 123));
        var groups = world.WorldBanks.ScriptGroups.Groups;
        CollectionAssert.AreEqual(new uint[] { 500 }, groups[42].ToArray()); Assert.IsFalse(groups.ContainsKey(99));
        Assert.ThrowsException<NotSupportedException>(() => ((IList<uint>)groups[42]).Add(123));
        Assert.ThrowsException<NotSupportedException>(() => ((IDictionary<uint, IReadOnlyList<uint>>)groups).Clear());
        var bad = new GoldScriptGroupEvent(message with { Payload = [] }, 0, 0, 0, 42, 500);
        var revision = world.Revision;
        Assert.ThrowsException<InvalidDataException>(() => world.Apply(bad)); Assert.AreEqual(revision, world.Revision);
        world.Clear(); Assert.AreEqual(0, groups.Count);
    }

    private static GoldMessage Message(uint action, uint group, uint member) => new(0x18f, 0, 31, 7,
        ScriptGroupChecks.Words(17, 0, action, group, member));
}
