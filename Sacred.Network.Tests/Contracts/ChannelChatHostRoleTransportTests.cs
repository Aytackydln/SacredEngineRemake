using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Packet")]
public sealed class ChannelChatHostRoleTransportTests
{
    [TestMethod]
    public async Task TcpReceivesAllFiveOpcodesInOrderWithoutEcho()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        await peer.SendAsync(Chat(3, 1, "private"), Role(true), Chat(4, 0, "team"), Role(false), Chat(5, 16, "public"));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        CollectionAssert.AreEqual(new[] { "private", "team", "public" }, world.Session.ChannelChat.Messages.Select(row => row.Event.Text).ToArray());
        CollectionAssert.AreEqual(new[] { GoldChatChannel.Private, GoldChatChannel.Team, GoldChatChannel.Public },
            world.Session.ChannelChat.Messages.Select(row => row.Event.Channel).ToArray());
        Assert.IsFalse(world.Session.ClientHostRole.IsClientHost); Assert.AreEqual(2L, world.Session.ClientHostRole.ChangeCount);
        Assert.IsFalse(peer.Connection.IsStopping); Assert.AreEqual(0L, peer.Connection.Diagnostics.SentMessages);
        Assert.AreEqual(0, world.CreatureControls.PendingReplies);
    }

    [TestMethod]
    public void RoleChangesUpdateCurrentAuthorizationAndPreserveStartupMilestones()
    {
        var tracker = new GoldAdmissionTracker(); tracker.Begin();
        tracker.ProcessBatch([ChannelChatHostRoleChecks.Admission(false), new(0x17, 0, 0, 0, [])]);
        var world = new GoldWorldReplica(); world.Apply(GoldGameEventDecoder.Decode(ChannelChatHostRoleChecks.Admission(false)));
        foreach (var host in new[] { true, true, false, false })
        {
            var message = Role(host); tracker.ProcessBatch([message]); world.Apply(GoldGameEventDecoder.Decode(message));
            Assert.AreEqual(host, tracker.Status.WorldStartup.AssignedClientHost); Assert.AreEqual(host, world.Session.ClientHostRole.IsClientHost);
            Assert.IsTrue(tracker.Status.WorldStartup.HostWorldAvailable); Assert.IsTrue(tracker.Status.WorldStartup.ParticipantStartApproved);
            Assert.AreEqual((byte)4, tracker.Status.Flags); Assert.AreEqual((byte)3, tracker.Status.PlayerSlot);
        }
        Assert.AreEqual(4L, world.Session.ClientHostRole.NotificationCount); Assert.AreEqual(2L, world.Session.ClientHostRole.ChangeCount);
        Assert.AreEqual("UI_ERROR_HOST_TRANSFERED2", world.Session.ClientHostRole.LastNotificationKey);
    }

    [TestMethod]
    public async Task MalformedLaterChatRejectsAllRoleAndChatChangesAtomically()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        var bad = Chat(4, 3, "bad"); bad = bad with { Payload = bad.Payload[..^1] };
        await peer.SendAsync(Role(true), Chat(3, 0, "valid"), bad); var received = await peer.ReceiveAsync();
        Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(received.Messages, world.Apply));
        Assert.IsNull(world.Session.ClientHostRole.IsClientHost); Assert.AreEqual(0L, world.Revision);
        Assert.AreEqual(0L, world.Session.ChannelChat.ReceivedCount);
        var tracker = new GoldAdmissionTracker(); var before = tracker.Status;
        Assert.ThrowsException<InvalidDataException>(() => tracker.ProcessBatch(received.Messages)); Assert.AreSame(before, tracker.Status);
    }

    [TestMethod]
    public void RoleNotificationsRequireServerIdentityInTransport()
    {
        var tracker = new GoldAdmissionTracker(); tracker.Begin(); tracker.ProcessBatch([ChannelChatHostRoleChecks.Admission(true)]);
        var before = tracker.Status;
        foreach (var message in new[] { Role(false) with { Sender = 3 }, Role(false) with { Origin = 3 } })
        {
            Assert.ThrowsException<InvalidDataException>(() => tracker.ProcessBatch([Role(false), message]));
            Assert.AreSame(before, tracker.Status); Assert.IsTrue(tracker.Status.WorldStartup.AssignedClientHost);
        }
    }

    [TestMethod]
    public void Utf16SkippedPrefixAndSuffixRemainOwnedWithNoInventedPrefixMeaning()
    {
        foreach (var opcode in new ushort[] { 3, 4, 5 })
        {
            var message = Chat(opcode, 3, "\ud800世界\udfff"); message.Payload.AsSpan(0, 4).Fill(0xff);
            message = message with { Payload = [..message.Payload, 0xa5] }; var original = message.Payload.ToArray();
            var entry = (GoldChannelChatEvent)GoldGameEventDecoder.Decode(message); Array.Fill(message.Payload, (byte)0xcc);
            Assert.AreEqual("\ud800世界\udfff", entry.Text); CollectionAssert.AreEqual(original, entry.Message.Payload);
            var bad = new GoldMessage(opcode, 0, 0, 0, [0, 0, 0, 0, 65, 0, 0]);
            Assert.ThrowsException<InvalidDataException>(() => GoldChannelChatCodec.Decode(bad));
        }
        var role = Role(true) with { Payload = [9, 8, 7] }; var decoded = GoldGameEventDecoder.Decode(role); role.Payload[0] = 0;
        CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, decoded.Message.Payload);
    }

    [TestMethod]
    public void OriginAndLocalIgnoreMaskControlDeliveryIncludingSlot16()
    {
        var world = new GoldWorldReplica(); var state = world.Session.ChannelChat;
        state.SetPlayerIgnored(1, true); state.SetPlayerIgnored(16, true);
        foreach (var origin in new byte[] { 0, 1, 3, 16, 17, 31 }) world.Apply(GoldGameEventDecoder.Decode(Chat(5, origin, "text")));
        CollectionAssert.AreEqual(new byte[] { 0, 3 }, state.Messages.Select(row => row.Event.Message.Origin).ToArray());
        Assert.AreEqual(6L, state.ReceivedCount); Assert.AreEqual(4L, state.SuppressedCount); Assert.IsNull(state.LastDelivery);
        state.SetPlayerIgnored(16, false); world.Apply(GoldGameEventDecoder.Decode(Chat(3, 16, "allowed")));
        Assert.AreEqual("allowed", state.LastDelivery!.Event.Text);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => state.SetPlayerIgnored(0, true));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => state.SetPlayerIgnored(17, true));
    }

    [TestMethod]
    public void CachedNamesAreCapturedAtReceiptAndPreSessionTextDoesNotInventAName()
    {
        var world = new GoldWorldReplica();
        world.Apply(GoldGameEventDecoder.Decode(ChannelChatHostRoleChecks.Roster("Before")));
        world.Apply(GoldGameEventDecoder.Decode(Chat(3, 3, "pre-session")));
        Assert.IsFalse(world.Session.ChannelChat.Messages[0].IncludeSpeaker); Assert.IsNull(world.Session.ChannelChat.Messages[0].SpeakerName);
        world.Session.ChannelChat.SetConnectionActive(true);
        world.Apply(GoldGameEventDecoder.Decode(Chat(3, 3, "connected before slot assignment")));
        Assert.AreEqual("Before3", world.Session.ChannelChat.Messages[1].SpeakerName);
        world.Apply(GoldGameEventDecoder.Decode(ChannelChatHostRoleChecks.Admission(false)));
        world.Apply(GoldGameEventDecoder.Decode(Chat(4, 3, "old name")));
        world.Apply(GoldGameEventDecoder.Decode(ChannelChatHostRoleChecks.Roster("After")));
        world.Apply(GoldGameEventDecoder.Decode(Chat(5, 3, "new name")));
        Assert.AreEqual("Before3", world.Session.ChannelChat.Messages[2].SpeakerName);
        Assert.AreEqual("After3", world.Session.ChannelChat.Messages[3].SpeakerName);
    }

    [TestMethod]
    public void HistoriesAreBoundedReadOnlyAndDisconnectClearsRolePreferencesAndNames()
    {
        var world = new GoldWorldReplica(); var state = world.Session.ChannelChat;
        world.Apply(GoldGameEventDecoder.Decode(ChannelChatHostRoleChecks.Admission(true)));
        for (var i = 0; i < GoldChatReplica.HistoryLimit + 2; i++) world.Apply(GoldGameEventDecoder.Decode(Chat(5, 1, i.ToString())));
        Assert.AreEqual(GoldChatReplica.HistoryLimit, state.Messages.Count); Assert.AreEqual("2", state.Messages[0].Event.Text);
        Assert.AreEqual((long)GoldChatReplica.HistoryLimit + 2, state.ReceivedCount);
        Assert.ThrowsException<NotSupportedException>(() => ((IList<GoldChannelChatDelivery>)state.Messages).Clear());
        state.SetPlayerIgnored(16, true); world.Clear();
        Assert.IsNull(world.Session.ClientHostRole.IsClientHost); Assert.AreEqual(0, state.Messages.Count);
        Assert.AreEqual((ushort)0, state.IgnoredPlayersMask); Assert.IsNull(state.LastDelivery);
        world.Apply(GoldGameEventDecoder.Decode(Chat(3, 3, "new session"))); Assert.IsFalse(state.LastDelivery!.IncludeSpeaker);
    }

    [TestMethod]
    public void HostRoleChangesDoNotApproveWorldGenerationOrModifyPauseAndGameFlags()
    {
        var world = new GoldWorldReplica(); var tracker = new GoldAdmissionTracker(); tracker.Begin();
        tracker.ProcessBatch([new(0x11, 0, 0, 0, [0, 3, 0, 0, 0, 0]), Role(true)]);
        Assert.IsTrue(tracker.Status.WorldStartup.AssignedClientHost); Assert.IsFalse(tracker.Status.WorldStartup.HostWorldAvailable);
        Assert.IsFalse(tracker.Status.WorldStartup.ParticipantStartApproved);
        GoldGameDispatcher.Dispatch([new(0x1e, 0, 0, 0, [4, 0, 0, 0]), new(0x135, 0, 3, 0, []), Role(true)], world.Apply);
        Assert.IsTrue(world.Session.IsPaused); Assert.IsTrue(world.Session.IsLocked); Assert.AreEqual(4u, world.Session.GameFlags);
        Assert.AreEqual(0, world.Objects.Count); Assert.AreEqual(0, world.SectorWork.PendingCount); Assert.AreEqual(0, world.CreatureControls.PendingReplies);
    }

    [TestMethod]
    public void WireOverridesForgedTextAndWrongOpcodesCannotMasqueradeAsRoles()
    {
        var world = new GoldWorldReplica(); world.Apply(new GoldChannelChatEvent(Chat(4, 0, "actual"), "forged"));
        Assert.AreEqual("actual", world.Session.ChannelChat.Messages.Single().Event.Text);
        var revision = world.Revision;
        Assert.ThrowsException<InvalidDataException>(() => world.Apply(new GoldClientHostRoleEvent(new(0x135, 0, 0, 0, []))));
        Assert.AreEqual(revision, world.Revision); Assert.IsNull(world.Session.ClientHostRole.IsClientHost);
        Assert.ThrowsException<InvalidDataException>(() => GoldChannelChatCodec.Decode(Chat(0x32, 0, "text")));
    }

    private static GoldMessage Role(bool host) => new((ushort)(host ? 0xd : 0xe), 0, 0, 0, []);
    private static GoldMessage Chat(ushort opcode, byte origin, string text)
    {
        var bytes = new byte[6 + text.Length * 2]; ChannelChatHostRoleChecks.WriteText(bytes, 4, text);
        return new(opcode, 0, origin, 7, bytes);
    }
}
