using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Gameplay;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests.Contracts;

/// <summary>Add one independent valid wire example here to get the shared packet checks.</summary>
internal static class PacketCatalog
{
    public static IReadOnlyList<PacketCase> All { get; } = Create();
    public static PacketCase Get(string id) => All.Single(row => row.Id == id);
    public static IEnumerable<object[]> Rows()
    {
        var filter = Environment.GetEnvironmentVariable("SACRED_TEST_PACKET");
        var cases = filter is null ? All : All.Where(row => row.Opcode == Convert.ToUInt16(filter, 16)).ToArray();
        if (cases.Count == 0) throw new InvalidOperationException($"No packet cases registered for 0x{filter}.");
        return cases.Select(row => new object[] { row.Id });
    }
    private static IReadOnlyList<PacketCase> Create()
    {
        var rows = new List<PacketCase>();
        void Add<T>(string name, ushort opcode, Func<byte[]> bytes, PayloadRule rule = PayloadRule.Exact,
            Action<GoldGameEvent>? verify = null, byte sender = 0, bool owns = false, int[]? shorter = null) where T : GoldGameEvent =>
            rows.Add(new(name, opcode, typeof(T), bytes, rule, verify, sender, owns, shorter));
        foreach (var opcode in new ushort[] { 0x001, 0x008, 0x037, 0x039 })
            Add<GoldSessionNoticeEvent>("connection lifecycle", opcode, () => opcode == 1 ? Words(uint.MaxValue) : [],
                PayloadRule.RetainedSuffix, e =>
                {
                    var notice = (GoldSessionNoticeEvent)e;
                    Assert.AreEqual(opcode != 1, notice.EndsSession);
                    Assert.AreEqual(opcode is 0x037 or 0x039, notice.ReportsConnectionLoss);
                    Assert.AreEqual(opcode == 1 ? uint.MaxValue : (uint?)null, notice.ErrorCode);
                }, owns: true);
        foreach (var opcode in new ushort[] { 0x01f, 0x020 })
            Add<GoldSessionReceiveControlEvent>("native receive control", opcode, () => [], PayloadRule.RetainedSuffix,
                e => Assert.AreEqual(opcode == 0x020, ((GoldSessionReceiveControlEvent)e).Enabled), owns: true);
        foreach (var opcode in new ushort[] { 0x02a, 0x02c, 0x047 })
            Add<GoldLobbyServerEvent>("lobby server record", opcode,
                () => opcode == 0x047 ? [..Words(42, 2, 3, 4, 5, 6, 7, 8), 72, 0, 105, 0, 0, 0, ..new byte[154], ..Words(123, 7)] :
                    opcode == 0x02c ? [..Words(42, 2, 3, 4, 5, 6, 7, 8), 72, 105, 0] :
                    [..Words(42, 2, 3, 4, 5, 6, 7, 8), 72, 105, 0, ..new byte[157], ..Words(123, 7)],
                PayloadRule.RetainedSuffix, e =>
                {
                    var server = ((GoldLobbyServerEvent)e).Server;
                    Assert.AreEqual(42u, server.Words[0]);
                    CollectionAssert.AreEqual(opcode == 0x047 ? new byte[] { 72, 0, 105, 0 } : new byte[] { 72, 105 }, server.NameBytes.ToArray());
                    Assert.AreEqual(opcode == 0x02c ? 0u : 123u, server.EntryKey);
                }, owns: true);
        Add<GoldLobbyPlayerRefreshEvent>("lobby player refresh", 0x030, () => [..Words(42), 72, 0, 105, 0, 0, 0],
            PayloadRule.RetainedSuffix, e => { var refresh = (GoldLobbyPlayerRefreshEvent)e;
                Assert.AreEqual(42u, refresh.Value); Assert.AreEqual("Hi", refresh.Text); Assert.AreEqual(2u, refresh.NativeMode); }, owns: true);
        Add<GoldLobbyProfileEvent>("lobby profile", 0x027, () => [42, 0, 0, 0, 72, 0, 105, 0, 0, 0], PayloadRule.RetainedSuffix,
            e => { Assert.AreEqual(42u, ((GoldLobbyProfileEvent)e).Value); Assert.AreEqual("Hi", ((GoldLobbyProfileEvent)e).Text); }, owns: true);
        Add<GoldLobbyWindowEvent>("network window request", 0x02d, () => [42, 0, 0, 0], PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(42u, ((GoldLobbyWindowEvent)e).Value), owns: true);
        Add<GoldLobbyChannelsEvent>("lobby channel list", 0x02e, () => [0, 0, 0, 0], PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(0, ((GoldLobbyChannelsEvent)e).Rows.Count), owns: true);
        Add<GoldLobbyWindowEvent>("lobby player change", 0x02f, () => [42, 0, 0, 0, 72, 0, 105, 0, 0, 0], PayloadRule.RetainedSuffix,
            e => { Assert.AreEqual(42u, ((GoldLobbyWindowEvent)e).Value); Assert.AreEqual("Hi", ((GoldLobbyWindowEvent)e).Text); }, owns: true);
        Add<GoldSessionNoticeEvent>("lobby kick notice", 0x031, () => [0xa5, 0xa5, 0xa5, 0xa5, 0xa5, 0xa5, 0xa5, 0xa5, 72, 0, 105, 0, 0, 0], PayloadRule.RetainedSuffix,
            e => { Assert.AreEqual("Hi", ((GoldSessionNoticeEvent)e).Text); Assert.IsTrue(((GoldSessionNoticeEvent)e).UpdatesCustomText); }, owns: true);
        Add<GoldFirstContactEvent>("first contact", 0x10, () => new byte[950]);
        foreach (var opcode in new ushort[] { 0x035, 0x038, 0x03a, 0x03c, 0x03d })
            Add<GoldSessionNoticeEvent>("session/lobby notice", opcode, () =>
            {
                if (opcode == 0x03a) return [];
                var offset = opcode == 0x035 ? 12 : opcode == 0x03c ? 2 : 6;
                var bytes = new byte[offset + 6]; bytes[offset] = 72; bytes[offset + 2] = 105;
                if (opcode == 0x03d) bytes[4] = 2; return bytes;
            }, PayloadRule.RetainedSuffix, e => Assert.AreEqual(opcode == 0x03a ? null : "Hi", ((GoldSessionNoticeEvent)e).Text), owns: true);
        foreach (var opcode in new ushort[] { 0x003, 0x004, 0x005 })
            Add<GoldChannelChatEvent>("channel chat", opcode, () => [0xff, 0xff, 0xff, 0xff, 72, 0, 105, 0, 0, 0], PayloadRule.RetainedSuffix,
                e => Assert.AreEqual("Hi", ((GoldChannelChatEvent)e).Text), owns: true);
        foreach (var opcode in new ushort[] { 0x00d, 0x00e })
            Add<GoldClientHostRoleEvent>("client host role", opcode, () => [], PayloadRule.RetainedSuffix,
                e => Assert.AreEqual(opcode == 0x00d, ((GoldClientHostRoleEvent)e).IsClientHost), owns: true);
        Add<GoldChatEvent>("received chat", 0x032, () =>
        {
            var bytes = new byte[270]; Words(3, 9).CopyTo(bytes, 0);
            new byte[] { 72, 0, 101, 0, 114, 0, 111, 0 }.CopyTo(bytes, 8);
            new byte[] { 72, 0, 105, 0 }.CopyTo(bytes, 264); return bytes;
        }, PayloadRule.RetainedSuffix, e =>
        {
            var chat = (GoldChatEvent)e; Assert.AreEqual("Hero", chat.Name); Assert.AreEqual("Hi", chat.Text);
            Assert.IsTrue(chat.IsPrivate); Assert.IsFalse(chat.IsServerMessage);
        }, owns: true);
        Add<GoldCheatsEnabledEvent>("server cheat mode", 0x1a8, () => Words(uint.MaxValue), PayloadRule.RetainedSuffix,
            e => { Assert.AreEqual(uint.MaxValue, ((GoldCheatsEnabledEvent)e).RawValue); Assert.IsTrue(((GoldCheatsEnabledEvent)e).Enabled); }, owns: true);
        Add<GoldScriptGroupEvent>("script group membership", 0x18f, () => Words(17, 0, 0, 42, 500), PayloadRule.RetainedSuffix,
            e => { var group = (GoldScriptGroupEvent)e; Assert.AreEqual(42u, group.Group); Assert.AreEqual(500u, group.Member); }, owns: true);
        foreach (var opcode in new ushort[] { 0x18e, 0x190, 0x191, 0x192, 0x193, 0x194, 0x195 })
            Add<GoldInactiveReceiveHookEvent>("inactive native receive hook", opcode, () => [], PayloadRule.RetainedSuffix, owns: true);
        Add<GoldObjectCallbackCleanupEvent>("object callback death cleanup", 0x1c1, () => Words(500), PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(500u, ((GoldObjectCallbackCleanupEvent)e).ObjectReference), owns: true);
        foreach (var opcode in new ushort[] { 0x134, 0x135 })
            Add<GoldSessionPauseEvent>("session pause control", opcode, () => [], PayloadRule.RetainedSuffix,
                e => Assert.AreEqual(opcode == 0x135, ((GoldSessionPauseEvent)e).IsPaused), owns: true);
        Add<GoldTeamJoinEvent>("team join", 0x138, () => Words(0x80000003), PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(0x80000003u, ((GoldTeamJoinEvent)e).Team), owns: true);
        Add<GoldTeamInviteEvent>("team invitation", 0x139, () => Words(3), PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(3u, ((GoldTeamInviteEvent)e).Player), owns: true);
        Add<GoldTeamSnapshotEvent>("team snapshot", 0x13a,
            () => Words([..Enumerable.Range(1, 16).Select(i => (uint)i), 0x12345678, 0x9abcdef0]), PayloadRule.RetainedSuffix,
            e => { var snapshot = (GoldTeamSnapshotEvent)e; Assert.AreEqual(16u, snapshot.Slots[15]);
                Assert.AreEqual(0x12345678u, snapshot.OutgoingInvitations); Assert.AreEqual(0x9abcdef0u, snapshot.IncomingInvitations); }, owns: true);
        Add<GoldTeamLeaveEvent>("team leave", 0x13b, () => Words(3, 2), PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(2u, ((GoldTeamLeaveEvent)e).Player), owns: true);
        Add<GoldSessionPlayerLimitEvent>("session player capacity", 0x023, () => Words(16), PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(16u, ((GoldSessionPlayerLimitEvent)e).AnnouncedMaxPlayers), owns: true);
        Add<GoldPlayerDataEvent>("player data", 0x1b, () => new byte[834]);
        Add<GoldPlayerRosterEvent>("player roster", 0x1b, () => new byte[834*17], shorter: [834]);
        Add<GoldPasswordResultEvent>("password accepted", 0x14, () => [], verify: e => Assert.IsTrue(((GoldPasswordResultEvent)e).Accepted));
        Add<GoldPasswordResultEvent>("password rejected", 0x13, () => [], verify: e => Assert.IsFalse(((GoldPasswordResultEvent)e).Accepted));
        Add<GoldTimeProbeEvent>("time probe", 0x43, () => Words(1, 2, 3, 4), verify: e => Assert.AreEqual(4u, ((GoldTimeProbeEvent)e).Word3));
        Add<GoldReferenceBatchEvent>("object reference batch", 0x153, () => Words(500, 501), PayloadRule.Structured, shorter: [0, 4]);
        Add<GoldReferenceRequestEvent>("object reference request", 0x153, () => Words(2), sender: 2);
        Add<GoldReferenceRangeEvent>("trigger reference range", 0x154, () => Words(10, 4), verify: e => Assert.AreEqual(14u, ((GoldReferenceRangeEvent)e).EndExclusive));
        Add<GoldAdmissionEvent>("admission", 0x11, () => [4, 2, 0x78, 0x56, 0x34, 0x12], verify: e => Assert.AreEqual(0x12345678u, ((GoldAdmissionEvent)e).HostToken));
        Add<GoldRejectionEvent>("admission rejected", 0x3f, () => Words(1, 4));
        foreach (var op in new ushort[] { 0x161, 0x163 })
            Add<GoldMovementEvent>("movement", op, () => Words(500, 2, 300, 400, 0, 2581), PayloadRule.Structured,
                e => Assert.AreEqual(300, ((GoldMovementEvent)e).Position.X));
        Add<GoldMovementEndEvent>("movement end", 0x162, () => Words(500, 300, 400, 0, 2581, 90), PayloadRule.RetainedSuffix);
        Add<GoldIdleEvent>("idle", 0x164, () => Words(500), PayloadRule.RetainedSuffix);
        Add<GoldDoorEvent>("door open", 0x112, () => Words(500), PayloadRule.RetainedSuffix);
        Add<GoldDoorEvent>("door close", 0x111, () => Words(500), PayloadRule.RetainedSuffix);
        foreach (var op in new ushort[] { 0x169, 0x16b, 0x16f, 0x16c, 0x16e })
            Add<GoldObjectEvent>("base object", op, () => ObjectPayload(op), PayloadRule.Structured,
                e => Assert.AreEqual(500u, ((GoldObjectEvent)e).BaseState.ObjectReference));
        Add<GoldUnknownEvent>("unknown packet", 0xffff, () => [], PayloadRule.RetainedSuffix);
        foreach (var op in new ushort[] { 0x173, 0x175 })
            Add<GoldObjectQuestEvent>("quest tag", op, () => Words(500, 0x1234ffff), verify: e => Assert.AreEqual((short)-1, ((GoldObjectQuestEvent)e).Value), owns: true);
        Add<GoldCreatureFightModeEvent>("fight mode", 0x119, () => Words(500, 1), owns: true);
        Add<GoldCreatureDialogEvent>("dialog", 0x11c, () => Words(500, 42, 0x80040), owns: true);
        Add<GoldCreatureSleepEvent>("sleep", 0x126, () => Words(500, 1), owns: true);
        Add<GoldCreatureTextEvent>("creature text", 0x11f, () => Words(500, 1), owns: true);
        foreach (var op in new ushort[] { 0x187, 0x188 })
            Add<GoldWorldRegionValueEvent>("region value", op, () => Words(257, 2, uint.MaxValue), owns: true);
        Add<GoldMapIconEvent>("map icon", 0x1cb, () => Words(2581, 2817, 2118, 0, 0xdcf0, 1), PayloadRule.RetainedSuffix,
            e => Assert.AreEqual((sbyte)-16, ((GoldMapIconEvent)e).Icon.Curvature), owns: true);
        Add<GoldAttractorEvent>("attractor", 0x1cc, () => Words(1, 2, 3, 4, 5, 6), PayloadRule.RetainedSuffix, owns: true);
        Add<GoldActualQuestsResetEvent>("actual quests reset", 0x1c9, () => Words(uint.MaxValue), PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(uint.MaxValue, ((GoldActualQuestsResetEvent)e).QuestWord), owns: true);
        Add<GoldActualQuestsSnapshotEvent>("actual quests snapshot", 0x1ca, () => Enumerable.Range(0, 128).Select(i => (byte)i).ToArray(),
            PayloadRule.Structured, owns: true, shorter: Enumerable.Range(0, 128).ToArray());
        Add<GoldSpawnRenewEvent>("spawn renew registration", 0x1cf, () => Enumerable.Range(0, 60).Select(i => (byte)i).ToArray(),
            PayloadRule.RetainedSuffix, e => Assert.AreEqual((byte)59, ((GoldSpawnRenewEvent)e).Record.ToPayload()[59]), owns: true);
        Add<GoldMouseoverEvent>("mouseover registration", 0x1d0, () => new byte[88], PayloadRule.RetainedSuffix,
            e => Assert.AreEqual((ushort)0, ((GoldMouseoverEvent)e).Record.X), owns: true);
        foreach (var op in new ushort[] { 0x1d3, 0x1d4, 0x1d5 })
        {
            var size = op == 0x1d5 ? 12 : 76;
            Add<GoldScriptTableEvent>("bulk script table", op, () => Enumerable.Range(0, size*2+1).Select(i => (byte)i).ToArray(),
                PayloadRule.Structured, e =>
                {
                    var snapshot = ((GoldScriptTableEvent)e).Snapshot;
                    Assert.AreEqual(2, snapshot.Count); Assert.AreEqual(size, snapshot.RecordSize);
                    Assert.AreEqual((byte)(size*2), snapshot.RemainingData[0]);
                }, owns: true, shorter: Enumerable.Range(0, size*2+1).ToArray());
        }
        Add<GoldObjectPositionEvent>("creature position correction", 0x179, () => Words(500, uint.MaxValue, 42, 257, 0x12345, 0x42b40000),
            PayloadRule.RetainedSuffix, e => Assert.AreEqual(-1, ((GoldObjectPositionEvent)e).Position.X), owns: true);
        Add<GoldScriptFunctionEvent>("script function replacement", 0x1c4,
            () => [..Words(7), ..new byte[64], ..Words(12, 8, uint.MaxValue, 123, 0x12340000)],
            PayloadRule.RetainedSuffix, e =>
            {
                var update = (GoldScriptFunctionEvent)e;
                Assert.AreEqual(7u, update.Index); Assert.AreEqual(123, update.Definition.Number);
                Assert.AreEqual(-1, update.Definition.Quest); Assert.AreEqual(12, update.Definition.Start);
                Assert.AreEqual(8, update.Definition.ByteLength); Assert.AreEqual(0x12340000u, update.Definition.Deleted);
                Assert.AreEqual((byte)0, update.Definition.DeletedFlag);
            }, owns: true);
        Add<GoldScriptDefinitionUpdateEvent>("script definition update", 0x1bf,
            () => [..Words(uint.MaxValue, 7), ..new byte[64], ..Words(1, 2, 3, 4, 5, 6, 7, 8)],
            PayloadRule.RetainedSuffix, e =>
            {
                var update = (GoldScriptDefinitionUpdateEvent)e;
                Assert.AreEqual(uint.MaxValue, update.Index); Assert.AreEqual(7u, update.Definition.Kind);
                Assert.AreEqual(8, update.Definition.Parameter(7));
            }, owns: true);
        Add<GoldScriptDefinitionDeleteEvent>("script definition delete", 0x1c0,
            () => [..Words(7), 0x50, 0], PayloadRule.RetainedSuffix,
            e => { Assert.AreEqual(7u, ((GoldScriptDefinitionDeleteEvent)e).Kind); Assert.AreEqual("P", ((GoldScriptDefinitionDeleteEvent)e).Name); }, owns: true);
        Add<GoldMovementCallbackEvent>("movement callback registration", 0x1d1,
            () => [..Words(uint.MaxValue), 0x47, 0x61, 0x74, 0x65, 0], PayloadRule.RetainedSuffix,
            e => { Assert.AreEqual(uint.MaxValue, ((GoldMovementCallbackEvent)e).FunctionIndex); Assert.AreEqual("Gate", ((GoldMovementCallbackEvent)e).Name); }, owns: true);
        Add<GoldMovementTriggerInsertionEvent>("movement trigger insertion", 0x1c3,
            () => [..Words(99, 60, 62, uint.MaxValue, 130, 129), ..new byte[80], ..new byte[128]],
            verify: e => { Assert.AreEqual(99u, ((GoldMovementTriggerInsertionEvent)e).Insertion.Sector); Assert.AreEqual(-1, ((GoldMovementTriggerInsertionEvent)e).Insertion.Surface); }, owns: true);
        Add<GoldScriptRegionEvent>("script region update", 0x1c5,
            () => Words(1, 7, 8, uint.MaxValue, 2, 3, 4, 5, 6, 7, 8), PayloadRule.RetainedSuffix,
            e => { var region = (GoldScriptRegionEvent)e; Assert.AreEqual(7u, region.FirstKey); Assert.AreEqual(-1, region.Region.FirstX); Assert.AreEqual(8u, region.Region.Timestamp); }, owns: true);
        Add<GoldTriggerPatchEvent>("trigger patch batch", 0x1ce,
            () => [..Words(2, 500, 10, 20, uint.MaxValue, 11, 20, 256), ..new byte[98*12]], PayloadRule.RetainedSuffix,
            e => { var patch = (GoldTriggerPatchEvent)e; Assert.AreEqual(2, patch.ActiveCount); Assert.AreEqual(100, patch.Cells.Count); Assert.AreEqual(-1, patch.Cells[0].Surface); }, owns: true);
        foreach (var op in new ushort[] { 0x019, 0x01a })
            Add<GoldSessionLockEvent>("session lock notification", op, () => [], PayloadRule.RetainedSuffix,
                e => Assert.AreEqual(op == 0x01a, ((GoldSessionLockEvent)e).IsLocked), owns: true);
        Add<GoldSessionGameFlagsEvent>("session game flags", 0x01e, () => Words(0x40014), PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(0x40014u, ((GoldSessionGameFlagsEvent)e).Flags), owns: true);
        foreach (var (code, target, name) in new[] { (15u, 0u, "celestial ground cast"), (17u, 501u, "rotating blades target cast"), (17u, 0u, "rotating blades caster fallback") })
            Add<GoldCreatureCastEvent>(name, 0x183, () => SeraphimVisualPackets.CastPayload(code, target),
                PayloadRule.RetainedSuffix, e =>
                {
                    var cast = (GoldCreatureCastEvent)e;
                    Assert.AreEqual(code, cast.Code); Assert.AreEqual(target, cast.TargetReference);
                    Assert.AreEqual(180000, cast.TargetX); Assert.AreEqual(135000, cast.TargetY);
                    Assert.AreEqual(2581u, cast.TargetSector);
                }, owns: true);
        foreach (var (resource, subtype, wearer, name) in new[] { (804u, 35u, 0u, "celestial full FX"), (814u, 41u, 501u, "rotating blades full FX") })
            Add<GoldObjectEvent>(name, 0x16F, () => SeraphimVisualPackets.FxPayload(resource, subtype, wearer), PayloadRule.Structured,
                e =>
                {
                    var fx = GoldObjectFxState.FromPayload(((GoldObjectEvent)e).ObjectState);
                    Assert.AreEqual(resource, fx.BaseState.ObjectType); Assert.AreEqual(subtype, fx.FxType);
                    Assert.AreEqual(wearer, fx.WearerReference); Assert.IsTrue(fx.IsSystemActive);
                }, shorter: [65, 66, 67, 68]);
        foreach (var code in new ushort[] { 1022, 1023 })
            Add<GoldCreatureSpecialMoveEvent>("vampire transformation " + code, 0x12D,
                () => VampireVisualPackets.SpecialPayload(code), PayloadRule.RetainedSuffix,
                e => Assert.AreEqual(code, ((GoldCreatureSpecialMoveEvent)e).Code), owns: true,
                shorter: Enumerable.Range(8, 84).ToArray());
        return rows;
    }
    private static byte[] Words(params uint[] words)
    {
        var bytes = new byte[words.Length*4];
        for (var i = 0; i < words.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i*4), words[i]);
        return bytes;
    }
    private static byte[] ObjectPayload(ushort opcode)
    {
        var prefix = opcode is 0x16c or 0x16e ? Words(500, 1) : Words(500, 1, 300, 400, 0, 2581);
        var state = new byte[41];
        BinaryPrimitives.WriteUInt32LittleEndian(state, 0xfeedf00d);
        BinaryPrimitives.WriteUInt32LittleEndian(state.AsSpan(4), 500);
        BinaryPrimitives.WriteUInt32LittleEndian(state.AsSpan(8), 1);
        return [..prefix, ..state];
    }
}
