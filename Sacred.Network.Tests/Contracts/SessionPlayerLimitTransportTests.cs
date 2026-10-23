using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Packet")]
public sealed class SessionPlayerLimitTransportTests
{
    [TestMethod]
    public async Task TcpCapacityUsesReceiptFlagsAndNeverRepliesOrDisconnects()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync();
        var world = new GoldWorldReplica();
        await peer.SendAsync(Word(0x1e, 0), Word(0x23, uint.MaxValue, [9, 8, 7]));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        Assert.AreEqual(16u, world.Session.MaxPlayers);
        Assert.AreEqual(uint.MaxValue, world.Session.AnnouncedMaxPlayers);
        await peer.SendAsync(Word(0x1e, 0x10), Word(0x23, 8), Word(0x1e, 0), Word(0x23, 0));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        Assert.AreEqual(4u, world.Session.MaxPlayers);
        Assert.AreEqual(0u, world.Session.AnnouncedMaxPlayers);
        Assert.AreEqual(3L, world.Session.PlayerLimitNotificationCount);
        Assert.AreEqual(0u, world.Session.GameFlags);
        Assert.IsFalse(peer.Connection.IsStopping);
        Assert.AreEqual(0L, peer.Connection.Diagnostics.SentMessages);
        Assert.AreEqual(0, world.CreatureControls.PendingReplies);
    }

    [TestMethod]
    public void MissingFlagsStayUnknownAndLaterSnapshotsDoNotRewriteHistory()
    {
        var world = new GoldWorldReplica();
        var tracker = new GoldAdmissionTracker();
        void Apply(GoldMessage message)
        {
            tracker.ProcessBatch([message]); world.Apply(GoldGameEventDecoder.Decode(message));
            Assert.AreEqual(world.Session.MaxPlayers, tracker.Status.MaxPlayers);
        }
        Apply(Word(0x23, 0)); Assert.IsNull(world.Session.MaxPlayers);
        Apply(Word(0x23, 3)); Assert.AreEqual(3u, world.Session.MaxPlayers);
        Apply(Word(0x23, 8)); Assert.IsNull(world.Session.MaxPlayers);
        Apply(Word(0x1e, 0)); Assert.IsNull(world.Session.MaxPlayers);
        Apply(Word(0x23, 8)); Assert.AreEqual(8u, world.Session.MaxPlayers);
        Apply(Word(0x1e, 0x40000)); Assert.AreEqual(8u, world.Session.MaxPlayers);
        Apply(Word(0x23, 8)); Assert.AreEqual(4u, world.Session.MaxPlayers);
        Apply(Word(0x1e, 0)); Assert.AreEqual(4u, world.Session.MaxPlayers);
        Apply(Word(0x23, 0)); Assert.AreEqual(4u, world.Session.MaxPlayers);
        Apply(Word(0x23, 16)); Assert.AreEqual(16u, world.Session.MaxPlayers);
    }

    [TestMethod]
    public void EarlyCapacitySurvivesAdmissionAndPreservesExistingWorldApproval()
    {
        var tracker = new GoldAdmissionTracker();
        tracker.ProcessBatch([Word(0x1e, 0), Word(0x23, 12)]);
        tracker.Begin(); Assert.AreEqual(12u, tracker.Status.MaxPlayers);
        tracker.ProcessBatch([new(0x11, 0, 0, 0, [4, 3, 0, 0, 0, 0]), new(0x17, 0, 0, 0, [])]);
        Assert.AreEqual(12u, tracker.Status.MaxPlayers); Assert.AreEqual(12u, tracker.Status.AnnouncedMaxPlayers);
        tracker.ProcessBatch([Word(0x23, 1)]);
        Assert.AreEqual(1u, tracker.Status.MaxPlayers); Assert.AreEqual((byte)3, tracker.Status.PlayerSlot);
        Assert.AreEqual((byte)4, tracker.Status.Flags); Assert.AreEqual(0u, tracker.Status.SessionGameFlags);
        Assert.IsTrue(tracker.Status.WorldStartup.ParticipantStartApproved);
        Assert.IsTrue(tracker.Status.WorldStartup.HostWorldAvailable);
        Assert.IsFalse(tracker.Status.WorldStartup.AssignedClientHost);
        Assert.AreEqual(GoldAdmissionPhase.SlotAssigned, tracker.Status.Phase);
    }

    [TestMethod]
    public void BadIdentityOrShortWordRollsBackTheWholeBatch()
    {
        var tracker = new GoldAdmissionTracker();
        tracker.ProcessBatch([Word(0x1e, 0), Word(0x23, 12)]);
        var before = tracker.Status;
        foreach (var bad in new[] { Word(0x23, 1) with { Sender = 3, Origin = 3 },
            Word(0x23, 1) with { Origin = 3 }, new GoldMessage(0x23, 0, 0, 0, [1, 2, 3]) })
        {
            Assert.ThrowsException<InvalidDataException>(() => tracker.ProcessBatch([Word(0x23, 4), bad]));
            Assert.AreSame(before, tracker.Status);
        }
        var world = new GoldWorldReplica();
        Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(
            [Word(0x23, 4), new(0x23, 0, 0, 0, [1])], world.Apply));
        Assert.IsNull(world.Session.MaxPlayers); Assert.AreEqual(0L, world.Session.PlayerLimitNotificationCount);
        Assert.AreEqual(0L, world.Revision);
    }

    [TestMethod]
    public void OwnedWireWinsOverForgedEventMetadataAndInvalidOpcodeIsRejected()
    {
        var message = Word(0x23, 3, [9, 8, 7]);
        var entry = GoldGameEventDecoder.Decode(message);
        Array.Fill(message.Payload, (byte)0xcc);
        var world = new GoldWorldReplica(); world.Apply(entry);
        Assert.AreEqual(3u, world.Session.MaxPlayers); Assert.AreEqual((byte)7, entry.Message.Payload[^1]);
        world.Apply(new GoldSessionPlayerLimitEvent(Word(0x23, 1), uint.MaxValue));
        Assert.AreEqual(1u, world.Session.MaxPlayers); Assert.AreEqual(1u, world.Session.AnnouncedMaxPlayers);
        Assert.ThrowsException<InvalidDataException>(() => world.Apply(new GoldSessionPlayerLimitEvent(Word(0xffff, 4), 4)));
        Assert.AreEqual(2L, world.Revision); Assert.AreEqual(2L, world.Session.PlayerLimitNotificationCount);
    }

    [TestMethod]
    public void RepeatedAndZeroAnnouncementsCountAndDisconnectClearsThem()
    {
        var world = new GoldWorldReplica();
        foreach (var count in new uint[] { 4, 4, 0, 0 }) world.Apply(GoldGameEventDecoder.Decode(Word(0x23, count)));
        Assert.AreEqual(4u, world.Session.MaxPlayers); Assert.AreEqual(0u, world.Session.AnnouncedMaxPlayers);
        Assert.AreEqual(4L, world.Session.PlayerLimitNotificationCount);
        Assert.IsNull(world.Session.GameFlags); Assert.IsNull(world.Session.IsLocked);
        world.Clear();
        Assert.IsNull(world.Session.MaxPlayers); Assert.IsNull(world.Session.AnnouncedMaxPlayers);
        Assert.AreEqual(0L, world.Session.PlayerLimitNotificationCount);
        var tracker = new GoldAdmissionTracker();
        Assert.IsNull(tracker.Status.MaxPlayers); Assert.IsNull(tracker.Status.AnnouncedMaxPlayers);
    }

    private static GoldMessage Word(ushort opcode, uint value, byte[]? suffix = null)
    {
        var bytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return new(opcode, 0, 0, 0, [..bytes, ..suffix ?? []]);
    }
}
