using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Admission;
using Sacred.Network.Connection;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Packet")]
public sealed class SessionBrowserTransportTests
{
    [TestMethod]
    public async Task BrowserAndControlsApplyInTcpReceiptOrder()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        GoldMessage[] messages = [new(0x2d, 0, 0, 0, BitConverter.GetBytes(7u)), Server(0x2a, 1, 4, "First"),
            Server(0x2a, 2, 4, "Replacement"), Server(0x2a, 3, 5, "Ignored", 8),
            Server(0x47, 4, 6, "世界\ud800"), Refresh(42, "\ud800Hero\udfff"), new(0x20, 0, 0, 0, [0xa5]), new(0x1f, 0, 0, 0, [])];
        await peer.SendAsync(messages); var batch = (await peer.ReceiveAsync()).Messages;
        GoldGameDispatcher.Dispatch(batch, world.Apply);
        var browser = world.Session.Lobby.ServerBrowser;
        Assert.AreEqual(1, browser.Servers.Count); Assert.AreEqual("Replacement", browser.Servers[0].Name);
        Assert.AreEqual(2u, browser.Servers[0].Record.Words[0]); Assert.AreEqual("世界\ud800", browser.Current!.Name);
        Assert.AreEqual("\ud800Hero\udfff", browser.LastPlayerRefresh!.Text); Assert.AreEqual(42u, browser.LastProfileRequestValue);
        Assert.AreEqual(false, world.Session.NativeReceiveControlEnabled); Assert.AreEqual(2L, world.Session.ReceiveControlNotificationCount);
        Assert.IsNull(world.Session.IsPaused); Assert.IsNull(world.Session.GameFlags);
        Assert.IsFalse(peer.Connection.IsStopping); Assert.AreEqual(0L, peer.Connection.Diagnostics.SentMessages);
    }

    [DataTestMethod]
    [DataRow(0x008), DataRow(0x037), DataRow(0x039)]
    public async Task SessionEndClosesTransportWithoutWaitingForGameConsumer(int opcode)
    {
        await using var peer = await GoldLoopbackPeer.StartAsync();
        await peer.SendAsync(new GoldMessage(0x20, 0, 0, 0, []), new GoldMessage((ushort)opcode, 0, 31, 7, [0xa5]));
        await peer.Connection.Completion.WaitAsync(peer.Token);
        var batch = await peer.ReceiveAsync(); var world = new GoldWorldReplica();
        GoldGameDispatcher.Dispatch(batch.Messages, world.Apply);
        Assert.AreEqual(2, batch.Messages.Count); Assert.IsTrue(peer.Connection.IsStopping);
        StringAssert.Contains(peer.Connection.CloseReason!, $"opcode 0x{opcode:X3}");
        Assert.AreEqual(1L, world.Session.Notices.SessionEndRequestCount);
        Assert.AreEqual(opcode == 8 ? 0L : 1L, world.Session.Notices.ConnectionLostChatRequestCount);
        Assert.AreEqual(1u, world.Session.Notices.CurrentDialog!.DialogAction);
        Assert.AreEqual(3u, world.Session.Notices.CurrentDialog.NativeDialogParameter);
        Assert.AreEqual(0L, peer.Connection.Diagnostics.SentMessages);
    }

    [TestMethod]
    public async Task ConnectionClosedNoticeRetainsErrorWithoutInventingSessionEnd()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        await peer.SendAsync(new GoldMessage(1, 0, 0, 0, BitConverter.GetBytes(uint.MaxValue)));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        Assert.AreEqual(uint.MaxValue, world.Session.Notices.LastReceipt!.Event.ErrorCode);
        Assert.AreEqual(0L, world.Session.Notices.SessionEndRequestCount); Assert.IsFalse(peer.Connection.IsStopping);
    }

    [TestMethod]
    public async Task MalformedCompanionRejectsTerminalBatchBeforeDelivery()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync();
        await peer.SendAsync(new GoldMessage(8, 0, 0, 0, []), new GoldMessage(0x2a, 0, 0, 0, new byte[199]));
        await peer.Connection.Completion.WaitAsync(peer.Token);
        var entry = await peer.Connection.Events.ReadAsync(peer.Token);
        Assert.IsInstanceOfType<GoldConnectionClosed>(entry);
        StringAssert.Contains(((GoldConnectionClosed)entry).Reason, "Truncated Gold lobby server record");
        Assert.AreEqual(0L, peer.Connection.Diagnostics.SentMessages);
    }

    [TestMethod]
    public void MalformedMixedBatchLeavesAllReceiveAndAdmissionStateUnchanged()
    {
        var world = new GoldWorldReplica(); var admission = new GoldAdmissionTracker(); var before = admission.Status;
        GoldMessage[] messages = [new(1, 0, 0, 0, new byte[4]), new(0x20, 0, 0, 0, []), Server(0x2a, 1, 2, "Valid"),
            new(0x2c, 0, 0, 0, Enumerable.Repeat((byte)0x41, 33).ToArray())];
        Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(messages, world.Apply));
        Assert.ThrowsException<InvalidDataException>(() => admission.ProcessBatch(messages));
        Assert.AreEqual(0L, world.Revision); Assert.AreSame(before, admission.Status);
        Assert.AreEqual(0L, world.Session.Notices.ReceivedCount); Assert.IsNull(world.Session.NativeReceiveControlEnabled);
        Assert.AreEqual(0, world.Session.Lobby.ServerBrowser.Servers.Count);
    }

    [TestMethod]
    public void RefreshGateIsAppliedAtReceiptAndDoesNotReplaySuppressedRequests()
    {
        var world = new GoldWorldReplica(); var browser = world.Session.Lobby.ServerBrowser;
        browser.SuppressPlayerRefresh = true; world.Apply(GoldGameEventDecoder.Decode(Refresh(42, "Blocked")));
        browser.SuppressPlayerRefresh = false;
        Assert.IsNull(browser.LastPlayerRefresh); Assert.AreEqual(0L, browser.RefreshRequestCount);
        world.Apply(GoldGameEventDecoder.Decode(Refresh(uint.MaxValue, "Accepted")));
        Assert.AreEqual(1L, browser.RefreshRequestCount); Assert.AreEqual("Accepted", browser.LastPlayerRefresh!.Text);
        Assert.AreEqual(uint.MaxValue, browser.LastProfileRequestValue);
        world.Clear(); Assert.AreEqual(0L, browser.RefreshRequestCount); Assert.IsNull(browser.LastProfileRequestValue);
    }

    [TestMethod]
    public void LateEncodingResolvesAnsiRowsAndKeepsUnicodeCurrentRecord()
    {
        var world = new GoldWorldReplica(); var browser = world.Session.Lobby.ServerBrowser;
        world.Apply(GoldGameEventDecoder.Decode(new(0x2d, 0, 0, 0, BitConverter.GetBytes(7u))));
        var ansi = Server(0x2a, 1, 9, "X"); ansi.Payload[32] = 0xe9;
        world.Apply(GoldGameEventDecoder.Decode(ansi)); world.Apply(GoldGameEventDecoder.Decode(Server(0x47, 2, 9, "世界\udfff")));
        Assert.IsNull(browser.Servers[0].Name); Assert.AreEqual("世界\udfff", browser.Current!.Name);
        var revision = browser.Revision;
        browser.ConfigureNameEncoding(CodePagesEncodingProvider.Instance.GetEncoding(1252)!);
        Assert.AreEqual("é", browser.Servers[0].Name); Assert.AreEqual("世界\udfff", browser.Current!.Name);
        Assert.AreEqual(revision, browser.Revision);
        var remove = Server(0x2c, 1, 123, "X"); remove.Payload[32] = 0xe9;
        world.Apply(GoldGameEventDecoder.Decode(remove)); Assert.AreEqual(0, browser.Servers.Count);
    }

    [DataTestMethod]
    [DataRow(0x02a), DataRow(0x02c), DataRow(0x047)]
    public void ServerNamesRequireTerminatorsButCanExtendPastNominalRecord(int opcode)
    {
        var unterminated = new GoldMessage((ushort)opcode, 0, 0, 0, Enumerable.Repeat((byte)0x41, 200).ToArray());
        Assert.ThrowsException<InvalidDataException>(() => GoldGameEventDecoder.Decode(unterminated));
        byte[] body = opcode == 0x47 ? [..Enumerable.Repeat((byte)0x41, 240), 0, 0, 0xa5] : [..Enumerable.Repeat((byte)0x41, 240), 0, 0xa5];
        var entry = (GoldLobbyServerEvent)GoldGameEventDecoder.Decode(unterminated with { Payload = body });
        Assert.AreEqual(208, entry.Server.NameBytes.Length);
        Assert.AreEqual(opcode == 0x47 ? 104 : (int?)null, entry.Server.UnicodeName?.Length);
    }

    [TestMethod]
    public void ForgedEventFieldsCannotBypassWireValidationOrOverwriteBrowserState()
    {
        var world = new GoldWorldReplica(); var browser = world.Session.Lobby.ServerBrowser;
        world.Apply(GoldGameEventDecoder.Decode(new(0x2d, 0, 0, 0, BitConverter.GetBytes(7u))));
        var source = Server(0x2a, 1, 9, "Wire");
        world.Apply(new GoldLobbyServerEvent(source, new(new uint[8], new byte[] { 70 }, 999, 999)));
        Assert.AreEqual("Wire", browser.Servers[0].Name); Assert.AreEqual(9u, browser.Servers[0].Record.EntryKey);
        var revision = world.Revision;
        Assert.ThrowsException<InvalidDataException>(() => world.Apply(new GoldLobbyServerEvent(source with { Payload = [] }, browser.Servers[0].Record)));
        Assert.AreEqual(revision, world.Revision); Assert.AreEqual(1, browser.Servers.Count);
        Assert.ThrowsException<NotSupportedException>(() => ((IList<uint>)browser.Servers[0].Record.Words)[0] = 99);
    }

    private static GoldMessage Refresh(uint value, string text)
    {
        var bytes = new byte[6 + text.Length * 2]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        ChannelChatHostRoleChecks.WriteText(bytes, 4, text); return new(0x30, 0, 31, 7, bytes);
    }
    private static GoldMessage Server(ushort opcode, uint first, uint key, string name, uint window = 7)
    {
        var bytes = new byte[200]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, first);
        if (opcode == 0x47) ChannelChatHostRoleChecks.WriteText(bytes, 32, name);
        else Encoding.ASCII.GetBytes(name).CopyTo(bytes, 32);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(192), key);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(196), window);
        return new(opcode, 0, 31, 7, bytes);
    }
}
