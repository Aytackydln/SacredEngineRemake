using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Packet")]
public sealed class SessionTextTransportTests
{
    [TestMethod]
    public async Task TcpChatAndCheatNotificationsPreserveOrderWithoutReplies()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        await peer.SendAsync(Chat(0, 0, "ignored", "Welcome"), Cheats(2), Chat(17, 9, "Hero", "Hi"), Cheats(0));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        Assert.AreEqual(2L, world.Session.Chat.ReceivedCount);
        CollectionAssert.AreEqual(new[] { "Welcome", "Hi" }, world.Session.Chat.Messages.Select(row => row.Text).ToArray());
        Assert.IsTrue(world.Session.Chat.Messages[0].IsServerMessage); Assert.IsTrue(world.Session.Chat.Messages[1].IsPrivate);
        Assert.IsFalse(world.Session.CheatsEnabled); Assert.AreEqual(2L, world.Session.CheatNotificationCount);
        Assert.IsFalse(peer.Connection.IsStopping); Assert.AreEqual(0L, peer.Connection.Diagnostics.SentMessages);
        Assert.AreEqual(0, world.CreatureControls.PendingReplies);
    }

    [TestMethod]
    public async Task TcpMalformedChatRejectsTheWholeMixedBatch()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        var bad = Chat(3, 0, "Hero", "Hi"); bad = bad with { Payload = bad.Payload[..^1] };
        await peer.SendAsync(Cheats(1), Chat(3, 0, "Hero", "valid"), bad);
        var batch = await peer.ReceiveAsync();
        Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(batch.Messages, world.Apply));
        Assert.AreEqual(0, world.Session.Chat.Messages.Count); Assert.IsNull(world.Session.CheatsEnabled); Assert.AreEqual(0L, world.Revision);
        var tracker = new GoldAdmissionTracker(); var before = tracker.Status;
        Assert.ThrowsException<InvalidDataException>(() => tracker.ProcessBatch(batch.Messages)); Assert.AreSame(before, tracker.Status);
    }

    [TestMethod]
    public void RecipientUsesSignedComparisonAndSpeakerIsIndependentOfTransportIdentity()
    {
        foreach (var recipient in new[] { int.MinValue, -1, 0, 1, int.MaxValue })
        foreach (var speaker in new uint[] { 0, 3, uint.MaxValue })
        {
            var entry = (GoldChatEvent)GoldGameEventDecoder.Decode(Chat(speaker, recipient, "Hero", "hello"));
            Assert.AreEqual(recipient > 0, entry.IsPrivate); Assert.AreEqual(speaker == 0, entry.IsServerMessage);
            Assert.AreEqual(speaker == 0 ? null : "Hero", entry.Name);
        }
        var server = Chat(0, 1, "", "notice"); server.Payload.AsSpan(8, 256).Fill(0xff);
        var decoded = (GoldChatEvent)GoldGameEventDecoder.Decode(server);
        Assert.IsNull(decoded.Name); Assert.IsTrue(decoded.IsPrivate); Assert.AreEqual("notice", decoded.Text);
    }

    [TestMethod]
    public void Utf16UnitsAndSuffixAreOwnedAndTerminatorsMustBeAligned()
    {
        var name = "\ud800"; var text = "Grüße 世界 \udfff";
        var message = Chat(3, 0, name, text); message = message with { Payload = [..message.Payload, 0xa5] };
        var original = message.Payload.ToArray(); var entry = (GoldChatEvent)GoldGameEventDecoder.Decode(message);
        Array.Fill(message.Payload, (byte)0xcc);
        Assert.AreEqual(name, entry.Name); Assert.AreEqual(text, entry.Text); CollectionAssert.AreEqual(original, entry.Message.Payload);
        var bytes = new byte[267]; bytes[264] = 65; // NUL bytes at 265/266 do not terminate the 16-bit unit at 264.
        Assert.ThrowsException<InvalidDataException>(() => GoldChatCodec.Decode(new(0x32, 0, 0, 0, bytes)));
        var noNameTerminator = Chat(3, 0, "", "body");
        for (var i = 8; i < 264; i += 2) noNameTerminator.Payload[i] = 78;
        Assert.AreEqual(new string('N', 128) + "body", ((GoldChatEvent)GoldGameEventDecoder.Decode(noNameTerminator)).Name);
    }

    [TestMethod]
    public void WireFieldsWinOverForgedMetadataAndDisconnectClearsTheHistory()
    {
        var world = new GoldWorldReplica();
        world.Apply(new GoldChatEvent(Chat(3, 9, "Hero", "Hi"), 0, 0, null, "forged"));
        world.Apply(new GoldCheatsEnabledEvent(Cheats(2), 0));
        var history = world.Session.Chat.Messages; var message = history.Single();
        Assert.AreEqual("Hero", message.Name); Assert.AreEqual("Hi", message.Text); Assert.IsTrue(message.IsPrivate);
        Assert.IsTrue(world.Session.CheatsEnabled);
        Assert.ThrowsException<NotSupportedException>(() => ((IList<GoldChatEvent>)history).Clear());
        var revision = world.Revision;
        Assert.ThrowsException<InvalidDataException>(() => world.Apply(new GoldChatEvent(Chat(0, 0, "", "") with { Payload = [] }, 0, 0, null, "")));
        Assert.AreEqual(revision, world.Revision);
        world.Clear(); Assert.AreEqual(0, history.Count); Assert.AreEqual(0L, world.Session.Chat.ReceivedCount);
        Assert.IsNull(world.Session.CheatsEnabled); Assert.AreEqual(0L, world.Session.CheatNotificationCount);
    }

    [TestMethod]
    public void BoundedHistoryRetainsNewestReceiptsAndCountsAllMessages()
    {
        var world = new GoldWorldReplica();
        for (var i = 0; i < GoldChatReplica.HistoryLimit + 2; i++) world.Apply(GoldGameEventDecoder.Decode(Chat(3, 0, "Hero", i.ToString())));
        Assert.AreEqual(GoldChatReplica.HistoryLimit, world.Session.Chat.Messages.Count);
        Assert.AreEqual("2", world.Session.Chat.Messages[0].Text);
        Assert.AreEqual((long)GoldChatReplica.HistoryLimit + 2, world.Session.Chat.ReceivedCount);
    }

    [TestMethod]
    public void CheatModeDoesNotChangeGameFlagsPauseOrAdmissionApproval()
    {
        var world = new GoldWorldReplica(); var tracker = new GoldAdmissionTracker(); tracker.Begin();
        GoldMessage[] controls = [new(0x1e, 0, 0, 0, Words(0x40004)), new(0x135, 0, 3, 0, [])];
        GoldGameDispatcher.Dispatch(controls, world.Apply); tracker.ProcessBatch(controls); var status = tracker.Status;
        foreach (var raw in new uint[] { 0, 1, 2, 0x80000000, uint.MaxValue, 0 })
        {
            var message = Cheats(raw); tracker.ProcessBatch([message, Chat(0, 0, "", "notice")]);
            world.Apply(GoldGameEventDecoder.Decode(message)); Assert.AreEqual(raw != 0, world.Session.CheatsEnabled);
            Assert.AreEqual(0x40004u, world.Session.GameFlags); Assert.IsTrue(world.Session.IsPaused); Assert.IsTrue(world.Session.IsLocked);
            Assert.AreEqual(status, tracker.Status);
        }
        Assert.AreEqual(6L, world.Session.CheatNotificationCount);
    }

    [TestMethod]
    public void MalformedCheatWordAndWrongOpcodesCannotMasqueradeAsNotifications()
    {
        for (var size = 0; size < 4; size++)
        {
            var world = new GoldWorldReplica(); var bad = Cheats(1) with { Payload = new byte[size] };
            Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch([Chat(0, 0, "", "notice"), bad], world.Apply));
            Assert.AreEqual(0, world.Session.Chat.Messages.Count); Assert.IsNull(world.Session.CheatsEnabled);
        }
        Assert.ThrowsException<InvalidDataException>(() => GoldChatCodec.Decode(Chat(0, 0, "", "") with { Opcode = 0x1a8 }));
        Assert.ThrowsException<InvalidDataException>(() => GoldCheatsEnabledCodec.Decode(Cheats(1) with { Opcode = 0x32 }));
    }

    private static GoldMessage Cheats(uint value) => new(0x1a8, 0, 0, 0, Words(value));
    private static byte[] Words(uint value) { var bytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); return bytes; }
    private static GoldMessage Chat(uint speaker, int recipient, string name, string text)
    {
        var bytes = new byte[266 + text.Length * 2]; Words(speaker).CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), recipient);
        for (var i = 0; i < name.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8 + i * 2), name[i]);
        for (var i = 0; i < text.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(264 + i * 2), text[i]);
        return new(0x32, 0, 31, 7, bytes);
    }
}
