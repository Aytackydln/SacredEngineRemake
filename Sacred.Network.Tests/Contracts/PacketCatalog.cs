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
        rows.Add(new("native health count uses roster address instead of presence", 0x1B, typeof(GoldPlayerRosterEvent),
            () => HostPlayerCountChecks.WireExample(RepositoryFiles.Root), Verify: entry =>
            { var roster=((GoldPlayerRosterEvent)entry).Roster; Assert.IsFalse(roster.GetEntry(1).IsPresent); Assert.AreNotEqual(0u,roster.GetEntry(1).NetworkAddressWord0); Assert.IsTrue(roster.GetEntry(2).IsPresent); }, ValidShorterLengths: [834]));
        void Add<T>(string name, ushort opcode, Func<byte[]> bytes, PayloadRule rule = PayloadRule.Exact,
            Action<GoldGameEvent>? verify = null, byte sender = 0, bool owns = false, int[]? shorter = null) where T : GoldGameEvent =>
            rows.Add(new(name, opcode, typeof(T), bytes, rule, verify, sender, owns, shorter));
        Add<GoldHostWorldAvailableEvent>("server host world availability", GoldWorldEntryCodec.HostStartOpcode,
            () => [], verify: entry => Assert.AreEqual((byte)1, entry.Message.Origin));
        Add<GoldWorldRegionFlagsEvent>("native initialization flag before callback", 0x185,
            () => RegionInitializationChecks.WireExample(RepositoryFiles.Root), verify: entry =>
            {
                var flags = (GoldWorldRegionFlagsEvent)entry;
                Assert.AreEqual(1u, flags.Region); Assert.AreEqual(1u, flags.Mask); Assert.IsTrue(flags.Set);
            }, sender: 1);
        Add<GoldScriptVariableEvent>("native EnterRegion clear-bit indexed publication", 0x1C7,
            () => HostRegionEntryBodyChecks.WireExample(RepositoryFiles.Root), sender: 1);
        Add<GoldObjectEvent>("native actual host ambient positioned factory", 0x16F,
            () => HostAmbientFactoryChecks.WireExample(RepositoryFiles.Root), PayloadRule.Structured,
            verify: entry => Assert.AreEqual(0x10000000u, ((GoldObjectEvent)entry).BaseState.Flags & 0x90000000u), sender: 1,
            shorter: [65,66,67,68,145,146,147,148,469,470,471,472]);
        Add<GoldSectorInterestEvent>("native first-host interest request",0x148,
            ()=>HostSectorInitializationChecks.InterestWireExample(RepositoryFiles.Root,false),
            verify:e=>{var request=((GoldSectorInterestEvent)e).Request;Assert.AreEqual((ushort)1,request.CurrentSector);Assert.AreEqual(768u,request.FreeReferenceCount);},sender:1);
        Add<GoldSectorEntryEvent>("controlled first-host initialization instructions",0x148,
            ()=>HostSectorInitializationChecks.InterestWireExample(RepositoryFiles.Root,true),
            verify:e=>{var slot=((GoldSectorEntryEvent)e).Entry.GetSlot(0);Assert.AreEqual((ushort)2,slot.Sector);Assert.IsTrue(slot.RequestsInitialization && slot.RequestsScripts);});
        Add<GoldQuestUpdateEvent>("native normal region quest activation", 0x1BA,
            () => RegionQuestChecks.WireExample(RepositoryFiles.Root), PayloadRule.RetainedSuffix, verify: entry =>
            {
                var quest = (GoldQuestUpdateEvent)entry;
                Assert.AreEqual(1u, quest.Index);
                Assert.AreEqual(292, quest.Record.Data.Length);
            }, sender: 1);
        Add<GoldCreatureUpdateEvent>("native normal region NPC dialog notification", 0x11A,
            () => StartupNpcSeeCommandChecks.RegionDialogWireExample(RepositoryFiles.Root), verify: entry =>
                Assert.AreEqual(65u, ((GoldCreatureUpdateEvent)entry).ObjectReference), sender: 1);
        Add<GoldAdmissionEvent>("native host replacement-name admission", 0x11,
            () => AdmissionStagingLiveChecks.WireExample(RepositoryFiles.Root), verify: e =>
            {
                var value = (GoldAdmissionEvent)e;
                Assert.AreEqual((byte)9, value.Flags);
                Assert.AreEqual((byte)16, value.PlayerSlot);
                Assert.AreEqual(0u, value.HostedServerWindow);
            });
        Add<GoldPlayerRosterEvent>("native live roster profile and bandwidth gates", 0x1B,
            () => RosterLiveChecks.WireExample(RepositoryFiles.Root), verify: e =>
            {
                var player = ((GoldPlayerRosterEvent)e).Roster.GetEntry(2);
                Assert.AreEqual("P2", player.Name);
                Assert.IsTrue(player.IsPresent);
                Assert.AreEqual(1u, player.ConnectionSpeed);
                Assert.AreEqual(1u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(player.Profile[4..]));
            }, shorter: [834]);
        Add<GoldAdmissionEvent>("native admission with both start notice flags", 0x11,
            () => AdmissionLiveChecks.WireExample(RepositoryFiles.Root), verify: e =>
            {
                var value = (GoldAdmissionEvent)e;
                Assert.AreEqual((byte)6, value.Flags);
                Assert.AreEqual((byte)16, value.PlayerSlot);
                Assert.AreEqual(0xdeadbeefu, value.HostedServerWindow);
            });
        Add<GoldAdmissionEvent>("native admission without hosted-server window", 0x11,
            () => Controller117Checks.AdmissionWireExample(RepositoryFiles.Root), verify: e =>
            {
                var value = (GoldAdmissionEvent)e;
                Assert.AreEqual(0u, value.HostedServerWindow);
                Assert.AreEqual((byte)1, value.PlayerSlot);
            });
        Add<GoldPlayerRosterEvent>("native formatted roster display names", 0x1B,
            () => RosterIdentifierChecks.WireExample(RepositoryFiles.Root), verify: e =>
            {
                var player = ((GoldPlayerRosterEvent)e).Roster.GetEntry(1);
                Assert.AreEqual("AAAAAAAAAAAA\\w", player.Name);
                Assert.IsTrue(player.IsPresent);
            }, shorter: [834]);
        Add<GoldPlayerRosterEvent>("native roster presence independent of character type", 0x1B,
            () => RosterConsumerChecks.WireExample(RepositoryFiles.Root), verify: e =>
            {
                var player=((GoldPlayerRosterEvent)e).Roster.GetEntry(2);
                Assert.IsTrue(player.IsPresent);
                Assert.AreEqual((byte)0,player.CharacterType);
            }, shorter:[834]);
        Add<GoldPlayerRosterEvent>("native accepted admission roster stage", 0x1B,
            () => AdmissionRosterChecks.WireExample(RepositoryFiles.Root), verify: e =>
            {
                var player = ((GoldPlayerRosterEvent)e).Roster.GetEntry(16);
                Assert.AreEqual(0x76543210u, player.SessionWord0);
                Assert.AreEqual(0x76543210u, player.NetworkAddressWord0);
                Assert.AreEqual(-1, player.LobbyIdentity);
                Assert.AreEqual((byte)0x5a, player.Profile[0]);
                Assert.IsFalse(player.IsPresent);
            }, shorter: [834]);
        Add<GoldAdmissionEvent>("native admission controller prefix", 0x11,
            () => AdmissionControllerChecks.WireExample(RepositoryFiles.Root), verify: e =>
            {
                var value = (GoldAdmissionEvent)e;
                Assert.AreEqual((byte)5, value.Flags);
                Assert.AreEqual((byte)16, value.PlayerSlot);
                Assert.AreEqual(0xdeadbeefu, value.HostToken);
            });
        Add<GoldPlayerStatisticsEvent>("native automatic-level elapsed counter",0x14F,
            ()=>AutomaticCreatureLevelChecks.WireExample(RepositoryFiles.Root),PayloadRule.Exact,
            e=>Assert.AreEqual(uint.MaxValue,((GoldPlayerStatisticsEvent)e).State.AutomaticLevelElapsedMilliseconds));
        Add<GoldObjectEvent>("native complete startup callback AI NPC", 0x16F, () => StartupNpcAiCommandChecks.WireExample(RepositoryFiles.Root), PayloadRule.Structured,
            verify: e => { var value = (GoldObjectEvent)e; Assert.AreEqual(119u,value.BaseState.ObjectType);
                Assert.AreEqual(1u,value.CreatureState!.Word1F4 & 1u); },
            shorter: [65,66,67,68,145,146,147,148,469,470,471,472]);
        Add<GoldObjectEvent>("native complete startup trader", 0x16F, () => StartupMerchantCommandChecks.WireExample(RepositoryFiles.Root), PayloadRule.Structured,
            verify: e => { var value = (GoldObjectEvent)e; Assert.AreEqual(692u,value.BaseState.ObjectType);
                Assert.AreEqual(0x2000u,value.CreatureState!.Word200 & 0x2000u); },
            shorter: [65,66,67,68,145,146,147,148,469,470,471,472]);
        Add<GoldObjectEvent>("native complete startup healer", 0x16F, () => StartupHealerCommandChecks.WireExample(RepositoryFiles.Root), PayloadRule.Structured,
            verify: e => { var value = (GoldObjectEvent)e; Assert.AreEqual(654u,value.BaseState.ObjectType);
                Assert.AreEqual(7,value.CreatureState!.Byte2B6); Assert.AreEqual(1u,value.CreatureState.Word245); },
            shorter: [65,66,67,68,145,146,147,148,469,470,471,472]);
        Add<GoldObjectEvent>("native live-blocker ground NPC", 0x16F, () => LoadedBlockedNpcChecks.GroundWireExample(RepositoryFiles.Root), PayloadRule.Structured,
            verify: e => { var value = (GoldObjectEvent)e; Assert.AreEqual(696u, value.BaseState.ObjectType);
                Assert.AreEqual((byte)0, value.BaseState.Surface); Assert.AreEqual((ushort)2217, value.BaseState.Sector); },
            shorter: [65,66,67,68,145,146,147,148,469,470,471,472]);
        Add<GoldObjectEvent>("native complete startup Horse", 0x16F, () => StartupHorseChecks.WireExample(RepositoryFiles.Root), PayloadRule.Structured,
            verify: e => { var value = (GoldObjectEvent)e; Assert.AreEqual(65u, value.BaseState.ObjectReference);
                Assert.AreEqual(555u, value.BaseState.ObjectType); Assert.IsFalse((value.BaseState.Flags & 2) != 0); },
            shorter: [65, 66, 67, 68, 145, 146, 147, 148, 469, 470, 471, 472]);
        Add<GoldObjectEvent>("native initial equipped placed creature", 0x16F, () => StartupCreatureSerializationChecks.WireExample(RepositoryFiles.Root), PayloadRule.Structured,
            verify: e => { var value = (GoldObjectEvent)e; Assert.AreEqual(26u, value.BaseState.ObjectReference);
                Assert.IsFalse((value.BaseState.Flags & 2) != 0); }, shorter: [65, 66, 67, 68, 145, 146, 147, 148, 469, 470, 471, 472]);
        Add<GoldObjectEvent>("native unloaded unplaced creature", 0x16F, () => UnloadedModelChecks.WireExample(RepositoryFiles.Root), PayloadRule.Structured,
            verify: e => { var value = (GoldObjectEvent)e; Assert.AreEqual(500u, value.BaseState.ObjectReference);
                Assert.AreEqual(295u, value.BaseState.ObjectType); Assert.AreEqual(2u, value.BaseState.Flags);
                Assert.IsFalse((value.BaseState.Flags & GoldPhysicalStateLayout.ModelLoadedFlag) != 0); },
            shorter: [65, 66, 67, 68, 145, 146, 147, 148, 469, 470, 471, 472]);
        Add<GoldCreatureHealthEvent>("absolute signed health consumer", 0x128, () => Words(500, uint.MaxValue),
            verify: e => Assert.AreEqual(-1, ((GoldCreatureHealthEvent)e).HitPoints), owns: true);
        Add<GoldCreatureAttackEvent>("melee swing without a damage result", 0x17C,
            () => Words(500, 182808, 135052, 0, 2582, 104, 0x3FB33333, 1114353424),
            verify: e => { var attack = (GoldCreatureAttackEvent)e; Assert.IsFalse(attack.IsBow);
                Assert.AreEqual(104u, attack.Motion); Assert.AreEqual(1.4f, attack.Speed); }, owns: true);
        Add<GoldCreatureDamageEvent>("numeric impact consumer", 0x17A, () => Words(500, 0xFFFF000C, 1, 2, 1, 0x12345678),
            verify: e => { var hit = (GoldCreatureDamageEvent)e; Assert.AreEqual((ushort)12, hit.Damage); Assert.AreEqual(0x12345678u, hit.ImpactEffectData); }, owns: true);
        Add<GoldCreatureDamageEvent>("fire hit flash creation", 0x17A, () => Words(500, 12, 0, 2, 1, 0xABCD0002),
            verify: e => { var hit = (GoldCreatureDamageEvent)e; Assert.AreEqual(1u, hit.ImpactEffectEnabled);
                  Assert.AreEqual((byte)2, unchecked((byte)hit.ImpactEffectData)); }, owns: true);
        Add<GoldCreatureDamageEvent>("explicit zero damage streak playback", 0x17A, () => Words(500, 0, 0, 2, 0, 0),
            verify: e => Assert.AreEqual((ushort)0, ((GoldCreatureDamageEvent)e).Damage), owns: true);
        Add<GoldCreatureFightModeEvent>("combat stance enabled", 0x119,
            () => Words(500, GoldCreatureStateLayout.FightModeFlag),
            verify: e => Assert.IsTrue(((GoldCreatureFightModeEvent)e).Enabled), owns: true);
        Add<GoldCreatureFightModeEvent>("local player leaves combat", 0x119, () => Words(2, 0),
            verify: e => Assert.IsFalse(((GoldCreatureFightModeEvent)e).Enabled), owns: true);
        Add<GoldAttachedFxEvent>("attached FX consumer", 0x149, () => Words(1, 44, 500, 0, 0, 0, 0),
            verify: e => Assert.AreEqual(500u, ((GoldAttachedFxEvent)e).WearerReference), owns: true);
        Add<GoldAttachedFxEvent>("attached deflection neck with root fallback", 0x149, () => Words(0x305, 10, 500, 0, 0, 0, 0),
            verify: e => Assert.AreEqual(10u, ((GoldAttachedFxEvent)e).FxSubtype), owns: true);
        Add<GoldObjectAnimationEvent>("creature explicit motion consumer", 0x170, () => Words(500, 59, 0x3F800000, 0x3F800000),
            PayloadRule.RetainedSuffix,
            verify: e => Assert.AreEqual(59u, ((GoldObjectAnimationEvent)e).Animation), owns: true);
        Add<GoldObjectEvent>("serialized deflection wearer", 0x16F,
            () => CombatReplicationConsumerTests.SerializedDeflection().Message.Payload, PayloadRule.Structured,
            verify: e => Assert.AreEqual(0x305u, ((GoldObjectEvent)e).BaseState.ObjectType), shorter: [65, 66, 67, 68]);
        foreach (var (opcode, notification) in new[] { ((ushort)0x182, (ushort)2), ((ushort)0x184, (ushort)8) })
            Add<GoldObjectFxSignalEvent>("particle flag consumer", opcode, () => Words(0x800001F4),
                verify: e => { var signal = (GoldObjectFxSignalEvent)e; Assert.AreEqual(500u, signal.ObjectReference); Assert.AreEqual(notification, signal.Notification); }, owns: true);
        Add<GoldExperienceAwardEvent>("experience award", 0x127, () => Words(500, 0xFFFFFFFF), PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(-1, ((GoldExperienceAwardEvent)e).Award), owns: true);
        Add<GoldStartPositionDataEvent>("compressed startup data", 0x132, RemainingReceivePackets.StartData, PayloadRule.RetainedSuffix,
            e => CollectionAssert.AreEqual(new byte[] { 3, 2, 1, 0, 8 }, ((GoldStartPositionDataEvent)e).DecodedData.ToArray()), owns: true);
        Add<GoldFxPartialEvent>("partial FX snapshot", 0x14B, () => RemainingReceivePackets.PartialFx(2), PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(194, ((GoldFxPartialEvent)e).State.Length), owns: true);
        Add<GoldControlledObjectEvent>("controlled object snapshot", 0x16A, RemainingReceivePackets.Controlled, PayloadRule.RetainedSuffix,
            e => Assert.AreEqual((ushort)0x169, ((GoldControlledObjectEvent)e).Snapshot.Message.Opcode), owns: true);
        Add<GoldFxBatchEvent>("multi FX batch", 0x181, () => RemainingReceivePackets.Batch(), PayloadRule.RetainedSuffix,
              e => Assert.AreEqual(2, ((GoldFxBatchEvent)e).Entries.Count), owns: true);
        Add<GoldCreatureFxSpawnEvent>("rendered puzzle radial burst", 0x14D, () => Words(500, 300, 400, 20, 2),
            PayloadRule.RetainedSuffix, e => Assert.AreEqual(2u, ((GoldCreatureFxSpawnEvent)e).Operands[3]), owns: true);
        Add<GoldFxBatchEvent>("terminal freeze impact", 0x181, () =>
        {
            var bytes = RemainingReceivePackets.Batch(1);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8 + 0x74C), 1);
            return bytes;
        }, PayloadRule.RetainedSuffix, e => Assert.AreEqual(1u, ((GoldFxBatchEvent)e).FinalParameter), owns: true);
        Add<GoldReferenceReconciliationEvent>("object reconciliation request", 0x197, () => [], PayloadRule.RetainedSuffix, owns: true);
        Add<GoldTradeEvent>("trade operation", 0x198, () => Words(0, 0x27, 42), PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(42u, ((GoldTradeEvent)e).Amount), owns: true);
        Add<GoldCreaturePairEffectEvent>("paired creature effect", 0x199, () => Words(2, 3, 4, 5), owns: true);
        Add<GoldCreatureEventBlock>("creature command block", 0x19B, () => new byte[128], owns: true);
        Add<GoldInterpreterStateEvent>("interpreter memory file", 0x1AA, RemainingReceivePackets.Interpreter, PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(4, ((GoldInterpreterStateEvent)e).State.Length), owns: true);
        Add<GoldCreatureTableEvent>("Creature.pak transfer", 0x1AB, RemainingReceivePackets.CreatureTable, PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(42u, ((GoldCreatureTableEvent)e).Records[0].ItemType), owns: true);
        Add<GoldBannerEvent>("banner transfer", 0x1AC, () => [], PayloadRule.RetainedSuffix,
            e => Assert.IsTrue(((GoldBannerEvent)e).ClearsBanner), owns: true);
        Add<GoldSessionNoticeEvent>("disconnect teardown", 0x002, () => Words(uint.MaxValue), PayloadRule.RetainedSuffix,
            e => { var n = (GoldSessionNoticeEvent)e; Assert.IsTrue(n.EndsSession); Assert.AreEqual(uint.MaxValue, n.ErrorCode); }, owns: true);
        Add<GoldServiceErrorEvent>("service error", 0x009, () => Words(107, uint.MaxValue), PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(uint.MaxValue, ((GoldServiceErrorEvent)e).Parameter), owns: true, shorter: [4]);
        Add<GoldGameSettingsEvent>("game settings", 0x00C, () => Words(uint.MaxValue, 1, 2, 3, 0x7fc00001, 5, 6, 7), PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(32, ((GoldGameSettingsEvent)e).Settings.Length), owns: true);
        foreach (var op in new ushort[] { 0x01c, 0x150, 0x151 })
            Add<GoldPlayerSessionEvent>("player session notice", op, () => op == 0x151 ? [] : Words(uint.MaxValue), PayloadRule.RetainedSuffix,
                e => Assert.AreEqual(op == 0x151 ? null : (uint?)uint.MaxValue, ((GoldPlayerSessionEvent)e).Reason), owns: true);
        foreach (var op in new ushort[] { 0x024, 0x026 })
            Add<GoldLobbyResultEvent>("lobby operation result", op, () => [..Words(17, 0), ..LobbyLifecyclePackets.Text("OK")], PayloadRule.RetainedSuffix,
                e => Assert.AreEqual("OK", ((GoldLobbyResultEvent)e).Text), owns: true);
        Add<GoldLobbyTransferEvent>("public profile transfer", 0x029, LobbyLifecyclePackets.PublicTransfer, PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(0xD98, ((GoldLobbyTransferEvent)e).DecodedProfile!.Value.Length), owns: true);
        Add<GoldLobbyTransferEvent>("player profile transfer", 0x02B, LobbyLifecyclePackets.PlayerTransfer, PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(556, ((GoldLobbyTransferEvent)e).PlayerProfile!.Value.Length), owns: true);
        Add<GoldAlternatePickupEvent>("alternate item pickup", 0x143, () => Words(500, 0x7fc00001), PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(0x7fc00001u, ((GoldAlternatePickupEvent)e).FacingBits), sender: 2, owns: true);
        Add<GoldInventoryInsertEvent>("local inventory insertion", 0x145, () => Words(123, uint.MaxValue), PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(uint.MaxValue, ((GoldInventoryInsertEvent)e).Word04), owns: true);
        foreach (var op in new ushort[] { 0x14a, 0x14d, 0x14e })
            Add<GoldCreatureFxSpawnEvent>("creature FX creation", op, () => op == 0x14e ? Words(500, uint.MaxValue, 0x80000000, 3) : Words(500, uint.MaxValue, 0x80000000, 3, 0x7fc00001),
                PayloadRule.RetainedSuffix, e => Assert.AreEqual(uint.MaxValue, ((GoldCreatureFxSpawnEvent)e).Operands[0]), owns: true);
        Add<GoldCreatureDeathCommandEvent>("creature death command", 0x17d, () => Words(500, uint.MaxValue), PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(uint.MaxValue, ((GoldCreatureDeathCommandEvent)e).PlayerWord), owns: true);
        Add<GoldCreatureDeathCommandEvent>("player death command", 0x17d, () => Words(2, 1), PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(1u, ((GoldCreatureDeathCommandEvent)e).PlayerWord), owns: true);
        Add<GoldContainerOpenCommandEvent>("container open command", 0x18b, () => Words(500), PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(500u, ((GoldContainerOpenCommandEvent)e).ObjectReference), owns: true);
        Add<GoldRegionObjectTypesEvent>("region object type record", 0x189, () => CreatureInteractionPackets.Region(),
            verify: e => { var d = (GoldRegionObjectTypesEvent)e; Assert.AreEqual(3, d.RegionIndex); Assert.AreEqual(2, d.EntryCount); }, owns: true);
        foreach (var op in new ushort[] { 0x33, 0x36 })
            Add<GoldBuddyEvent>("buddy status", op, () => CreatureInteractionPackets.Buddy(), PayloadRule.RetainedSuffix,
                e => { var b = ((GoldBuddyEvent)e).Buddy; Assert.AreEqual("User", b.Name); Assert.AreEqual("Lobby", b.ChannelName); }, owns: true);
        Add<GoldCreatureDefinitionEvent>("creature definition", 0x114, CreatureCommandPackets.Definition, PayloadRule.RetainedSuffix,
            e => { var d = (GoldCreatureDefinitionEvent)e; Assert.AreEqual(500u, d.ContextReference); Assert.AreEqual(348, d.Definition.Length); }, owns: true);
        Add<GoldCreatureValueCommandEvent>("creature value command", 0x125, () => Words(500, 30, uint.MaxValue, 0x7fc00001, 1, 2, 3), PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(0x7fc00001u, ((GoldCreatureValueCommandEvent)e).NativeArguments[2]), owns: true);
        foreach (var op in new ushort[] { 0x12e, 0x12f })
            Add<GoldCreatureLinkEvent>("creature link", op, () => op == 0x12e ? Words(500, 501) : Words(500, 501, 0x80000000), PayloadRule.RetainedSuffix,
                e => { var link = (GoldCreatureLinkEvent)e; Assert.AreEqual(501u, link.ChildReference); Assert.AreEqual(op == 0x12f, link.Option); }, owns: true);
        Add<GoldPhysicalHitEvent>("physical hit notifications", 0x13c, CreatureCommandPackets.Hit, PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(72, ((GoldPhysicalHitEvent)e).Data.Length), owns: true);
        foreach (var op in new ushort[] { 0x15e, 0x160 })
            Add<GoldMountTransitionEvent>("mount transition", op, () => Words(500, 501), PayloadRule.RetainedSuffix,
                e => { var mount = (GoldMountTransitionEvent)e; Assert.AreEqual(501u, mount.PartnerReference); Assert.AreEqual(op == 0x160, mount.Mounts); }, owns: true);
        Add<GoldFxTargetEvent>("FX target inputs", 0x177, () => Words(500, 501, 0x80000000, 0x3f800000, 0x7fc00001, uint.MaxValue), PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(0x7fc00001u, ((GoldFxTargetEvent)e).Vector.Z), owns: true);
        Add<GoldLocalHitCheckEvent>("local hit check", 0x180, () => [..Words(2), ..Enumerable.Range(0, 32).Select(i => (byte)(i * 7))], PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(32, ((GoldLocalHitCheckEvent)e).Data.Length), owns: true);
        Add<GoldDialogRegistrationEvent>("dialog callback registration", 0x1cd, () => [..Words(900, 2, uint.MaxValue), 78, 112, 99, 0], PayloadRule.RetainedSuffix,
            e => { var d = (GoldDialogRegistrationEvent)e; Assert.AreEqual("Npc", d.Name); Assert.AreEqual(uint.MaxValue, d.QuestWord); }, owns: true);
        Add<GoldScriptInvokeEvent>("named script invocation", 0x110, () => [68, 111, 110, 101, 0], PayloadRule.RetainedSuffix,
            e => Assert.AreEqual("Done", ((GoldScriptInvokeEvent)e).FunctionName), owns: true);
        Add<GoldCreatureMarkerEvent>("creature marker request", 0x13d, () => Words(500), PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(500u, ((GoldCreatureMarkerEvent)e).ObjectReference), owns: true);
        Add<GoldCreatureTimedControlEvent>("creature timed control", 0x178, () => Words(500, uint.MaxValue, 0x80000000), PayloadRule.RetainedSuffix,
            e => { var control = (GoldCreatureTimedControlEvent)e; Assert.AreEqual(uint.MaxValue, control.ControlWord);
                Assert.AreEqual(0x80000000u, control.ClockWord); }, owns: true);
        Add<GoldRegionCountdownEvent>("region countdown", 0x17e, () => Words(999, 0xaabbccff), PayloadRule.RetainedSuffix,
            e => { var countdown = (GoldRegionCountdownEvent)e; Assert.AreEqual((byte)255, countdown.RegionIndex);
                Assert.AreEqual(0xaabbccffu, countdown.SelectorWord); }, owns: true);
        Add<GoldCreatureTypeChangeEvent>("creature type change request", 0x1d2, () => Words(500, uint.MaxValue), PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(uint.MaxValue, ((GoldCreatureTypeChangeEvent)e).ObjectType), owns: true);
        Add<GoldCounterMessageEvent>("counter HUD message", 0x19e, () => Words(16, 3, 12345), PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(12345u, ((GoldCounterMessageEvent)e).TextResource), owns: true);
        foreach (var opcode in new ushort[] { 0x19f, 0x1a0, 0x1a1 })
            Add<GoldCounterTargetEvent>("counter resource", opcode, () => Words(17), PayloadRule.RetainedSuffix,
                e => Assert.AreEqual(17u, ((GoldCounterTargetEvent)e).TargetResource), owns: true);
        foreach (var opcode in new ushort[] { 0x1a2, 0x1a3 })
            Add<GoldCounterCollectEvent>("collection notification", opcode, () => Words(500, 17), PayloadRule.RetainedSuffix,
                e => { var collection = (GoldCounterCollectEvent)e; Assert.AreEqual(500u, collection.ObjectReference);
                    Assert.AreEqual(opcode == 0x1a3, collection.UpdatesCounter); }, owns: true);
        foreach (var opcode in new ushort[] { 0x1a4, 0x1a5 })
            Add<GoldSpecialFxValueEvent>("special FX scalar", opcode, () => Words(500, uint.MaxValue), PayloadRule.RetainedSuffix,
                e => Assert.AreEqual(uint.MaxValue, ((GoldSpecialFxValueEvent)e).Value), owns: true);
        Add<GoldPingPongBallEvent>("pingpong ball state", 0x1a6,
            () => [..Words(500, 300, 400, 0x123401ff, 0x80000000, 0x3f800000, 0x7fc00001, 0x41200000, 0x41a00000, 0x41f00000, 0x3fc00000), 2],
            PayloadRule.RetainedSuffix, e => { var ball = (GoldPingPongBallEvent)e; Assert.AreEqual((byte)255, ball.Surface);
                Assert.AreEqual(0x80000000u, ball.Direction.X); Assert.IsTrue(ball.SetsParticleFlag); }, owns: true);
        Add<GoldPacmanGhostEvent>("pacman ghost state", 0x1a9,
            () => Words(500, 300, 400, 0x123401ff, 0x80000000, 0x3f800000, 0x7fc00001, 0x3fc00000, uint.MaxValue, 0x80000000, 123),
            PayloadRule.RetainedSuffix, e => { var ghost = (GoldPacmanGhostEvent)e; Assert.AreEqual(0x7fc00001u, ghost.VectorData.Z);
                Assert.AreEqual(uint.MaxValue, ghost.IntegerData1); Assert.AreEqual(123u, ghost.IntegerData3); }, owns: true);
        Add<GoldQuestProgressEvent>("quest progress", 0x1b8, () => [..Words(1), ..new byte[100], ..Words(1, uint.MaxValue)],
            PayloadRule.RetainedSuffix, e => { var progress = (GoldQuestProgressEvent)e;
                Assert.AreEqual(1, progress.HeaderIndex); Assert.AreEqual(1, progress.Entries.Count); }, owns: true);
        Add<GoldQuestInsertEvent>("quest insert", 0x1b9, () => [..Words(500), ..new byte[24], ..Words(0, 0)],
            PayloadRule.RetainedSuffix, e => Assert.AreEqual(500u, ((GoldQuestInsertEvent)e).ObjectReference), owns: true);
        foreach (var opcode in new ushort[] { 0x1ba, 0x1bb, 0x1bc, 0x1bd, 0x1be })
            Add<GoldQuestUpdateEvent>("quest table update", opcode, () => [..Words(3), ..(opcode == 0x1be ? Words(32) : Array.Empty<byte>()), ..new byte[opcode switch
                { 0x1ba => 292, 0x1bb => 104, 0x1bd => 368, _ => 32 }]], PayloadRule.RetainedSuffix,
                e => Assert.AreEqual(3u, ((GoldQuestUpdateEvent)e).Index), owns: true);
        Add<GoldIndexedRecordEvent>("indexed field record", 0x15d, () => [..Words(3), ..new byte[188]],
            PayloadRule.RetainedSuffix, e => Assert.AreEqual(3u, ((GoldIndexedRecordEvent)e).Index), owns: true);
        Add<GoldObjectAnimationEvent>("object animation request", 0x170, () => Words(500, 0xaa, 0x3f800000, 0x3f000000),
            PayloadRule.RetainedSuffix, e => { var animation = (GoldObjectAnimationEvent)e;
                Assert.AreEqual(0xaau, animation.Animation); Assert.AreEqual(0x3f000000u, animation.SecondArgumentBits); }, owns: true);
        Add<GoldContainerCloseEvent>("container close request", 0x18d, () => Words(500), PayloadRule.RetainedSuffix,
            e => Assert.AreEqual(500u, ((GoldContainerCloseEvent)e).ObjectReference), owns: true);
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
        Add<GoldObjectEvent>("native current entered host hero", 0x169,
            () => HostCurrentPublicationChecks.HeroWireExample(RepositoryFiles.Root), PayloadRule.Structured,
            e => { var value=((GoldObjectEvent)e).BaseState; Assert.AreEqual(1u,value.ObjectReference); Assert.AreEqual((ushort)2581,value.Sector); Assert.AreEqual(180343,value.X); }, sender:1,
            shorter:[65,66,67,68,145,146,147,148,469,470,471,472]);
        Add<GoldObjectEvent>("native current saved host item",0x16E,
            () => HostCurrentPublicationChecks.ItemWireExample(RepositoryFiles.Root),PayloadRule.Structured,
            e=>Assert.AreEqual(17u,((GoldObjectEvent)e).BaseState.ObjectReference),sender:1,
            shorter:[49,50,51,52,129,130,131,132,273,274,275,276]);
        Add<GoldInventoryEvent>("native current host primary inventory",0x122,
            () => HostCurrentPublicationChecks.InventoryWireExample(RepositoryFiles.Root),PayloadRule.Structured,
            e=>{var value=(GoldInventoryEvent)e;Assert.AreEqual(1u,value.InventoryId);Assert.AreEqual(0,value.Inventory.QuestCount);},sender:1);
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
        Add<GoldMouseoverEvent>("native startup hideout mouseover",0x1D0,()=>StartupHideoutChecks.WireExample(RepositoryFiles.Root),
            PayloadRule.RetainedSuffix,e=>{var patch=((GoldMouseoverEvent)e).Record;Assert.AreEqual(2,patch.Mode);Assert.AreEqual((ushort)1,patch.RangeInput);Assert.AreEqual("callback",patch.Callback);},owns:true);
        Add<GoldMouseoverEvent>("native startup grave mouseover", 0x1D0, () => StartupMouseoverChecks.WireExample(RepositoryFiles.Root),
            PayloadRule.RetainedSuffix, e => { var patch = ((GoldMouseoverEvent)e).Record;
                Assert.AreEqual((ushort)3546,patch.X); Assert.AreEqual((ushort)2463,patch.Y);
                Assert.AreEqual("setvar_000",patch.Callback); Assert.AreEqual(1,patch.Mode); }, owns: true);
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
        Add<GoldWorldTriggerEvent>("native initial-sector trigger creation producer",0x1AE,
            () => HostSectorInitializationChecks.WireExample(RepositoryFiles.Root,0x1AE).Payload,PayloadRule.RetainedSuffix,
            e => { var trigger=(GoldWorldTriggerEvent)e; Assert.AreEqual(4214u,trigger.Reference); Assert.AreEqual("Trg756",trigger.Name); });
        Add<GoldTriggerPatchEvent>("native initial-sector normalized trigger patch producer",0x1CE,
            () => HostSectorInitializationChecks.WireExample(RepositoryFiles.Root,0x1CE).Payload,PayloadRule.RetainedSuffix,
            e => { var patch=(GoldTriggerPatchEvent)e; Assert.AreEqual(1,patch.ActiveCount); Assert.AreEqual(3378,patch.Cells[0].X); });
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
        Add<GoldObjectEvent>("native startup Horse equipment before rejected installation", 0x16E, () => StartupHorseChecks.EquipmentWireExample(RepositoryFiles.Root), PayloadRule.Structured,
            verify: e => { var value = (GoldObjectEvent)e; Assert.AreEqual(66u, value.BaseState.ObjectReference);
                Assert.AreEqual(3201u, value.BaseState.ObjectType); Assert.IsTrue((value.BaseState.Flags & 2) != 0); },
            shorter: [49, 50, 51, 52, 129, 130, 131, 132, 272]);
        foreach (var index in new[] { 0, 1 })
            Add<GoldObjectEvent>($"native initial installed equipment {index}", 0x16E, () => StartupCreatureSerializationChecks.EquipmentWireExample(RepositoryFiles.Root, index), PayloadRule.Structured,
                verify: e => Assert.AreEqual(index == 0 ? 28u : 27u, ((GoldObjectEvent)e).BaseState.ObjectReference),
                shorter: index == 0 ? [49, 50, 51, 52, 129, 130, 131, 132] : [49, 50, 51, 52, 129, 130, 131, 132, 273, 274, 275, 276]);
        Add<GoldMapIconEvent>("native startup map label", 0x1CB, () => StartupMapIconChecks.WireExample(RepositoryFiles.Root),
            PayloadRule.RetainedSuffix, e => { var icon = ((GoldMapIconEvent)e).Icon;
                Assert.AreEqual(3376, icon.X); Assert.AreEqual(2670, icon.Y); Assert.AreEqual((sbyte)43, icon.AngleDegrees);
                Assert.IsTrue(icon.IsLabel); }, owns: true);
        Add<GoldObjectEvent>("native startup active FX with particle snapshot", 0x16F,
            () => StartupEffectFactoryChecks.WireExample(RepositoryFiles.Root), PayloadRule.Structured,
            e => { var fx = GoldObjectFxState.FromPayload(((GoldObjectEvent)e).ObjectState);
                Assert.AreEqual(34u, fx.FxType); Assert.AreEqual(802u, GoldParticleSnapshot.FromPayload(fx.ParticleState.Span).Resource); },
            shorter: new[] { 65, 66, 67, 68 }.Concat(Enumerable.Range(251, 224)).ToArray());
        Add<GoldObjectEvent>("native entered-sector owned fixed FX", 0x16F,
            () => HostEnteredEffectChecks.WireExample(RepositoryFiles.Root), PayloadRule.Structured,
            e => { var fx = GoldObjectFxState.FromPayload(((GoldObjectEvent)e).ObjectState);
                Assert.AreEqual(0x10000000u,fx.BaseState.Flags); Assert.AreEqual((ushort)2503,fx.BaseState.Sector); },
            sender: 1, shorter: new[] { 65, 66, 67, 68 }.Concat(Enumerable.Range(251, 224)).ToArray());
        Add<GoldObjectEvent>("native complete startup special level NPC", 0x16F, () => StartupSpecialLevelChecks.WireExample(RepositoryFiles.Root), PayloadRule.Structured,
            verify: e => { var value=(GoldObjectEvent)e; Assert.AreEqual(355u,value.BaseState.ObjectType);
                Assert.AreEqual(0x80000u,value.CreatureState!.Word1F4 & 0x80000u); },
            shorter: [65,66,67,68,145,146,147,148,469,470,471,472]);
        Add<GoldObjectEvent>("native MAGICPRISON opaque snapshot", 0x16F, () => MagicPrisonSnapshotChecks.WireExample(RepositoryFiles.Root), PayloadRule.Structured,
            verify: e => { var fx=GoldObjectFxState.FromPayload(((GoldObjectEvent)e).ObjectState);
                Assert.AreEqual(854u,fx.BaseState.ObjectType); Assert.AreEqual(78u,fx.FxType);
                Assert.IsFalse(GoldParticleSnapshot.FromPayload(fx.ParticleState.Span).HasDerivedMarker); }, shorter: new[]{65,66,67,68}.Concat(Enumerable.Range(251,1332)).ToArray());
        Add<GoldObjectEvent>("verified host named MAGICPRISON snapshot", 0x16F, () => MagicPrisonNamedChecks.WireExample(RepositoryFiles.Root), PayloadRule.Structured,
            verify: e => { var fx=GoldObjectFxState.FromPayload(((GoldObjectEvent)e).ObjectState);
                Assert.AreEqual(854u,fx.BaseState.ObjectType); Assert.AreEqual(8310u,fx.BaseState.Word3C);
                Assert.AreEqual(78u,fx.FxType); Assert.IsTrue(fx.IsSystemActive); },
            shorter: new[]{65,66,67,68}.Concat(Enumerable.Range(251,1332)).ToArray());
        Add<GoldObjectEvent>("native complete startup Book", 0x16E, () => StartupBookCreationChecks.WireExample(RepositoryFiles.Root), PayloadRule.Structured,
            verify: e => Assert.AreEqual(4098u,((GoldObjectEvent)e).BaseState.ObjectType), shorter: [49,50,51,52,129,130,131,132]);
        Add<GoldObjectEvent>("native startup placed QuestItem", 0x16E,
            () => StartupItemCreationChecks.WireExample(RepositoryFiles.Root), PayloadRule.Structured,
            e => { var state = GoldItemState.FromPayload(((GoldObjectEvent)e).ObjectState);
                Assert.AreEqual(54u,state.PhysicalState.BaseState.ObjectReference); Assert.AreEqual(4993u,state.PhysicalState.BaseState.ObjectType);
                Assert.AreEqual(0x201u,state.PhysicalState.BaseState.Flags); }, shorter: [49,50,51,52,129,130,131,132]);
        Add<GoldObjectEvent>("verified host ground Weapon", 0x16E,
            () => StartupGroundEquipmentChecks.WireExample(RepositoryFiles.Root,1779), PayloadRule.Structured,
            e => { var state = GoldItemState.FromPayload(((GoldObjectEvent)e).ObjectState);
                Assert.AreEqual(1779u,state.PhysicalState.BaseState.ObjectType); Assert.AreEqual(0x200u,state.PhysicalState.BaseState.Flags);
                Assert.IsTrue(state.HasWeaponExtension); Assert.AreEqual(285,state.SerializedSize); },
            shorter: [49,50,51,52,129,130,131,132,273,274,275,276]);
        Add<GoldObjectEvent>("verified host ground Shield", 0x16E,
            () => StartupGroundEquipmentChecks.WireExample(RepositoryFiles.Root,1200), PayloadRule.Structured,
            e => { var state = GoldItemState.FromPayload(((GoldObjectEvent)e).ObjectState);
                Assert.AreEqual(1200u,state.PhysicalState.BaseState.ObjectType); Assert.IsFalse(state.HasWeaponExtension);
                Assert.AreEqual(265,state.SerializedSize); }, shorter: [49,50,51,52,129,130,131,132]);
        Add<GoldCreatureUpdateEvent>("native startup Horse owner dialog state", 0x11A,
            () => StartupOwnerChecks.WireExample(RepositoryFiles.Root), PayloadRule.Exact,
            verify: e => { var state=(GoldCreatureUpdateEvent)e; Assert.AreEqual(65u,state.ObjectReference); });
        Add<GoldCreatureMountEvent>("native startup previous Horse detach", 0x15C,
            () => StartupOwnerChecks.ReplacementWireExample(RepositoryFiles.Root), PayloadRule.Exact,
            verify: e => { var value=(GoldCreatureMountEvent)e; Assert.AreEqual(0u,value.FirstReference); Assert.AreEqual(65u,value.SecondReference); });
        Add<GoldObjectEvent>("native startup indoor Horse",0x16F,()=>StartupLoadedHorseChecks.WireExample(RepositoryFiles.Root),PayloadRule.Structured,
            verify:e=>{var value=(GoldObjectEvent)e;Assert.AreEqual(86u,value.BaseState.ObjectReference);Assert.AreEqual((byte)1,value.BaseState.Surface);},
            shorter:[65,66,67,68,145,146,147,148,469,470,471,472]);
        Add<GoldObjectEvent>("host pre-install Armor EquipItem",0x16E,
            () => ScriptArmorCommandChecks.WireExample(RepositoryFiles.Root),PayloadRule.Structured,
            e => { var item=GoldItemState.FromPayload(((GoldObjectEvent)e).ObjectState);
                Assert.AreEqual(3846u,item.PhysicalState.BaseState.ObjectType);
                Assert.IsFalse(item.HasWeaponExtension);Assert.AreEqual(265,item.SerializedSize); },
            shorter: [49,50,51,52,129,130,131,132]);
        Add<GoldObjectEvent>("host explicit elevated blocker NPC",0x16F,
            ()=>LoadedBlockedNpcChecks.WireExample(RepositoryFiles.Root),PayloadRule.Structured,
            e=> { var value=(GoldObjectEvent)e;Assert.AreEqual(516u,value.BaseState.ObjectType);Assert.AreEqual((byte)1,value.BaseState.Surface); },
            shorter:[65,66,67,68,145,146,147,148,469,470,471,472]);
        Add<GoldObjectEvent>("host sector-zero NPC",0x16F,
            ()=>SectorZeroNpcChecks.WireExample(RepositoryFiles.Root),PayloadRule.Structured,
            e=> { var value=(GoldObjectEvent)e;Assert.AreEqual(297u,value.BaseState.ObjectType);Assert.AreEqual((ushort)0,value.BaseState.Sector);Assert.AreEqual(0u,value.BaseState.Flags&2u); },
            shorter:[65,66,67,68,145,146,147,148,469,470,471,472]);
        Add<GoldObjectEvent>("verified host named BUBBLEPRISON", 0x16F, () => BubblePrisonChecks.WireExample(RepositoryFiles.Root), PayloadRule.Structured,
            verify: e => { var fx=GoldObjectFxState.FromPayload(((GoldObjectEvent)e).ObjectState);
                Assert.AreEqual(853u,fx.BaseState.ObjectType); Assert.AreEqual(8310u,fx.BaseState.Word3C); Assert.AreEqual(77u,fx.FxType);
                Assert.AreEqual(128,GoldParticleSnapshot.FromPayload(fx.ParticleState.Span).DerivedState.Length); },
            shorter: new[]{65,66,67,68}.Concat(Enumerable.Range(251,296)).ToArray());
        Add<GoldObjectEvent>("verified host elevated BUBBLEPRISON", 0x16F, () => ElevatedBubblePrisonChecks.WireExample(RepositoryFiles.Root), PayloadRule.Structured,
            verify: e => { var fx=GoldObjectFxState.FromPayload(((GoldObjectEvent)e).ObjectState);
                Assert.AreEqual((byte)1,fx.BaseState.Surface); Assert.AreEqual(17572u,fx.BaseState.Word3C);
                Assert.AreEqual(0f,GoldParticleSnapshot.FromPayload(fx.ParticleState.Span).Position.Z); },
            shorter: new[]{65,66,67,68}.Concat(Enumerable.Range(251,296)).ToArray());
        Add<GoldWorldBankEvent>("normalized host dialog association section fixture",0x1B3,
            () => DialogAssociationNormalizationChecks.BankThreeWireExample(RepositoryFiles.Root),PayloadRule.RetainedSuffix,
            e => { var bank=((GoldWorldBankEvent)e).Snapshot;
                Assert.AreEqual(1,bank.Sections[5].Count); Assert.AreEqual(88,bank.Sections[5].ElementSize); });
        Add<GoldWorldBankEvent>("verified host trigger-cell patch section fixture",0x1B3,
            () => TriggerPatchBankSourceChecks.BankThreeWireExample(RepositoryFiles.Root).Payload,PayloadRule.RetainedSuffix,
            e => { var bank=((GoldWorldBankEvent)e).Snapshot;
                Assert.AreEqual(5,bank.Sections[2].Count); Assert.AreEqual(8,bank.Sections[2].ElementSize); });
        Add<GoldWorldBankEvent>("native bank-one reader fixture",0x1B1,
            () => RemainingWorldBankChecks.BankOneWireExample(RepositoryFiles.Root),PayloadRule.RetainedSuffix,
            e => { var bank=((GoldWorldBankEvent)e).Snapshot;
                Assert.AreEqual(128,bank.Globals!.FirstBlock.Length); Assert.AreEqual(10,bank.Sections.Count); });
        Add<GoldWorldBankEvent>("host replacement block in native bank-one reader control",0x1B1,
            () => RegionReplacementBankChecks.BankOneWireExample(RepositoryFiles.Root),PayloadRule.RetainedSuffix,
            e => Assert.IsTrue(((GoldWorldBankEvent)e).Snapshot.Globals!.SecondBlock.ToArray().All(value => value == 0)));
        Add<GoldScriptRegionEvent>("verified host SetRG producer",0x1C5,
            () => ScriptSetRegionChecks.WireExample(RepositoryFiles.Root),PayloadRule.RetainedSuffix,
            e => { var region=(GoldScriptRegionEvent)e; Assert.AreEqual(2726u,region.FirstKey); Assert.AreEqual(2655u,region.SecondKey); Assert.AreEqual(1u,region.Region.Type); });
        Add<GoldEquipmentRemovalEvent>("verified host Armor hand removal",0x11D,
            () => ScriptArmorRefreshChecks.RemovalWireExample(RepositoryFiles.Root), PayloadRule.Exact,
            e => { var removal=(GoldEquipmentRemovalEvent)e; Assert.AreEqual(2691u,removal.CreatureReference); });
        Add<GoldObjectEvent>("verified host ShoulderArmor EquipItem",0x16E,
            () => ScriptArmorCommandChecks.ShoulderWireExample(RepositoryFiles.Root),PayloadRule.Structured,
            e => { var item=GoldItemState.FromPayload(((GoldObjectEvent)e).ObjectState);
                Assert.AreEqual(4015u,item.PhysicalState.BaseState.ObjectType); Assert.AreEqual(265,item.SerializedSize); },
            shorter: [49,50,51,52,129,130,131,132]);
        foreach (var type in new uint[] { 4013, 4014, 4012 })
            Add<GoldObjectEvent>($"verified host Armor accessory {type}",0x16E,
                () => ScriptArmorCommandChecks.ChainWireExample(RepositoryFiles.Root,type),PayloadRule.Structured,
                e => { var item=GoldItemState.FromPayload(((GoldObjectEvent)e).ObjectState);
                    Assert.AreEqual(type,item.PhysicalState.BaseState.ObjectType); Assert.AreEqual(265,item.SerializedSize); },
                shorter: [49,50,51,52,129,130,131,132]);
        Add<GoldObjectEvent>("verified host behavior-2000 trader",0x16F,
            ()=>StartupMerchantCommandChecks.Behavior2000WireExample(RepositoryFiles.Root),PayloadRule.Structured,
            e=> { var value=(GoldObjectEvent)e;Assert.AreEqual(692u,value.BaseState.ObjectType);Assert.AreEqual(0x2000u,value.CreatureState!.Word1F4&0x2000u); },
            shorter:[65,66,67,68,145,146,147,148,469,470,471,472]);
        Add<GoldQuestUpdateEvent>("verified host Tutorial entry journal",0x1BD,
            ()=>ScriptTriggerQuestChecks.WireExample(RepositoryFiles.Root),PayloadRule.RetainedSuffix,
            e=> { var value=(GoldQuestUpdateEvent)e;Assert.AreEqual(0u,value.Index);Assert.AreEqual(3u,BinaryPrimitives.ReadUInt32LittleEndian(value.Record.Data)); },
            owns:true, shorter:[4,12,371]);
        Add<GoldQuestUpdateEvent>("verified host populated QuestBook journal",0x1BD,
            ()=>ScriptQuestBookChecks.WireExample(RepositoryFiles.Root),PayloadRule.RetainedSuffix,
            e=> { var value=(GoldQuestUpdateEvent)e;Assert.AreEqual(0u,value.Index);Assert.AreEqual(9000u,BinaryPrimitives.ReadUInt32LittleEndian(value.Record.Data[32..])); },
            owns:true, shorter:[4,12,371]);
        Add<GoldQuestUpdateEvent>("verified host Tutorial compass journal",0x1BD,
            ()=>ScriptQuestCompassChecks.WireExample(RepositoryFiles.Root),PayloadRule.RetainedSuffix,
            e=> { var value=(GoldQuestUpdateEvent)e;Assert.AreEqual(0u,value.Index);Assert.AreEqual(3349u,BinaryPrimitives.ReadUInt32LittleEndian(value.Record.Data[12..])); },
            owns:true, shorter:[4,12,371]);
        Add<GoldCreatureUpdateEvent>("verified host startup dialog change",0x11A,
            ()=>StartupDialogStateChecks.WireExample(RepositoryFiles.Root),PayloadRule.Exact,
            e=> { var value=(GoldCreatureUpdateEvent)e;Assert.AreEqual(2u,value.State.Word245); });
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
