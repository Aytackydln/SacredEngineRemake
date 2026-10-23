using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Packet")]
public sealed class InactiveReceiveHookTransportTests
{
    private static readonly ushort[] Opcodes = [0x18e, 0x190, 0x191, 0x192, 0x193, 0x194, 0x195];

    [TestMethod]
    public async Task TcpHooksPreserveWorldStateAndAllowLaterMessagesWithoutEcho()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        await peer.SendAsync(new GoldMessage(0x1e, 0, 0, 0, [4, 0, 0, 0]));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply); var revision = world.Revision;
        await peer.SendAsync(Opcodes.SelectMany(opcode => new[] { Message(opcode, []), Message(opcode, [1, 2, 3]) }).ToArray());
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        Assert.AreEqual(revision, world.Revision); Assert.AreEqual(4u, world.Session.GameFlags);
        Assert.AreEqual(0, world.CreatureControls.PendingReplies); Assert.AreEqual(0, world.WorldBanks.Functions.PendingCount);
        await peer.SendAsync(new GoldMessage(0x135, 0, 0, 0, []));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        Assert.IsTrue(world.Session.IsPaused); Assert.AreEqual(revision + 1, world.Revision);
        Assert.IsFalse(peer.Connection.IsStopping); Assert.AreEqual(0L, peer.Connection.Diagnostics.SentMessages);
    }

    [TestMethod]
    public void HooksHaveNoAdmissionTransitionAtAnyJoinPhase()
    {
        var tracker = new GoldAdmissionTracker();
        var messages = Opcodes.Select(opcode => Message(opcode, [9]) with { Sender = 31, Origin = 31 }).ToArray();
        var before = tracker.Status; tracker.ProcessBatch(messages); Assert.AreEqual(before, tracker.Status);
        tracker.Begin(); before = tracker.Status; tracker.ProcessBatch(messages); Assert.AreEqual(before, tracker.Status);
        tracker.ProcessBatch([new(0x11, 0, 0, 0, [4, 3, 0, 0, 0, 0]), new(0x17, 0, 0, 0, [])]);
        before = tracker.Status; tracker.ProcessBatch(messages); Assert.AreEqual(before, tracker.Status);
        Assert.IsTrue(tracker.Status.WorldStartup.ParticipantStartApproved);
        Assert.AreEqual((byte)3, tracker.Status.PlayerSlot);
    }

    [TestMethod]
    public void ArbitraryIgnoredPayloadsAreOwnedAndHaveNoInventedFieldsOrState()
    {
        var world = new GoldWorldReplica();
        foreach (var opcode in Opcodes)
        {
            var bytes = Enumerable.Range(0, 512).Select(i => (byte)i).ToArray(); var expected = bytes.ToArray();
            var message = Message(opcode, bytes) with { Sender = 17, Origin = 31, Sequence = ushort.MaxValue };
            var entry = GoldGameEventDecoder.Decode(message); Array.Fill(bytes, (byte)0xcc);
            CollectionAssert.AreEqual(expected, entry.Message.Payload); Assert.IsInstanceOfType<GoldInactiveReceiveHookEvent>(entry);
            var roundTrip = GoldMessageCodec.DecodeBatch(GoldMessageCodec.Encode(entry.Message)).Single();
            Assert.AreEqual((byte)31, roundTrip.Origin); Assert.AreEqual((byte)17, roundTrip.Sender);
            Assert.AreEqual(ushort.MaxValue, roundTrip.Sequence); CollectionAssert.AreEqual(expected, roundTrip.Payload);
            world.Apply(entry);
        }
        Assert.AreEqual(0L, world.Revision); Assert.AreEqual(0L, world.SessionVersion);
        Assert.IsNull(world.Session.GameFlags); Assert.IsNull(world.Session.IsPaused);
        Assert.AreEqual(0, world.Objects.Count); Assert.AreEqual(0, world.SectorWork.PendingCount);
    }

    [TestMethod]
    public void MalformedLaterTrafficStillRejectsTheWholeBatch()
    {
        var messages = new[] { new GoldMessage(0x1e, 0, 0, 0, [4, 0, 0, 0]), Message(0x190, []), new GoldMessage(0x43, 0, 0, 0, [0]) };
        var world = new GoldWorldReplica(); var delivered = 0;
        Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(messages, entry => { delivered++; world.Apply(entry); }));
        Assert.AreEqual(0, delivered); Assert.AreEqual(0L, world.Revision); Assert.IsNull(world.Session.GameFlags);
        var tracker = new GoldAdmissionTracker(); var before = tracker.Status;
        Assert.ThrowsException<InvalidDataException>(() => tracker.ProcessBatch(messages)); Assert.AreSame(before, tracker.Status);
    }

    [TestMethod]
    public void NeighboringActiveOpcodeAndUnknownTrafficAreNotClassifiedAsInactive()
    {
        foreach (var opcode in new ushort[] { 0x18d, 0x18f, 0x196, 0xffff, 0x135 })
            Assert.ThrowsException<InvalidDataException>(() => GoldInactiveReceiveHookCodec.Decode(Message(opcode, [])));
        Assert.IsInstanceOfType<GoldScriptGroupEvent>(GoldGameEventDecoder.Decode(Message(0x18f, new byte[20])));
        Assert.IsInstanceOfType<GoldUnknownEvent>(GoldGameEventDecoder.Decode(Message(0xffff, [])));
        var world = new GoldWorldReplica();
        Assert.ThrowsException<InvalidDataException>(() => world.Apply(new GoldInactiveReceiveHookEvent(Message(0x135, []))));
        Assert.IsNull(world.Session.IsPaused); Assert.AreEqual(0L, world.Revision);
    }

    private static GoldMessage Message(ushort opcode, byte[] bytes) => new(opcode, 0, 3, 7, bytes);
}
