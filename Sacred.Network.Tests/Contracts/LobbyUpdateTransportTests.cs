using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Packet")]
public sealed class LobbyUpdateTransportTests
{
    [TestMethod]
    public async Task FiveLobbyUpdatesArriveOverTcpWithoutAdmissionOrReplies()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        world.Session.Notices.ConfigureResources(LobbyUpdateChecks.Resources());
        GoldMessage[] messages = [Text(0x27, 4, "Hero"), new(0x2d, 0, 0, 0, [42, 0, 0, 0]),
            new(0x2e, 0, 0, 0, [0, 0, 0, 0]), Text(0x2f, 4, "Player"), Text(0x31, 8, "bye")];
        await peer.SendAsync(messages); var batch = (await peer.ReceiveAsync()).Messages;
        var tracker = new GoldAdmissionTracker(); tracker.Begin(); var before = tracker.Status;
        tracker.ProcessBatch(batch); GoldGameDispatcher.Dispatch(batch, world.Apply);
        Assert.AreEqual(before.Phase, tracker.Status.Phase); Assert.AreEqual(before.PlayerSlot, tracker.Status.PlayerSlot);
        Assert.AreEqual(4L, world.Session.Lobby.ReceivedCount); Assert.AreEqual("Hero", world.Session.Lobby.LastProfile!.Text);
        Assert.AreEqual(42u, world.Session.Lobby.NetworkWindowValue); Assert.AreEqual("Player", world.Session.Lobby.LastWindowRequest!.Text);
        Assert.AreEqual("Lobby kick\n\nbye", world.Session.Notices.CurrentDialog!.Text);
        Assert.IsFalse(peer.Connection.IsStopping); Assert.AreEqual(0L, peer.Connection.Diagnostics.SentMessages);
        Assert.AreEqual(0, world.CreatureControls.PendingReplies);
    }

    [TestMethod]
    public void TextOffsetsKeepUtf16CodeUnitsAndIgnoreSuffixes()
    {
        foreach (var (op, offset) in new (ushort, int)[] { (0x27, 4), (0x2f, 4), (0x31, 8) })
        {
            var message = Text(op, offset, "\ud800世界\udfff"); message = message with { Payload = [..message.Payload, 0xa5] };
            var entry = GoldGameEventDecoder.Decode(message);
            var text = entry switch { GoldLobbyProfileEvent p => p.Text, GoldLobbyWindowEvent w => w.Text, GoldSessionNoticeEvent n => n.Text, _ => null };
            Assert.AreEqual("\ud800世界\udfff", text);
            for (var length = 0; length < message.Payload.Length - 1; length++)
                Assert.ThrowsException<InvalidDataException>(() => GoldGameEventDecoder.Decode(message with { Payload = message.Payload[..length] }));
        }
    }

    [TestMethod]
    public void ChannelListChecksCountsButAllowsNativeCompactFinalRecordsAndLongNames()
    {
        var empty = new GoldMessage(0x2e, 0, 0, 0, [0, 0, 0, 0]);
        Assert.AreEqual(0, ((GoldLobbyChannelsEvent)GoldGameEventDecoder.Decode(empty)).Rows.Count);
        foreach (var count in new uint[] { 1, 2, 0x80000000, uint.MaxValue })
        {
            var bad = empty.Payload.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(bad, count);
            Assert.ThrowsException<InvalidDataException>(() => GoldGameEventDecoder.Decode(empty with { Payload = bad }));
        }
        foreach (var name in new[] { Array.Empty<byte>(), new byte[] { 0xe9 }, Enumerable.Repeat((byte)'A', 180).ToArray() })
        {
            var bytes = Channel(name); var entry = (GoldLobbyChannelsEvent)GoldGameEventDecoder.Decode(empty with { Payload = bytes });
            Assert.AreEqual(1, entry.Rows.Count); CollectionAssert.AreEqual(name, entry.Rows[0].NameBytes.ToArray());
            Assert.AreEqual(-1, entry.Rows[0].PlayerCount); Assert.AreEqual(16, entry.Rows[0].PlayerLimit);
            Assert.ThrowsException<InvalidDataException>(() => GoldGameEventDecoder.Decode(empty with { Payload = bytes[..^1] }));
        }
        // The native ANSI scanner can consume bytes beyond the next record's start.
        var crossing = Channel(Enumerable.Repeat((byte)'A', 180).ToArray()); Array.Resize(ref crossing, 197);
        BinaryPrimitives.WriteUInt32LittleEndian(crossing, 2); crossing.AsSpan(180, 16).Fill(0x41); crossing[196] = 0;
        var decoded = (GoldLobbyChannelsEvent)GoldGameEventDecoder.Decode(empty with { Payload = crossing });
        Assert.AreEqual(2, decoded.Rows.Count); Assert.AreEqual(176, decoded.Rows[0].NameBytes.Length);
        Assert.AreEqual(0, decoded.Rows[1].NameBytes.Length);
    }

    [TestMethod]
    public void FiltersAndEncodingCanResolveReceivedRowsWithoutReplayingReceipt()
    {
        var world = new GoldWorldReplica(); var state = world.Session.Lobby;
        world.Apply(GoldGameEventDecoder.Decode(new(0x2e, 0, 0, 0, Channel([0xe9]))));
        Assert.AreEqual(1, state.Channels.Count); Assert.IsNull(state.ChannelRequests);
        state.ConfigureChannels(new(false, 0, false));
        Assert.AreEqual(1, state.ChannelRequests!.Count); Assert.IsNull(state.ChannelRequests[0].Text);
        state.ConfigureChannels(new(false, 0, false), CodePagesEncodingProvider.Instance.GetEncoding(1252));
        Assert.AreEqual("#1 é (-1/16)", state.ChannelRequests![0].Text);
        state.ConfigureChannels(new(true, 0, true)); Assert.AreEqual(0, state.ChannelRequests!.Count);
        Assert.AreEqual(1L, state.ReceivedCount); Assert.AreEqual(1L, state.ChannelClearRequestCount);
        Assert.ThrowsException<NotSupportedException>(() => ((IList<GoldLobbyChannelRow>)state.Channels).Clear());
        world.Clear(); Assert.AreEqual(0, state.Channels.Count); Assert.IsNull(state.ChannelRequests);
        Assert.AreEqual(new GoldLobbyChannelFilter(true, 0, true), state.ChannelFilter);
    }

    [TestMethod]
    public async Task MalformedListRejectsMixedBatchBeforeStateMutation()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        await peer.SendAsync(Text(0x27, 4, "Hero"), new GoldMessage(0x135, 0, 0, 0, []), new GoldMessage(0x2e, 0, 0, 0, [1, 0, 0, 0]));
        var batch = (await peer.ReceiveAsync()).Messages;
        Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(batch, world.Apply));
        Assert.AreEqual(0L, world.Revision); Assert.AreEqual(0L, world.Session.Lobby.ReceivedCount); Assert.IsNull(world.Session.IsPaused);
        var tracker = new GoldAdmissionTracker(); var before = tracker.Status;
        Assert.ThrowsException<InvalidDataException>(() => tracker.ProcessBatch(batch)); Assert.AreSame(before, tracker.Status);
    }

    [TestMethod]
    public void WireFieldsWinAndKickKeepsFixedDialogActionWithLateLocalization()
    {
        var world = new GoldWorldReplica(); var state = world.Session.Lobby;
        world.Apply(new GoldLobbyProfileEvent(Text(0x27, 4, "real"), 999, "forged"));
        Assert.AreEqual("real", state.LastProfile!.Text); Assert.AreEqual(0u, state.LastProfile.Value);
        var revision = world.Revision;
        Assert.ThrowsException<InvalidDataException>(() => world.Apply(new GoldLobbyProfileEvent(new(0x2e, 0, 0, 0, []), 0, "")));
        Assert.AreEqual(revision, world.Revision); Assert.AreEqual(1L, state.ReceivedCount);
        world.Apply(GoldGameEventDecoder.Decode(Text(0x31, 8, new string('T', 180))));
        var notices = world.Session.Notices;
        Assert.IsTrue(notices.CurrentDialog!.NeedsLocalization); Assert.AreEqual(1u, notices.CurrentDialog.DialogAction);
        notices.ConfigureResources(LobbyUpdateChecks.Resources());
        Assert.AreEqual(192, notices.CurrentDialog!.Text!.Length); Assert.AreEqual(1u, notices.CurrentDialog.DialogAction);
        Assert.AreEqual(3u, notices.CurrentDialog.NativeDialogParameter); Assert.AreEqual(1L, notices.OpenRequestCount);
        Assert.IsNull(world.Session.GameFlags); Assert.IsNull(world.Session.IsPaused);
    }

    private static GoldMessage Text(ushort opcode, int offset, string text)
    {
        var bytes = new byte[offset + text.Length * 2 + 2];
        ChannelChatHostRoleChecks.WriteText(bytes, offset, text); return new(opcode, 0, 31, 7, bytes);
    }
    private static byte[] Channel(byte[] name)
    {
        var bytes = new byte[21 + name.Length]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), -1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 16);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 1); name.CopyTo(bytes, 20); return bytes;
    }
}
