using System.Buffers.Binary;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Networking;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests;

/// <summary>Original selected file plus controlled TCP grants, not actor/host approval.</summary>
[TestClass, TestCategory("Installed")]
public sealed class HostCharacterInitializationTests
{
    private static readonly string Installation = Environment.GetEnvironmentVariable("SACRED_GAME_DIRECTORY") ?? @"E:\SteamLibrary\steamapps\common\Sacred Gold";
    private static readonly Lazy<WorldHostSavedCharacterSource> Source = new(() => WorldHostSavedCharacterSource.ReadSelected(Installation, 1));

    [TestMethod, DataRow(1), DataRow(2)]
    public async Task MinimalHostSendsRestoredCharacterAndEmptyBanksInOneUpload(int selection)
    {
        _ = ReadSource();
        var source = WorldHostSavedCharacterSource.ReadSelected(Installation, selection);
        await using var peer = await GoldLoopbackPeer.StartHostAsync(source, 0, default, null);
        peer.AllowLongPreparation();
        await Admit(peer, source.ObjectCount + 4);
        var owner = peer.HostSession!;
        var joining = owner.JoinMinimalAsync(peer.Token);
        var upload = new List<GoldMessage>();
        while (!upload.Any(message => message.Opcode == GoldWorldEntryCodec.HostStartOpcode))
            foreach (var message in await peer.ReadClientBatchAsync())
            {
                if (message.Opcode == GoldWorldEntryCodec.HostUploadOpcode || upload.Count != 0)
                    upload.Add(message);
            }

        var character = owner.MinimalCharacterPublication!;
        CollectionAssert.AreEqual(new ushort[] { 0x16 }.Concat(character.CreateMessages().Select(message => message.Opcode))
            .Concat(new ushort[] { 0x148, 0x1B2, 0x1B4, 0x1B3, 0x1B1, 0x18 }).ToArray(), upload.Select(message => message.Opcode).ToArray());
        foreach (var message in upload.Skip(upload.Count - 5).Take(4))
        {
            var bank = GoldWorldBankSnapshot.FromPayload(message.Opcode, message.Payload);
            Assert.IsTrue(bank.RemainingData.IsEmpty);
            Assert.IsTrue(bank.Sections.All(section => section.Records.Count == 0));
            if (bank.Globals is { } globals)
                CollectionAssert.AreEqual(GoldWorldRegionTable.CreateInitial().ToPayload(), globals.Table.ToArray());
        }
        Assert.IsFalse(joining.IsCompleted);
        Assert.AreEqual((byte)0, owner.Replicas.LocalPlayerSlot);
        await peer.SendAsync(new GoldMessage(0x18, 0, 1, 0, []));
        await joining;
        await owner.JoinMinimalAsync(peer.Token);
        Assert.AreEqual(WorldHostInitializationPhase.MinimalUploadApproved, owner.Phase);
        Assert.IsTrue(owner.IsCurrent);
        Assert.IsTrue(owner.CharacterPublicationSent);
        Assert.AreEqual(1u, owner.Replicas.Objects[1].BaseState.ObjectReference);
        Assert.AreEqual((byte)1, owner.Replicas.LocalPlayerSlot);
        Assert.AreEqual(owner.MinimalInitialInterest!.CurrentSector, owner.Replicas.Objects[1].BaseState.Sector);
        Assert.IsNotNull(owner.Replicas.Objects[1].Creature);
        Assert.AreEqual(1, peer.Connection.Diagnostics.Recent.Count(entry => entry.Outgoing && entry.Opcode == 0x18));
        Assert.IsFalse(owner.AdmissionReceipt!.DeliveryComplete);
        Assert.IsNull(owner.Startup);
    }

