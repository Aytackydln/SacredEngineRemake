using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Packet")]
public sealed class TeamTransportTests
{
    [TestMethod]
    public async Task TcpAdmissionInvitationsJoinsAndLeaderDepartureStayOrderedWithoutReply()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync();
        var world = new GoldWorldReplica();
        await peer.SendAsync(new GoldMessage(0x11, 0, 0, 0, [4, 2, 0, 0, 0, 0]),
            Message(0x139, 7, 2), Message(0x139, 2, 7));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        var teams = world.Session.Teams;
        Assert.AreEqual(1u << 6, teams.IncomingInvitations);
        Assert.AreEqual(1u << 6, teams.OutgoingInvitations);
        await peer.SendAsync(Message(0x138, 7, 3), Message(0x138, 2, 3), Message(0x13b, 9, 3, 7));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        Assert.AreEqual(3u, teams.GetTeam(2)); Assert.IsTrue(teams.IsLeader(2));
        Assert.AreEqual(0u, teams.GetTeam(7)); Assert.AreEqual(0u, teams.IncomingInvitations);
        Assert.AreEqual(0u, teams.OutgoingInvitations); Assert.AreEqual(5L, teams.NotificationCount);
        Assert.IsFalse(peer.Connection.IsStopping); Assert.AreEqual(0L, peer.Connection.Diagnostics.SentMessages);
        Assert.AreEqual(0, world.CreatureControls.PendingReplies);
    }

    [TestMethod]
    public void SnapshotReplacesAllSlotsAndMasksWithoutNormalizingRawWords()
    {
        var world = World(); var slots = new uint[16];
        slots[0] = 0x80000002; slots[1] = 2; slots[15] = uint.MaxValue;
        Apply(world, Snapshot(slots, 0xdeadbeef, 0x12345678));
        var teams = world.Session.Teams;
        Assert.IsTrue(teams.HasSnapshot); Assert.AreEqual(2u, teams.GetTeam(1)); Assert.IsTrue(teams.IsLeader(1));
        Assert.AreEqual(0x7fffffffu, teams.GetTeam(16)); Assert.AreEqual(uint.MaxValue, teams.Slots[15]);
        Assert.AreEqual(0xdeadbeefu, teams.OutgoingInvitations); Assert.AreEqual(0x12345678u, teams.IncomingInvitations);
        Apply(world, Snapshot(new uint[16], 0, 0));
        Assert.IsTrue(teams.Slots.All(value => value == 0)); Assert.AreEqual(0u, teams.IncomingInvitations);
        Assert.AreEqual(0u, teams.OutgoingInvitations);
    }

    [TestMethod]
    public void FullTeamRejectsEvenAnExistingMemberButStillClearsInvitations()
    {
        var world = World(); var slots = new uint[16];
        Array.Fill(slots, 2u, 0, 8); slots[0] |= GoldTeamCodec.LeaderFlag;
        Apply(world, Snapshot(slots, 0xffff, 0xffff));
        Apply(world, Message(0x138, 1, 2));
        var teams = world.Session.Teams;
        Assert.IsTrue(teams.IsLeader(1)); Assert.AreEqual(0xfffeu, teams.OutgoingInvitations);
        Assert.AreEqual(0xfffeu, teams.IncomingInvitations);
        Apply(world, Message(0x138, 9, 2)); Assert.AreEqual(0u, teams.GetTeam(9));
        Assert.AreEqual(0xfefeu, teams.OutgoingInvitations);
        Apply(world, Message(0x138, 9, 3)); Assert.AreEqual(3u, teams.GetTeam(9)); Assert.IsTrue(teams.IsLeader(9));
    }

    [TestMethod]
    public void LeavingLeaderPromotesFirstRemainingSlotAndIgnoresInformationalTeamWord()
    {
        var world = World(); var slots = new uint[16];
        slots[0] = 2; slots[3] = 0x80000002; slots[6] = 2;
        Apply(world, Snapshot(slots, 0, 0));
        Apply(world, Message(0x13b, 16, 999, 4));
        Assert.IsTrue(world.Session.Teams.IsLeader(1)); Assert.AreEqual(0u, world.Session.Teams.GetTeam(4));
        Apply(world, Message(0x13b, 16, 0, 1)); Assert.IsTrue(world.Session.Teams.IsLeader(7));
        Apply(world, Message(0x13b, 16, 2, 7)); Assert.IsTrue(world.Session.Teams.Slots.All(value => value == 0));
    }

    [TestMethod]
    public void LocalJoinClearsOwnOutgoingInvitesAndInvalidOperandsUseNativeNoOps()
    {
        var world = World(); Apply(world, Snapshot(new uint[16], 0x8000ffff, 0x4000ffff));
        Apply(world, Message(0x138, 2, 17)); // Invalid team still executes invitation reset.
        Assert.AreEqual(0u, world.Session.Teams.GetTeam(2));
        Assert.AreEqual(0u, world.Session.Teams.OutgoingInvitations);
        Assert.AreEqual(0x4000fffdu, world.Session.Teams.IncomingInvitations);
        foreach (var origin in new byte[] { 0, 17, 31 }) Apply(world, Message(0x138, origin, 1));
        foreach (var player in new uint[] { 0, 17, uint.MaxValue }) Apply(world, Message(0x139, 2, player));
        Assert.IsTrue(world.Session.Teams.Slots.All(value => value == 0));
        Apply(world, Message(0x138, 2, 0x80000002));
        Assert.AreEqual(2u, world.Session.Teams.GetTeam(2)); Assert.IsTrue(world.Session.Teams.IsLeader(2));
        Apply(world, Message(0x138, 2, 2)); Assert.IsFalse(world.Session.Teams.IsLeader(2));
        Apply(world, Message(0x138, 2, 0x80000000)); Assert.AreEqual(0u, world.Session.Teams.GetTeam(2));
    }

    [TestMethod]
    public void OwnedWireAndOriginOverrideForgedMetadataAndMalformedBatchIsAtomic()
    {
        var world = World(); var slots = new uint[16]; slots[2] = 0x80000005;
        world.Apply(new GoldAdmissionEvent(new(0x11, 0, 0, 0, [4, 2, 0, 0, 0, 0]), 0, 16, 0));
        Assert.AreEqual((byte)2, world.Session.Teams.LocalPlayerSlot);
        var message = Snapshot(slots, 8, 16); var entry = GoldGameEventDecoder.Decode(message);
        Array.Fill(message.Payload, (byte)0xcc); Apply(world, Message(0x138, 4, 1)); world.Apply(entry);
        Assert.AreEqual(5u, world.Session.Teams.GetTeam(3));
        world.Apply(new GoldTeamJoinEvent(Message(0x138, 7, 2), uint.MaxValue));
        Assert.AreEqual(2u, world.Session.Teams.GetTeam(7));
        world.Apply(new GoldTeamSnapshotEvent(Snapshot(new uint[16], 1, 2), slots, uint.MaxValue, uint.MaxValue));
        Assert.IsTrue(world.Session.Teams.Slots.All(value => value == 0));
        Assert.AreEqual(1u, world.Session.Teams.OutgoingInvitations); Assert.AreEqual(2u, world.Session.Teams.IncomingInvitations);
        var revision = world.Revision; var notifications = world.Session.Teams.NotificationCount;
        Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(
            [Message(0x138, 4, 3), new(0x13a, 0, 0, 0, new byte[71])], world.Apply));
        Assert.AreEqual(revision, world.Revision); Assert.AreEqual(notifications, world.Session.Teams.NotificationCount);
        Assert.ThrowsException<InvalidDataException>(() => world.Apply(new GoldTeamInviteEvent(Message(0xffff, 2, 3), 3)));
    }

    [TestMethod]
    public void RepeatedInvitesCountWithoutSnapshotAndClearResetsIdentityAndState()
    {
        var world = World(); var teams = world.Session.Teams;
        Apply(world, Message(0x139, 2, 2)); Apply(world, Message(0x139, 2, 2));
        Assert.AreEqual(2u, teams.OutgoingInvitations); Assert.AreEqual(2u, teams.IncomingInvitations);
        Assert.AreEqual(2L, teams.NotificationCount); Assert.IsFalse(teams.HasSnapshot);
        world.Clear(); Assert.AreEqual((byte)0, teams.LocalPlayerSlot); Assert.AreEqual(0L, teams.NotificationCount);
        Assert.AreEqual(0u, teams.OutgoingInvitations); Assert.AreEqual(0u, teams.IncomingInvitations);
        Assert.IsFalse(teams.HasSnapshot); Assert.IsTrue(teams.Slots.All(value => value == 0));
    }

    private static GoldWorldReplica World()
    {
        var world = new GoldWorldReplica();
        Apply(world, new(0x11, 0, 0, 0, [4, 2, 0, 0, 0, 0]));
        return world;
    }
    private static void Apply(GoldWorldReplica world, GoldMessage message) => world.Apply(GoldGameEventDecoder.Decode(message));
    private static GoldMessage Message(ushort opcode, byte origin, params uint[] words)
    {
        var bytes = new byte[words.Length * 4 + 3];
        for (var i = 0; i < words.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), words[i]);
        bytes[^1] = 0xa5;
        return new(opcode, 0, origin, 0, bytes);
    }
    private static GoldMessage Snapshot(uint[] slots, uint outgoing, uint incoming) =>
        Message(0x13a, 0, [..slots, outgoing, incoming]);
}
