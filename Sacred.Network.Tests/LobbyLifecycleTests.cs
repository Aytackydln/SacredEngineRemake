using System.Buffers.Binary;
using System.Collections.Frozen;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.GameRes;
using Sacred.Core.Pax.Hero.Objects;
using Sacred.Network.Connection;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class LobbyLifecycleTests
{
    private static GoldWorldReplica World()
    {
        var world = new GoldWorldReplica(); world.Apply(GoldGameEventDecoder.Decode(CreatureCommandPackets.Admission()));
        return world;
    }
    [TestMethod]
    public void RemoteDepartureClearsRosterTeamAndInvitesAfterCapturingName()
    {
        var world = World(); world.Apply(GoldGameEventDecoder.Decode(LobbyLifecyclePackets.Roster(3, "Before")));
        world.Apply(GoldGameEventDecoder.Decode(new(0x138, 0, 3, 0, Words(3))));
        world.Apply(GoldGameEventDecoder.Decode(new(0x138, 0, 4, 0, Words(3))));
        world.Apply(GoldGameEventDecoder.Decode(new(0x139, 0, 3, 0, Words(2))));
        Assert.IsTrue(world.Session.Teams.IsLeader(3)); var control = world.Session.Lobby.Control; var old = control.Roster.Find(3)!;
        world.Apply(Event(0x150, Words(9), 3));
        Assert.AreEqual("Before", control.Notices.Single().PlayerName); Assert.AreEqual("Before", old.Name);
        Assert.AreEqual("", control.Roster.Find(3)!.Name); Assert.IsTrue(control.Roster.Find(3)!.ToPayload().All(v => v == 0));
        Assert.AreEqual(0u, world.Session.Teams.GetTeam(3)); Assert.IsTrue(world.Session.Teams.IsLeader(4));
        Assert.AreEqual(0u, world.Session.Teams.IncomingInvitations & 4);
        world.Apply(GoldGameEventDecoder.Decode(LobbyLifecyclePackets.Roster(3, "After")));
        Assert.AreEqual("Before", control.Notices.Single().PlayerName);
        world.Apply(Event(0x151, [], 3)); Assert.AreEqual("After", control.Notices[^1].PlayerName);
        Assert.AreEqual("After", control.Roster.Find(3)!.Name, "A login notice does not clear the player.");
    }

    [TestMethod]
    public void LocalDeparturesRequireKnownNativeDecisionInputsAndDoNotCloseTransport()
    {
        var world = World(); var control = world.Session.Lobby.Control;
        world.Apply(Event(0x01C, Words(10), 2)); Assert.IsNull(control.LocalDepartures.Single().ClosesBusyDialog);
        Assert.AreEqual(0L, control.Native117RequestCount); Assert.AreEqual(0L, control.ClockResetRequestCount);
        control.ConfigureLocalDeparture(true, true); world.Apply(Event(0x150, Words(0), 2));
        Assert.AreEqual(1L, control.ClockResetRequestCount); Assert.AreEqual(4u, control.UiRequests.Single().Action);
        control.ConfigureLocalDeparture(false, true); world.Apply(Event(0x1C, Words(uint.MaxValue), 2));
        Assert.AreEqual(1L, control.Native117RequestCount); Assert.AreEqual(3L, control.SingletonListClearCount);
        Assert.AreEqual(0L, world.Session.Notices.SessionEndRequestCount);
        world.Clear(); Assert.AreEqual(0, control.LocalDepartures.Count); Assert.AreEqual(0, control.UiRequests.Count);
    }

    [TestMethod]
    public void ResultFollowupsPreserveServiceLatchesAndUnknownWindowDecision()
    {
        var world = World(); var control = world.Session.Lobby.Control;
        control.ConfigureService(false, false);
        world.Apply(Event(0x26, [..Words(0, 0), ..LobbyLifecyclePackets.Text("OK")]));
        Assert.AreEqual(8, control.ChannelQueries.Count); Assert.AreEqual(1L, control.ServiceSettingRequestCount);
        Assert.AreEqual(false, control.NativeFlag3819); Assert.AreEqual(false, control.NativeFlag381A);
        world.Apply(Event(0x26, [..Words(0, 0), 0, 0])); Assert.AreEqual(16, control.ChannelQueries.Count); Assert.AreEqual(1L, control.ServiceSettingRequestCount);
        var count = control.UiRequests.Count; world.Apply(Event(0x24, [..Words(0, 9), 0, 0]));
        Assert.AreEqual(count, control.UiRequests.Count); Assert.AreEqual(1, control.ConditionalResults.Count);
        world.Apply(Event(0x2D, Words(1))); world.Apply(Event(0x24, [..Words(0, 9), 0, 0])); Assert.AreEqual(count, control.UiRequests.Count);
        world.Apply(Event(0x2D, Words(0))); world.Apply(Event(0x24, [..Words(0, 9), 0, 0])); Assert.AreEqual(count + 2, control.UiRequests.Count);
        world.Apply(Event(0x24, [..Words(17, 0), 0, 0])); Assert.AreEqual(1L, control.OwnProfileRequestCount);
        world.Apply(Event(0x24, [..Words(0, uint.MaxValue), ..LobbyLifecyclePackets.Text("\ud800")]));
        Assert.AreEqual("Lobby Error -1: \ud800!", control.UiRequests[^1].Text);
    }

    [TestMethod]
    public void ErrorsRetainOptionalWordAndResolveOriginalNameFormattingLate()
    {
        var world = World(); var control = world.Session.Lobby.Control;
        world.Apply(GoldGameEventDecoder.Decode(LobbyLifecyclePackets.Roster(3, "Player")));
        world.Apply(Event(9, Words(107), 3)); var notice = control.Notices.Single(); Assert.IsTrue(notice.IsChat); Assert.IsNull(control.Resolve(notice));
        control.ConfigureResources(new GameResStore(new Dictionary<uint, string>
        {
            [SacredResourceHash.Compute(GoldServiceErrorKeys.Get(107)!)] = "Error",
            [SacredResourceHash.Compute("UI_SPIELER")] = "Player slot"
        }.ToFrozenDictionary()));
        Assert.AreEqual("Error Player\\r!", control.Resolve(notice));
        world.Apply(Event(9, Words(107, uint.MaxValue), 3)); Assert.AreEqual("Error (-1)", control.Resolve(control.Notices[^1]));
        world.Apply(Event(9, Words(108), 3)); Assert.AreEqual(7u, control.Notices[^1].DialogAction);
        Assert.ThrowsException<InvalidDataException>(() => Event(9, [..Words(1), 0]));
        var wire = new GoldMessage(9, 3, 2, 0, Words(21)); world.Apply(new GoldServiceErrorEvent(wire, 108, 999));
        Assert.IsTrue(control.Notices[^1].IsChat, "Wire fields win over public record metadata.");
    }

    [TestMethod]
    public void SettingsUseLastEquipmentReferenceRatherThanHorsePairingForSectorFallback()
    {
        var world = World(); world.Apply(LobbyLifecyclePackets.Actor(2, flags: 2, sector: 0, context: 3));
        world.Apply(LobbyLifecyclePackets.Actor(3, sector: 7)); var settings = world.Session.Lobby.Control.Settings;
        world.Apply(Event(12, Words(uint.MaxValue, 60, 3, 90, 0x3fc00000, 5, 6, 7)));
        Assert.AreEqual((ushort)7, settings.ContextSector); Assert.IsNull(settings.Receipts.Single().Direct); Assert.IsNull(settings.Fields);
        Assert.AreEqual(0u, world.Objects[2].MountPartnerReference);
        Assert.AreEqual(18, SacredSavedCreatureLayout.SectorContextReferenceIndex);
        Assert.AreEqual(491, SacredSavedCreatureLayout.SectorContextReferenceOffset);
        settings.ConfigureSectorAvailability(_ => true); world.Apply(Event(12, Words(uint.MaxValue, 60, 3, 90, 0x3fc00000, 5, 6, 7)));
        Assert.AreEqual(0xfffffeffu, settings.Fields!.Flags); Assert.AreEqual(7u, settings.NativeWord9E08);
        settings.ConfigureSectorAvailability(_ => false); world.Apply(Event(12, Words(0, uint.MaxValue, 3, 90, 0x7fc00001, 5, 6, 7)));
        CollectionAssert.AreEqual(new int[] { 0, 1, 3, 0 }, settings.Receipts[^1].Operations.Select(v => v.Value).ToArray());
        Assert.AreEqual(0xfffffeffu, settings.Fields.Flags, "Queued operations do not pretend to have changed direct fields.");
        world.Clear(); Assert.IsNull(settings.Fields); Assert.AreEqual(0, settings.Receipts.Count);
    }

    [TestMethod]
    public void PackedProfilesValidateChecksumTrailerDeclaredLengthAndRawRemainders()
    {
        foreach (var size in new[] { 0, 1, 3, 4, 7, 32, 3480 })
        {
            var bytes = Enumerable.Range(0, size).Select(i => (byte)(i * 7)).ToArray();
            CollectionAssert.AreEqual(bytes, GoldPackedProfileBlock.Decode(LobbyLifecyclePackets.Raw(bytes)));
            var compressed = LobbyLifecyclePackets.Compressed(bytes);
            CollectionAssert.AreEqual(bytes, GoldPackedProfileBlock.Decode([..compressed, 0xA5]));
            Assert.ThrowsException<InvalidDataException>(() => GoldPackedProfileBlock.Decode(compressed[..^1]));
            compressed[^1] ^= 1; Assert.ThrowsException<InvalidDataException>(() => GoldPackedProfileBlock.Decode(compressed));
        }
        Assert.ThrowsException<InvalidDataException>(() => GoldPackedProfileBlock.Decode(Words(0xffffffff)));
        var data = LobbyLifecyclePackets.Raw(new byte[10]); BinaryPrimitives.WriteUInt32LittleEndian(data, 0x0800000B);
        Assert.ThrowsException<InvalidDataException>(() => GoldPackedProfileBlock.Decode(data));
        var zlib = LobbyLifecyclePackets.Compressed(new byte[10]); BinaryPrimitives.WriteUInt32LittleEndian(zlib, 9);
        Assert.ThrowsException<InvalidDataException>(() => GoldPackedProfileBlock.Decode(zlib));
    }

    [TestMethod]
    public void PlayerProfilePatchSourceIsCapturedAtReceiptAndNeverReplaysAnUnknownMerge()
    {
        var world = World(); var transfers = world.Session.Lobby.Transfers; world.Apply(GoldGameEventDecoder.Decode(LobbyLifecyclePackets.Roster(2, "Player")));
        var bytes = LobbyLifecyclePackets.PlayerTransfer(); var incoming = Event(0x2B, bytes); Array.Fill(bytes, (byte)0xA5); world.Apply(incoming);
        Assert.IsTrue(transfers.PlayerRequests.Single().NeedsPatchSource); Assert.AreEqual(0, transfers.PlayerProfiles.Count);
        var patch = new byte[] { 1, 2, 3 }; transfers.ConfigurePatchSource(2, patch); patch[0] = 99;
        world.Apply(Event(0x2B, LobbyLifecyclePackets.PlayerTransfer())); var work = transfers.PlayerRequests[^1];
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, work.Profile.Span.Slice(8, 3).ToArray());
        Assert.IsTrue(work.Profile.Span.Slice(11, 125).ToArray().All(b => b == 0)); Assert.IsFalse(work.NeedsPatchSource);
        Assert.IsTrue(transfers.PlayerRequests[0].NeedsPatchSource); Assert.AreEqual(1, transfers.PlayerProfiles.Count);
        CollectionAssert.AreEqual(work.Profile.ToArray(), world.Session.Lobby.Control.Roster.Find(2)!.Profile.ToArray());
        var unsupported = new byte[0xD98]; world.Apply(Event(0x29, [..Words(0, 10, 0, 1), ..LobbyLifecyclePackets.Raw(unsupported)]));
        Assert.AreEqual(1L, transfers.UnsupportedProfileCount); Assert.AreEqual(0, transfers.PublicRequests.Count);
        Assert.IsNull(((GoldLobbyTransferEvent)Event(0x29, Words(0, 3))).StatusWord);
        world.Clear(); Assert.AreEqual(0, transfers.PlayerProfiles.Count);
    }

    [TestMethod]
    public async Task FragmentedTcpAppliesNineNonterminalControlsAndRejectsMalformedTailAtomically()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = World(); world.Apply(LobbyLifecyclePackets.Actor(2));
        world.Session.Lobby.Control.Settings.ConfigureSectorAvailability(_ => true);
        world.Session.Lobby.Transfers.ConfigurePatchSource(2, null);
        await peer.SendAsync(new(9, 3, 2, 1, Words(21)), new(12, 0, 2, 2, Words(0, 1, 2, 3, 4, 5, 6, 7)),
            new(0x1C, 3, 2, 3, Words(9)), new(0x24, 0, 2, 4, [..Words(1, 0), 0, 0]), new(0x26, 0, 2, 5, [..Words(0, 13), 0, 0]),
            new(0x29, 0, 2, 6, LobbyLifecyclePackets.PublicTransfer()), new(0x2B, 0, 2, 7, LobbyLifecyclePackets.PlayerTransfer()),
            new(0x150, 3, 2, 8, Words(0)), new(0x151, 3, 2, 9, []));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        Assert.AreEqual(4, world.Session.Lobby.Control.Notices.Count); Assert.AreEqual(1, world.Session.Lobby.Transfers.PublicRequests.Count);
        Assert.AreEqual(1, world.Session.Lobby.Transfers.PlayerProfiles.Count); Assert.IsFalse(peer.Connection.IsStopping);
        var revision = world.Revision;
        await peer.SendAsync(new(0x151, 3, 2, 10, []), new(0x29, 0, 2, 11, [..Words(0, 10, 0, 1), ..Words(0x08000010)]));
        var batch = await peer.ReceiveAsync(); Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(batch.Messages, world.Apply));
        Assert.AreEqual(revision, world.Revision); Assert.AreEqual(4, world.Session.Lobby.Control.Notices.Count);
    }

    [TestMethod]
    public async Task DisconnectClosesTransportBeforeGameThreadConsumesIt()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync();
        await peer.SendAsync(new GoldMessage(2, 0, 2, 1, Words(uint.MaxValue)));
        await peer.Connection.Completion.WaitAsync(peer.Token); Assert.IsTrue(peer.Connection.IsStopping);
        var batch = await peer.ReceiveAsync(); var world = World(); world.Apply(LobbyLifecyclePackets.Actor(2)); world.Apply(LobbyLifecyclePackets.Actor(3, owner: 3));
        GoldGameDispatcher.Dispatch(batch.Messages, world.Apply);
        Assert.AreEqual(uint.MaxValue, world.Session.Notices.LastReceipt!.Event.ErrorCode);
        Assert.AreEqual(0x10u, world.Objects[3].BaseState.Flags & 0x10); Assert.AreEqual(0u, world.Objects[2].BaseState.Flags & 0x10);
        Assert.AreEqual(false, world.Session.ClientHostRole.IsClientHost); Assert.AreEqual(0L, peer.Connection.Diagnostics.SentMessages);
    }

    [TestMethod]
    public async Task MalformedProfileCompanionPreventsTerminalBatchDelivery()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync();
        await peer.SendAsync(new(2, 0, 2, 1, Words(0)), new(0x24, 0, 2, 2, [..Words(0, 0), 65]));
        await peer.Connection.Completion.WaitAsync(peer.Token); var result = await peer.Connection.Events.ReadAsync(peer.Token);
        Assert.IsInstanceOfType<GoldConnectionClosed>(result); StringAssert.Contains(((GoldConnectionClosed)result).Reason, "Truncated Gold lobby control");
    }
}
