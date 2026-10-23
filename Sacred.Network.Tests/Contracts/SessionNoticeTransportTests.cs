using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.GameRes;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Packet")]
public sealed class SessionNoticeTransportTests
{
    private static readonly ushort[] Opcodes = [0x35, 0x38, 0x3a, 0x3c, 0x3d];

    [TestMethod]
    public async Task TcpReceivesAllFiveNoticesWithoutReplyOrDisconnect()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        var state = world.Session.Notices; state.ConfigureResources(SessionNoticeChecks.Resources());
        await peer.SendAsync(Opcodes.Select(op => SessionNoticeChecks.Message(op, "Hi")).ToArray());
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        CollectionAssert.AreEqual(Opcodes, state.Recent.Select(row => row.Event.Message.Opcode).ToArray());
        Assert.AreEqual(5L, state.ReceivedCount); Assert.AreEqual(4L, state.CloseRequestCount);
        Assert.AreEqual(3L, state.OpenRequestCount); Assert.AreEqual(2L, state.ChatRequestCount);
        Assert.IsNull(state.CurrentDialog); Assert.AreEqual("Lobby: Hi", state.CustomDialogText);
        Assert.IsFalse(peer.Connection.IsStopping); Assert.AreEqual(0L, peer.Connection.Diagnostics.SentMessages);
        Assert.AreEqual(0, world.CreatureControls.PendingReplies);
    }

    [TestMethod]
    public void EachLayoutHonorsItsTextOffsetSkippedBytesAndAlignedTerminator()
    {
        foreach (var op in Opcodes)
        {
            var message = SessionNoticeChecks.Message(op); var entry = (GoldSessionNoticeEvent)GoldGameEventDecoder.Decode(message);
            Assert.AreEqual(op == 0x3a ? null : string.Empty, entry.Text);
            for (var size = 0; size < message.Payload.Length; size++)
                Assert.ThrowsException<InvalidDataException>(() => GoldGameEventDecoder.Decode(message with { Payload = message.Payload[..size] }));
            if (op == 0x3a) continue;
            var unicode = SessionNoticeChecks.Message(op, "\ud800世界\udfff", 255);
            unicode = unicode with { Payload = [..unicode.Payload, 0xa5] }; var expected = unicode.Payload.ToArray();
            var decoded = (GoldSessionNoticeEvent)GoldGameEventDecoder.Decode(unicode); Array.Fill(unicode.Payload, (byte)0xcc);
            Assert.AreEqual("\ud800世界\udfff", decoded.Text); CollectionAssert.AreEqual(expected, decoded.Message.Payload);
            if (op == 0x3d) Assert.AreEqual((byte)255, decoded.ModalFlag);
            var bad = SessionNoticeChecks.Message(op, "A"); bad = bad with { Payload = bad.Payload[..^1] };
            Assert.ThrowsException<InvalidDataException>(() => GoldGameEventDecoder.Decode(bad));
        }
        foreach (var size in new[] { 0, 1, 3, 17, 257 })
            Assert.IsInstanceOfType<GoldSessionNoticeEvent>(GoldGameEventDecoder.Decode(new(0x3a, 0, 0, 0, new byte[size])));
    }

    [TestMethod]
    public void LayoutThresholdUsesResolvedUtf16LengthAndAbortKeepsItsFixedAction()
    {
        var world = new GoldWorldReplica(); var state = world.Session.Notices; state.ConfigureResources(SessionNoticeChecks.Resources());
        foreach (var length in new[] { 149, 150, 151 })
        {
            world.Apply(GoldGameEventDecoder.Decode(SessionNoticeChecks.Message(0x38, new string('T', length - 9))));
            Assert.AreEqual(length, state.LastReceipt!.Text!.Length); Assert.AreEqual(length > 150 ? 7u : 1u, state.CurrentDialog!.DialogAction);
            world.Apply(GoldGameEventDecoder.Decode(SessionNoticeChecks.Message(0x3c, new string('T', length))));
            Assert.AreEqual(length > 150 ? 7u : 1u, state.CurrentDialog!.DialogAction);
            world.Apply(GoldGameEventDecoder.Decode(SessionNoticeChecks.Message(0x3d, new string('T', length - 7), 2)));
            Assert.AreEqual(length, state.LastReceipt!.Text!.Length); Assert.AreEqual(length > 150 ? 7u : 1u, state.CurrentDialog!.DialogAction);
        }
        state.ConfigureResources(SessionNoticeChecks.Resources(failed: new string('F', 160), aborted: new string('B', 180)));
        world.Apply(GoldGameEventDecoder.Decode(SessionNoticeChecks.Message(0x38)));
        Assert.AreEqual(7u, state.CurrentDialog!.DialogAction);
        world.Apply(GoldGameEventDecoder.Decode(SessionNoticeChecks.Message(0x3a)));
        Assert.AreEqual(180, state.CurrentDialog!.Text!.Length); Assert.AreEqual(1u, state.CurrentDialog.DialogAction);
    }

    [TestMethod]
    public async Task MalformedFinalNoticeRollsBackAnEntireMixedBatch()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        var bad = SessionNoticeChecks.Message(0x3d, "Hi", 1); bad = bad with { Payload = bad.Payload[..^1] };
        await peer.SendAsync(SessionNoticeChecks.Message(0x3a), new GoldMessage(0x135, 0, 0, 0, []), bad);
        var received = await peer.ReceiveAsync();
        Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(received.Messages, world.Apply));
        Assert.AreEqual(0L, world.Revision); Assert.AreEqual(0L, world.Session.Notices.ReceivedCount); Assert.IsNull(world.Session.IsPaused);
        var tracker = new GoldAdmissionTracker(); var before = tracker.Status;
        Assert.ThrowsException<InvalidDataException>(() => tracker.ProcessBatch(received.Messages)); Assert.AreSame(before, tracker.Status);
    }

    [TestMethod]
    public void LateResourcesResolveMissingTextWithoutReplayingRequests()
    {
        var world = new GoldWorldReplica(); var state = world.Session.Notices;
        world.Apply(GoldGameEventDecoder.Decode(SessionNoticeChecks.Message(0x35, "Hero")));
        world.Apply(GoldGameEventDecoder.Decode(SessionNoticeChecks.Message(0x38, new string('T', 141))));
        Assert.IsTrue(state.LastReceipt!.NeedsLocalization); Assert.IsNull(state.CurrentDialog!.DialogAction);
        Assert.IsNull(state.CustomDialogText); Assert.AreEqual(1L, state.CloseRequestCount);
        state.ConfigureResources(SessionNoticeChecks.Resources());
        Assert.AreEqual("Added: Hero", state.Recent[0].Text); Assert.AreEqual(150, state.LastReceipt!.Text!.Length);
        Assert.AreEqual(1u, state.CurrentDialog!.DialogAction); Assert.AreEqual(state.LastReceipt.Text, state.CustomDialogText);
        Assert.AreEqual(2L, state.ReceivedCount); Assert.AreEqual(1L, state.CloseRequestCount); Assert.AreEqual(1L, state.OpenRequestCount);
        state.ConfigureResources(GameResStore.Empty); Assert.IsTrue(state.LastReceipt.NeedsLocalization);
    }

    [TestMethod]
    public void ChatNoticesAndAbortPreserveTheSeparateMutableTextSlotRules()
    {
        var world = new GoldWorldReplica(); var state = world.Session.Notices; state.ConfigureResources(SessionNoticeChecks.Resources());
        world.Apply(GoldGameEventDecoder.Decode(SessionNoticeChecks.Message(0x38, "problem"))); var dialog = state.CurrentDialog;
        world.Apply(GoldGameEventDecoder.Decode(SessionNoticeChecks.Message(0x35, "Hero")));
        Assert.AreSame(dialog, state.CurrentDialog); Assert.AreEqual("Failure\n\nproblem", state.CustomDialogText);
        Assert.AreEqual(17u, state.LastReceipt!.NativeChatSelector);
        world.Apply(GoldGameEventDecoder.Decode(SessionNoticeChecks.Message(0x3a)));
        Assert.AreEqual("Aborted", state.CurrentDialog!.Text); Assert.AreEqual("Failure\n\nproblem", state.CustomDialogText);
        world.Apply(GoldGameEventDecoder.Decode(SessionNoticeChecks.Message(0x3d, "notice", 0)));
        Assert.IsNull(state.CurrentDialog); Assert.AreEqual("Lobby: notice", state.CustomDialogText); Assert.AreEqual(0u, state.LastReceipt!.NativeChatSelector);
        world.Apply(GoldGameEventDecoder.Decode(SessionNoticeChecks.Message(0x3d, "modal", 255)));
        Assert.IsNotNull(state.CurrentDialog); Assert.IsNull(state.LastReceipt!.NativeChatSelector);
    }

    [TestMethod]
    public void WireMetadataWinsHistoryIsReadOnlyAndDisconnectClearsReceivedState()
    {
        var world = new GoldWorldReplica(); var state = world.Session.Notices; state.ConfigureResources(SessionNoticeChecks.Resources());
        var message = SessionNoticeChecks.Message(0x3d, "actual", 0);
        world.Apply(new GoldSessionNoticeEvent(message, "forged", 1));
        Assert.AreEqual("Lobby: actual", state.LastReceipt!.Text); Assert.IsNull(state.CurrentDialog);
        var revision = world.Revision;
        Assert.ThrowsException<InvalidDataException>(() => world.Apply(new GoldSessionNoticeEvent(new(0x135, 0, 0, 0, []), null, null)));
        Assert.AreEqual(revision, world.Revision);
        for (var i = 0; i < GoldChatReplica.HistoryLimit; i++) world.Apply(GoldGameEventDecoder.Decode(SessionNoticeChecks.Message(0x3c, i.ToString())));
        Assert.AreEqual(GoldChatReplica.HistoryLimit, state.Recent.Count); Assert.AreEqual("0", state.Recent[0].Text);
        Assert.ThrowsException<NotSupportedException>(() => ((IList<GoldSessionNoticeView>)state.Recent).Clear());
        world.Clear(); Assert.AreEqual(0, state.Recent.Count); Assert.IsNull(state.CurrentDialog); Assert.IsNull(state.CustomDialogText);
        Assert.IsNull(state.LastReceipt); Assert.AreEqual(0L, state.ReceivedCount);
        world.Apply(GoldGameEventDecoder.Decode(SessionNoticeChecks.Message(0x3a))); Assert.AreEqual("Aborted", state.LastReceipt!.Text);
    }

    [TestMethod]
    public void NoticesDoNotGrantAdmissionOrModifyGameplayControls()
    {
        var world = new GoldWorldReplica(); var tracker = new GoldAdmissionTracker(); tracker.Begin(); var before = tracker.Status;
        GoldGameDispatcher.Dispatch([new(0x1e, 0, 0, 0, [4, 0, 0, 0]), new(0x135, 0, 3, 0, [])], world.Apply);
        var messages = Opcodes.Select(op => SessionNoticeChecks.Message(op, "Hi", 1)).ToArray();
        tracker.ProcessBatch(messages); GoldGameDispatcher.Dispatch(messages, world.Apply);
        Assert.AreEqual(before, tracker.Status); Assert.IsTrue(world.Session.IsPaused); Assert.IsTrue(world.Session.IsLocked);
        Assert.AreEqual(4u, world.Session.GameFlags); Assert.AreEqual(0, world.Objects.Count); Assert.AreEqual(0, world.CreatureControls.PendingReplies);
        Assert.ThrowsException<InvalidDataException>(() => GoldSessionNoticeCodec.Decode(new(0x32, 0, 0, 0, new byte[270])));
    }
}