    [TestMethod]
    public async Task CharacterConsumesSharedReservationsOnceAndCannotBypassNativeActorEntry()
    {
        var source = ReadSource();
        await using var peer = await GoldLoopbackPeer.StartHostAsync(source, 0, default, null);
        peer.AllowLongPreparation();
        await Admit(peer, source.ObjectCount + 4);
        var owner = peer.HostSession!;
        var character = await owner.PrepareCharacterAsync(peer.Token);
        Assert.AreEqual(WorldHostInitializationPhase.WaitingForActorBinding, owner.Phase);
        Assert.AreEqual(4, owner.References.Available);
        Assert.AreEqual(owner.Admission.PlayerSlot, character.ReferencePlan.PlayerSlot);
        Assert.AreEqual((uint)owner.Admission.PlayerSlot, character.ReferencePlan.References[source.Records.Hero.Creature.BaseState.ObjectReference]);
        var allocated = character.ReferencePlan.References.Values.Where(reference => reference > 16).Order().ToArray();
        CollectionAssert.AreEqual(Enumerable.Range(17, source.ObjectCount).Select(value => (uint)value).ToArray(), allocated);
        Assert.AreEqual(source.ObjectCount + 3, character.Packets.MessageCount);
        Assert.IsTrue(character.IsCurrent);
        Assert.AreEqual(source.Records.Hero.Creature.BaseState.Resource, character.HeroConstruction.Template.ItemType);
        Assert.AreEqual((uint)owner.Admission.PlayerSlot, character.HeroConstruction.Actor.Base.ObjectReference);
        Assert.AreEqual(owner.Admission.PlayerSlot, character.HeroConstruction.Actor.Base.Owner);
        Assert.IsTrue(character.HeroConstruction.Actor.Node.Active);
        Assert.AreEqual(unchecked((byte)(1 + source.ObjectCount)), character.HeroConstruction.Counter.Value);
        Assert.IsTrue(character.HeroConstruction.SavedItemsRestored);
        Assert.IsTrue(character.HeroConstruction.SavedItemPostLoadCompleted);
        Assert.IsTrue(character.HeroConstruction.Actor.HeroPostLoadCompleted);
        Assert.AreEqual((ushort)0, character.HeroConstruction.Actor.KiMode);
        Assert.AreEqual((ushort)0, character.HeroConstruction.Actor.EiMode);
        Assert.AreEqual(0u, character.HeroConstruction.Actor.LocomotionAction);
        Assert.IsTrue(character.HeroConstruction.Actor.KiState.AsSpan(0, 4).SequenceEqual(new byte[4]));
        Assert.IsTrue(character.HeroConstruction.Actor.EiState.AsSpan(0, 4).SequenceEqual(new byte[4]));
        var savedItems = character.HeroConstruction.EquipmentFactory.Items.Values.ToArray();
        Assert.AreEqual(source.ObjectCount, savedItems.Length);
        Assert.IsTrue(savedItems.All(item => item.Node.Active && item.SavedState is not null && item.SavedPostLoadCompleted && !item.PacketReady));
        using (var proof = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRoot(), "docs/game-server/evidence/native-host-saved-items.json"))))
        {
            foreach (var row in proof.RootElement.GetProperty("cases").EnumerateArray())
            {
                var payload = Convert.FromHexString(row.GetProperty("source_hex").GetString()!);
                var oldReference = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(10));
                var item = character.HeroConstruction.EquipmentFactory.Items[character.ReferencePlan.References[oldReference]];
                CollectionAssert.AreEqual(payload, item.SavedState!.Source.ToPayload());
                var native = Convert.FromHexString(row.GetProperty("item_hex").GetString()!);
                CollectionAssert.AreEqual(native.AsSpan(0x118, 130).ToArray(), item.Values,
                    "Installed original definitions must produce the independently compared reader values.");
                Assert.AreEqual(BinaryPrimitives.ReadUInt32LittleEndian(native.AsSpan(4)), item.Node.PeriodMilliseconds);
            }
        }
        CollectionAssert.AreEquivalent(allocated, character.HeroConstruction.EquipmentFactory.Items.Keys.ToArray());
        Assert.AreEqual(0x80040u, character.HeroConstruction.Actor.RuntimeFlags);
        var normalized = character.HeroConstruction.Actor.RestoredCreature;
        Assert.IsNotNull(normalized);
        Assert.AreEqual(source.Records.Hero.Creature.Values.Count22, normalized.CombatArts.Count);
        Assert.AreEqual(source.Records.Hero.Creature.Values.Count38, normalized.Combos.Count);
        Assert.AreEqual((byte)1, character.HeroConstruction.Actor.Component.ResultValid);
        Assert.AreEqual(source.Records.Hero.Creature.Values.Level,
            BinaryPrimitives.ReadUInt16LittleEndian(character.HeroConstruction.Actor.Component.Values.AsSpan(78)));
        Assert.IsFalse(source.Records.Hero.Creature.Values.Block08.SequenceEqual(character.HeroConstruction.Actor.Component.Values),
            "Saved derived stats must be recalculated by native rules.");
        Assert.IsTrue(character.HeroConstruction.Actor.SavedEquipmentReferencesBound);
        Assert.IsTrue(character.HeroConstruction.Actor.SavedLoaderTailPrepared);
        var nativeHeading = Sacred.GameplayHost.Runtime.SacredStartupHeading.ToDirection(
            Sacred.Assets.Paks.Weapon.SacredObjectTemplateArchive.Load(Installation).GetItem(character.HeroConstruction.Actor.Base.ObjectType).ModelDesc.Angle3D);
        Assert.AreEqual(nativeHeading.X, character.HeroConstruction.Actor.Direction.X);
        Assert.AreEqual(nativeHeading.Y, character.HeroConstruction.Actor.Direction.Y);
        Assert.IsTrue(character.HeroConstruction.Actor.EquipmentReferences.Where(reference => reference != 0)
            .Select(reference => character.HeroConstruction.EquipmentFactory.Items[reference].Physical.Base)
            .All(state => state.Sector == 0 && state.X == 0 && state.Y == 0 && state.Surface == 0));
        var expectedEquipment = Enumerable.Range(0, Sacred.Core.Pax.Hero.Objects.SacredSavedCreatureLayout.EquipmentCount)
            .Select(slot => source.Records.Hero.Creature.GetEquipmentReference(slot))
            .Select(reference => reference == 0 ? 0 : character.ReferencePlan.References[reference]).ToArray();
        CollectionAssert.AreEqual(expectedEquipment, character.HeroConstruction.Actor.EquipmentReferences);
        var inventoryBinding = character.HeroConstruction.InventoryBinding;
        Assert.IsNotNull(inventoryBinding);
        foreach (var (reference, inventory) in inventoryBinding.Membership)
            Assert.AreEqual((uint)inventory, character.HeroConstruction.EquipmentFactory.Items[reference].InventoryId);
        using var worldProof = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRoot(), "docs/game-server/evidence/native-host-character-world-entry.json")));
        var refresh = worldProof.RootElement.GetProperty("refresh");
        CollectionAssert.AreEqual(Convert.FromHexString(refresh.GetProperty("inventories")[0].GetProperty("payload_hex").GetString()!), inventoryBinding.Primary.Inventory.ToPayload());
        Assert.AreEqual(0, inventoryBinding.Primary.Inventory.QuestCount);
        CollectionAssert.AreEqual(character.Import.Inventories.Shared.Inventory.ToPayload(), inventoryBinding.Shared.Inventory.ToPayload());
        var actorAfterEntry = character.HeroConstruction.Actor;
        Assert.IsTrue(actorAfterEntry.SavedWorldPreludeCompleted);
        Assert.IsTrue(actorAfterEntry.CampaignEntryPlaced);
        var nativeEntry = Convert.FromHexString(worldProof.RootElement.GetProperty("entry").GetProperty("actor_hex").GetString()!);
        Assert.AreEqual(BinaryPrimitives.ReadUInt16LittleEndian(nativeEntry.AsSpan(24)), actorAfterEntry.Base.Sector);
        Assert.AreEqual(BinaryPrimitives.ReadInt32LittleEndian(nativeEntry.AsSpan(28)), actorAfterEntry.Base.X);
        Assert.AreEqual(BinaryPrimitives.ReadInt32LittleEndian(nativeEntry.AsSpan(32)), actorAfterEntry.Base.Y);
        Assert.AreEqual(BinaryPrimitives.ReadUInt16LittleEndian(nativeEntry.AsSpan(0x1fc)), actorAfterEntry.Flags1FC);
        var terrain = character.HeroConstruction.Terrain;
        Assert.IsNotNull(terrain);
        Assert.AreEqual(2, terrain.LoadedSectorCount);
        var runtime = character.HeroConstruction.RuntimePublication;
        Assert.IsNotNull(runtime);
        Assert.AreEqual(source.ObjectCount + 3, runtime.MessageCount);
        var snapshots = character.HeroConstruction.ValueSnapshots;
        Assert.IsNotNull(snapshots);
        snapshots.RequireMatch(actorAfterEntry);
        using var published = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRoot(), "docs/game-server/evidence/native-host-current-publication.json")));
        CollectionAssert.AreEqual(Convert.FromHexString(published.RootElement.GetProperty("snapshots")[0].GetString()!), snapshots.First.ToArray());
        CollectionAssert.AreEqual(Convert.FromHexString(published.RootElement.GetProperty("snapshots")[1].GetString()!), snapshots.Second.ToArray());
        var currentMessages = runtime.CreateMessages();
        var nativeItems = published.RootElement.GetProperty("items").EnumerateArray().ToArray();
        for (var index = 0; index < nativeItems.Length; index++)
            CollectionAssert.AreEqual(Convert.FromHexString(nativeItems[index].GetProperty("payload_hex").GetString()!), currentMessages[index].Payload.AsSpan(8).ToArray(), $"Current native item {index}");
        var nativeMessages = published.RootElement.GetProperty("publications").EnumerateArray().ToArray();
        for (var index = 0; index < nativeMessages.Length; index++)
        {
            var expectedMessage = Convert.FromHexString(nativeMessages[index].GetProperty("prefix_hex").GetString()!)
                .Concat(Convert.FromHexString(nativeMessages[index].GetProperty("payload_hex").GetString()!)).ToArray();
            CollectionAssert.AreEqual(expectedMessage, currentMessages[source.ObjectCount + index].Payload.ToArray(), $"Current native publication {index}");
        }
        var changed = actorAfterEntry.Component.Values.ToArray(); changed[4] ^= 1;
        Assert.ThrowsException<NotSupportedException>(() => snapshots.RequireMatch(actorAfterEntry with { Component = actorAfterEntry.Component with { Values = changed } }));
        foreach (var sector in worldProof.RootElement.GetProperty("sectors").EnumerateArray())
        {
            var keyx = System.Runtime.InteropServices.MemoryMarshal.Read<Sacred.Core.World.KeyxSectorRecord>(Convert.FromHexString(sector.GetProperty("keyx_hex").GetString()!));
            var originX = Sacred.Core.World.SacredSectorCoordinates.ToTileOrigin(keyx.RawX);
            var originY = Sacred.Core.World.SacredSectorCoordinates.ToTileOrigin(keyx.RawY);
            var bytes = new byte[4096 * 32];
            for (var index = 0; index < 4096; index++)
            {
                var tile = terrain.GetLoadedTile(originX + index % 64, originY + index / 64);
                Assert.IsTrue(tile.HasValue);
                var value = tile.Value;
                System.Runtime.InteropServices.MemoryMarshal.Write(bytes.AsSpan(index * 32), in value);
            }
            CollectionAssert.AreEqual(Convert.FromHexString(sector.GetProperty("tiles_hex").GetString()!), bytes,
                "Complete native outdoor grids, including actual dynamic chains, must match.");
            var members = terrain.Members((ushort)keyx.Id);
            CollectionAssert.AreEqual(keyx.Id == actorAfterEntry.Base.Sector ? new[] { actorAfterEntry.Base.ObjectReference } : [], members.ToArray());
        }
        Assert.IsTrue(normalized.PendingEquipmentReferences.Any(reference => reference != 0));
        var model = character.HeroConstruction.ModelResources;
        Assert.IsNotNull(model);
        Assert.IsNotNull(model.Body.Mesh); Assert.IsNotNull(model.Body.Skin);
        Assert.IsNull(model.Body.DefaultAnimation, "Saved numeric selection cannot be replaced with a default animation.");
        Assert.IsTrue(model.HasRoot); Assert.IsFalse(model.IsInvalidated);
        Assert.AreEqual(System.Numerics.Vector3.Zero, model.RootLocalOffset);
        Assert.IsFalse(string.IsNullOrEmpty(model.SavedAnimation.Name));
        Assert.AreEqual(0x4000000u, character.HeroConstruction.Actor.Base.Flags & 0x4000000u,
            "Verified physical import opens a real shared pose sequence on the existing root.");
        var physical = character.HeroConstruction.Actor.RestoredPhysical;
        var bindPose = character.HeroConstruction.BindPose;
        Assert.IsNotNull(physical); Assert.IsNotNull(bindPose);
        Assert.IsTrue(character.HeroConstruction.Actor.ModelReactivated);
        Assert.IsNull(character.HeroConstruction.Animation, "Reactivation cannot keep the old control on its new body.");
        Assert.AreEqual(0u, physical.Animation);
        Assert.IsTrue(bindPose.Pose.IsEvaluated);
        Assert.IsNull(bindPose.Pose.Animation);
        var node = character.HeroConstruction.Actor.Node;
        Assert.AreSame(character.HeroConstruction, (await owner.PrepareCharacterAsync(peer.Token)).HeroConstruction);
        Assert.AreSame(model, (await owner.PrepareCharacterAsync(peer.Token)).HeroConstruction.ModelResources);
        Assert.AreSame(bindPose, (await owner.PrepareCharacterAsync(peer.Token)).HeroConstruction.BindPose);
        Assert.AreSame(terrain, (await owner.PrepareCharacterAsync(peer.Token)).HeroConstruction.Terrain);
        Assert.AreSame(runtime, (await owner.PrepareCharacterAsync(peer.Token)).HeroConstruction.RuntimePublication);
        Assert.AreSame(node, character.HeroConstruction.Actor.Node);
        Assert.AreSame(normalized, character.HeroConstruction.Actor.RestoredCreature);
        Assert.AreEqual(unchecked((byte)(1 + source.ObjectCount)), character.HeroConstruction.Counter.Value);
        Assert.IsTrue(savedItems.SequenceEqual(character.HeroConstruction.EquipmentFactory.Items.Values));
        var expectedEntry = source.StartPositions.Select(owner.Admission.PlayerSlot, true, 0);
        Assert.AreEqual(expectedEntry.X, character.EntryPosition.X);
        Assert.AreEqual(expectedEntry.Y, character.EntryPosition.Y);
        Assert.AreSame(character, await owner.PrepareCharacterAsync(peer.Token));
        Assert.AreEqual(4, owner.References.Available, "Retry cannot allocate the character twice.");
        Assert.IsFalse(owner.CharacterPublicationSent);
        actorAfterEntry.Component.Values[4] ^= 1;
        try { await Assert.ThrowsExceptionAsync<NotSupportedException>(() => owner.PublishCharacterAsync(peer.Token)); }
        finally { actorAfterEntry.Component.Values[4] ^= 1; }
        Assert.IsFalse(owner.CharacterPublicationSent);
        Assert.AreEqual(0, peer.Connection.Diagnostics.Recent.Count(row => row.Outgoing && row.Opcode is 0x16E or 0x169 or 0x122));
        Assert.AreSame(runtime, await owner.PublishCharacterAsync(peer.Token));
        var wire = new List<GoldMessage>();
        while (wire.Count < currentMessages.Count)
            wire.AddRange((await peer.ReadClientBatchAsync()).Where(message => message.Opcode is 0x16E or 0x169 or 0x122));
        Assert.AreEqual(currentMessages.Count, wire.Count);
        for (var index = 0; index < wire.Count; index++)
        {
            Assert.AreEqual(currentMessages[index].Opcode, wire[index].Opcode);
            Assert.AreEqual(owner.Admission.PlayerSlot, wire[index].Sender);
            Assert.AreEqual(owner.Admission.PlayerSlot, wire[index].Origin);
            CollectionAssert.AreEqual(currentMessages[index].Payload, wire[index].Payload, $"Actual ordered TCP character message {index}");
        }
        Assert.IsTrue(owner.CharacterPublicationSent);
        var active = owner.ActiveHero;
        Assert.IsNotNull(active);
        Assert.AreEqual(WorldHostInitializationPhase.WaitingForStartupInputs, owner.Phase);
        active.RequireCurrent();
        Assert.AreSame(character.HeroConstruction.Actor, active.Actor);
        Assert.AreSame(character.HeroConstruction.Actor, active.Creatures[owner.Admission.PlayerSlot]);
        Assert.AreSame(character.HeroConstruction.EquipmentFactory.Items, active.Equipment);
        Assert.AreSame(character.HeroConstruction.Counter, active.Counter);
        Assert.AreSame(model, active.Models);
        Assert.AreSame(bindPose.Pose, active.Pose);
        Assert.AreSame(terrain, active.Terrain);
        Assert.AreEqual(source.Records.Hero.Creature.Values.Level, active.Level);
        CollectionAssert.AreEqual(character.HeroConstruction.Actor.Component.Values, active.Actor.Component.Values);
        using (var bindingProof = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRoot(), "docs/game-server/evidence/native-host-active-hero-binding.json"))))
        {
            var nativeActor = Convert.FromHexString(bindingProof.RootElement.GetProperty("actor_hex").GetString()!);
            Assert.AreEqual(BinaryPrimitives.ReadUInt32LittleEndian(nativeActor.AsSpan(12)), active.Actor.Base.ObjectReference);
            CollectionAssert.AreEqual(nativeActor.AsSpan(0x3b0, 238).ToArray(), active.Actor.Component.Values);
        }
        active.Actor.Component.Values[4] ^= 1;
        Assert.ThrowsException<NotSupportedException>(active.RequireCurrent);
        character.HeroConstruction.Actor.Component.Values[4] ^= 1;
        active.RequireCurrent();
        Assert.AreSame(runtime, await owner.PublishCharacterAsync(peer.Token));
        Assert.AreSame(active, owner.ActiveHero);
        Assert.AreEqual(currentMessages.Count, peer.Connection.Diagnostics.Recent.Count(row => row.Outgoing && row.Opcode is 0x16E or 0x169 or 0x122));
        await peer.SendAsync(Words(0x153, Enumerable.Range(17 + source.ObjectCount + 4, 3000 - source.ObjectCount - 4).Select(value => (uint)value).ToArray()));
        while (owner.References.Issued < 3000) await Task.Delay(5, peer.Token);
        var startupSeed = active.RandomState;
        var startup = await owner.InitializeAsync(Installation, SacredCampaignFiles.Resolve(Installation), peer.Token);
        Console.WriteLine($"Entered host startup: commands {startup.Execution.ExecutedCommands}, complete {startup.Execution.StartupCompleted}, phase {owner.Phase}; failure {startup.Execution.Failure?.Message}");
        Assert.AreSame(startup, owner.Startup);
        Assert.AreEqual(WorldHostInitializationPhase.StartupComplete, owner.Phase);
        Assert.AreEqual(true, startup.Execution.HasActiveHero);
        Assert.IsTrue(startup.Execution.ExecutedCommands > 0);
        Assert.IsTrue(startup.Execution.StartupCompleted);
        Assert.IsNull(startup.Execution.Failure);
        Assert.AreSame(active.Creatures, startup.Objects.Creatures!.Factory.Actors);
        Assert.AreSame(active.Equipment, startup.Objects.Creatures.EquipmentFactory.Items);
        Assert.AreSame(active.Counter, startup.Objects.AllocationCounter);
        CollectionAssert.AreEqual(terrain.Members(active.Actor.Base.Sector).ToArray(),
            startup.Objects.Creatures.Terrain!.Membership.Get(active.Actor.Base.Sector).ToArray());
        Assert.AreEqual(0x80000000u, owner.StartupRequest!.EngineMode);
        Assert.IsTrue(owner.StartupRequest.LoadsStartCode);
        using (var automaticProof = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRoot(), "docs/game-server/evidence/native-host-automatic-level-source.json"))))
        {
            CollectionAssert.AreEqual(Convert.FromHexString(automaticProof.RootElement.GetProperty("scratch_hex").GetString()!), active.Actor.Scratch);
            var firstAutomatic = startup.Objects.Creatures.Prepared.Single(value => value.Blueprint.SourceOffset == 0x697).Actor!;
            Assert.AreEqual(automaticProof.RootElement.GetProperty("level_calls")[0][0].GetUInt16(),
                BinaryPrimitives.ReadUInt16LittleEndian(firstAutomatic.Component.Values.AsSpan(78)));
        }
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => owner.InitializeAsync(Installation, SacredCampaignFiles.Resolve(Installation), peer.Token));
        Assert.AreEqual(currentMessages.Count, peer.Connection.Diagnostics.Recent.Count(row => row.Outgoing && row.Opcode is 0x16E or 0x169 or 0x122));
        Assert.IsTrue(startup.Objects.HasActiveHero, "Startup must retain the actual hero instead of selecting the absent-hero diagnostic.");
        var worldReferences = active.Creatures.Keys.Concat(active.Equipment.Keys)
            .Concat(startup.Objects.Objects.Select(value => value.BaseState.ObjectReference))
            .Concat(startup.Objects.Creatures.Effects?.Created.Select(value => value.BaseState.ObjectReference) ?? [])
            .Where(reference => reference > 16).Distinct().ToArray();
        Assert.AreEqual(3000 - worldReferences.Length, owner.References.Available);
        Assert.AreEqual(unchecked((byte)(1 + worldReferences.Length)), active.Counter.Value);
        Assert.AreEqual(active.RandomState, startup.Objects.Creatures.Random.State);
        Assert.AreNotEqual(startupSeed, active.RandomState);
        Assert.IsTrue(startup.Objects.Objects.Count > 0);
        foreach (var created in startup.Objects.Objects)
        {
            CollectionAssert.AreEqual(created.GetAuthoredInstance(), created.CurrentValues.ToArray());
            Assert.IsFalse(created.PacketReady);
            Assert.AreEqual(0x10000u, created.BaseState.Flags & 0x10000u, "Unpublished runtime flags survive without a writer.");
        }
        var groundContainer = startup.Objects.Objects.Single(value => value.SourceOffset == 0x466A8);
        Assert.IsTrue(groundContainer.Node.Active);
        Assert.AreEqual(200000u, groundContainer.Node.PeriodMilliseconds);
        var indoorContainer = startup.Objects.Objects.Single(value => value.SourceOffset == 0x4C032);
        Assert.IsTrue(indoorContainer.Node.Active);
        Assert.AreNotEqual(0u, indoorContainer.StaticSurfaceReference);
        var startupWeapon = startup.Objects.Creatures.Items.Created.Single(value => value.SourceOffset == 0x72310).Item;
        Assert.IsTrue(startupWeapon.Generated);
        Assert.IsNotNull(startupWeapon.Name);
        Assert.AreEqual(startupWeapon.Name.NativeReference, startupWeapon.Physical.Base.Word3C);
        Assert.IsFalse(startupWeapon.Node.Active);
        Assert.IsNull(owner.RegionInitialization);
        Assert.AreEqual(0, owner.Replicas.Objects.Count, "Packet preparation must not invent a runtime actor registry entry.");
        Assert.AreEqual((byte)0, owner.Replicas.LocalPlayerSlot);
        Assert.IsFalse(owner.AdmissionReceipt!.DeliveryComplete);
        Assert.IsTrue(await owner.ContinueCampaignAsync(peer.Token));
        Assert.AreEqual(WorldHostInitializationPhase.CampaignComplete, owner.Phase);
        Assert.IsTrue(startup.Execution.PostStartup.IsCompleted);
        Assert.AreEqual((uint)owner.Admission.PlayerSlot, startup.Execution.PostStartup.Invocation!.ActorReference);
        Assert.AreEqual(0u, startup.Execution.AssociatedCreatureReference);
        Assert.IsFalse(startup.Execution.IsExecutingStartup);
        Assert.IsFalse(startup.Execution.IsInvalidated);
        Assert.IsFalse(owner.AdmissionReceipt.DeliveryComplete);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => owner.ContinueCampaignAsync(peer.Token));
        var enteredBeforeReload = active.Actor;
        Assert.IsFalse(startup.Objects.Creatures.Terrain!.Tiles.IsLoaded(active.Actor.Base.Sector));
        Assert.AreEqual(true, await owner.InitializeRegionAsync(Installation,active.Actor.Base.Sector,peer.Token));
        var region = owner.RegionInitialization!;
        Assert.IsNull(region.Blocker); Assert.IsFalse(region.Lifecycle.HasPendingCallback);
        Assert.AreEqual(1,region.Lifecycle.Records.Count);
        Assert.AreEqual(enteredBeforeReload.Base,active.Actor.Base);
        CollectionAssert.AreEqual(enteredBeforeReload.KiState,active.Actor.KiState);
        CollectionAssert.AreEqual(enteredBeforeReload.EiState,active.Actor.EiState);
        Assert.AreSame(enteredBeforeReload.RestoredPhysical,active.Actor.RestoredPhysical);
        Assert.AreSame(enteredBeforeReload.Node,active.Actor.Node);
        var afterRegionCommands = startup.Execution.ExecutedCommands;
        var afterRegionPublications = region.Publications.Count;
        Assert.AreEqual(true, await owner.InitializeRegionAsync(Installation,active.Actor.Base.Sector,peer.Token));
        Assert.AreEqual(afterRegionCommands,startup.Execution.ExecutedCommands);
        Assert.AreEqual(afterRegionPublications,region.Publications.Count);
        Assert.IsTrue(owner.IsCurrent); Assert.IsFalse(owner.AdmissionReceipt.DeliveryComplete);
        var bankSources = await owner.PrepareBankSourcesAsync(peer.Token);
        var patchSource = bankSources.Single(value => value.Name == "trigger cell patches");
        Assert.IsNull(patchSource.Blocker);
        Assert.AreEqual((uint)startup.Triggers.Patches.Count,patchSource.Count);
        CollectionAssert.AreEqual(WorldScriptTriggerPatchBank.Write(startup.Triggers.Patches),patchSource.Bytes.ToArray());
        Assert.IsNotNull(bankSources.Single(value => value.Name == "runtime triggers").Blocker);
        await HostInitialSectorAssertions.RunAsync(owner,Installation,FindRoot(),peer.Token, peer);
        var worldNodes = startup.Objects.Creatures.Factory.Activation.Nodes.ToArray();
        await owner.DisposeAsync();
        Assert.IsTrue(worldNodes.All(node => !node.Active));
        Assert.AreEqual(0, startup.Objects.Creatures.Factory.Activation.Nodes.Count);
        Assert.AreEqual(0, startup.Objects.Creatures.Factory.Actors.Count);
        Assert.IsFalse(character.IsCurrent); Assert.IsNull(owner.Character);
        Assert.IsNull(owner.ActiveHero);
        Assert.ThrowsException<InvalidOperationException>(active.RequireCurrent);
        Assert.ThrowsException<InvalidOperationException>(() => _ = active.Actor);
        Assert.ThrowsException<InvalidOperationException>(() => _ = active.Pose);
        Assert.IsFalse(owner.CharacterPublicationSent);
        Assert.IsTrue(character.HeroConstruction.IsInvalidated);
        Assert.IsTrue(model.IsInvalidated);
        Assert.IsNull(character.HeroConstruction.InventoryBinding);
        Assert.IsTrue(bindPose.IsInvalidated);
        Assert.IsTrue(terrain.IsInvalidated);
        Assert.IsNull(character.HeroConstruction.RuntimePublication);
        Assert.IsNull(character.HeroConstruction.ValueSnapshots);
        Assert.ThrowsException<NotSupportedException>(() => snapshots.RequireMatch(actorAfterEntry));
        Assert.ThrowsException<InvalidOperationException>(() => terrain.GetLoadedTile(0, 0));
        Assert.ThrowsException<InvalidOperationException>(() => _ = bindPose.Pose);
        Assert.IsFalse(character.HeroConstruction.Actor.Node.Active);
        Assert.IsTrue(savedItems.All(item => !item.Node.Active));
        Assert.AreEqual(0, character.HeroConstruction.EquipmentFactory.Items.Count);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => owner.PrepareCharacterAsync());
    }

    [TestMethod]
    public async Task ShortGrantDoesNotPartlyDrainReferencesAndCanResumeAfterActualReplenishment()
    {
        var source = ReadSource(); Assert.IsTrue(source.ObjectCount > 1);
        await using var peer = await GoldLoopbackPeer.StartHostAsync(source, 0, default, null);
        await Admit(peer, source.ObjectCount - 1);
        var owner = peer.HostSession!;
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => owner.PrepareCharacterAsync(peer.Token));
        Assert.IsNull(owner.Character); Assert.IsNull(owner.Startup);
        Assert.AreEqual(source.ObjectCount - 1, owner.References.Available);
        await peer.SendAsync(Words(0x153, [(uint)(17 + source.ObjectCount - 1)]));
        while (owner.References.Available < source.ObjectCount) await Task.Delay(5, peer.Token);
        var prepared = await owner.PrepareCharacterAsync(peer.Token);
        Assert.IsTrue(prepared.IsCurrent); Assert.AreEqual(0, owner.References.Available);
        Assert.IsTrue(owner.IsCurrent); Assert.IsFalse(owner.AdmissionReceipt!.DeliveryComplete);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SacredItemSimulator.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root unavailable.");
    }

    private static WorldHostSavedCharacterSource ReadSource()
    {
        if (!Directory.Exists(Installation)) Assert.Inconclusive("Installed original Gold files are required.");
        return Source.Value;
    }
    private static async Task Admit(GoldLoopbackPeer peer, int references)
    {
        await peer.SendAsync(new(0x11, 0, 0, 1, [1, 1, 0, 0, 0, 0]), Words(0x1e, [0x110]),
            new(0x1b, 0, 0, 1, HostPlayerCountChecks.WireExample(FindRoot())),
            Words(0x153, Enumerable.Range(17, references).Select(value => (uint)value).ToArray()), Words(0x154, [2269, 2000]));
        await peer.HostSession!.WaitForReservationsAsync(peer.Token);
    }
    private static GoldMessage Words(ushort opcode, uint[] words)
    {
        var bytes = new byte[words.Length * 4];
        for (var index = 0; index < words.Length; index++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * 4), words[index]);
        return new(opcode, 0, 0, 1, bytes);
    }
}
