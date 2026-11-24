using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.World;
using Sacred.GameplayHost.Networking;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests;

/// <summary>Installed sources, actual shared host factories and a controlled
/// neighbor-first TCP instruction. Original-server acceptance is separate.</summary>
[TestClass, TestCategory("Installed")]
public sealed class HostSectorEntryInstalledTests
{
    [TestMethod]
    public async Task EnterCallbackRetainsInstructionGrantsAndActorWithActualLowBandwidthSetting()
    {
        var installation = Environment.GetEnvironmentVariable("SACRED_GAME_DIRECTORY") ?? @"E:\SteamLibrary\steamapps\common\Sacred Gold";
        if (!File.Exists(Path.Combine(installation, "Sacred.exe"))) Assert.Inconclusive("Installed Gold data required.");
        var source = WorldHostSavedCharacterSource.ReadSelected(installation, 1);
        await using var peer = await GoldLoopbackPeer.StartHostAsync(source, 1, default, null);
        peer.AllowLongPreparation();
        await peer.SendAsync(new(0x11, 0, 0, 1, [1, 1, 0, 0, 0, 0]), Words(0x1e, [0x110]),
            new(0x1b, 0, 0, 1, HostPlayerCountChecks.WireExample(RepositoryFiles.Root)),
            Words(0x153, Enumerable.Range(17, 4000).Select(value => (uint)value).ToArray()), Words(0x154, [2269, 2000]));
        var session = peer.HostSession!;
        await session.WaitForReservationsAsync(peer.Token);
        await session.PublishCharacterAsync(peer.Token);
        var startup = await session.InitializeAsync(installation, SacredCampaignFiles.Resolve(installation), peer.Token);
        Assert.IsTrue(startup.Execution.StartupCompleted);
        Assert.IsTrue(await session.ContinueCampaignAsync(peer.Token));
        var interest = session.RequestInitialInterestAsync(installation, peer.Token);
        GoldSectorInterestRequest? request = null;
        while (request is null)
            foreach (var message in await peer.ReadClientBatchAsync())
                if (message.Opcode == 0x148) request = GoldSectorInterestRequest.FromPayload(message.Payload);
        // Confirm the whole requested neighborhood, with the first neighbor
        // ordered first. The original server's exact reply is checked separately.
        var reply = new byte[62];
        var ordered = new ushort[] { 2502 }.Concat(Enumerable.Range(0, 9).Select(request.GetEnteredSector).Where(id => id != 2502)).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        { BinaryPrimitives.WriteUInt16LittleEndian(reply.AsSpan(4 + index * 2), ordered[index]); reply[40 + index] = 1; reply[49 + index] = 1; }
        await peer.SendAsync(new GoldMessage(0x148, 0, 0, 1, reply)); await interest;
        Assert.AreEqual(true, await session.InitializeSectorAsync(installation, 2502, peer.Token));
        var init = session.RegionInitialization!.InitialSector!;
        Assert.AreEqual(0, startup.SectorRestoreSource!.Count);
        await Assert.ThrowsExceptionAsync<NotSupportedException>(() => init.ContinueFinalPatchesAsync(peer.Token));
        await Assert.ThrowsExceptionAsync<NotSupportedException>(() => init.ContinueCompletionAsync(peer.Token));
        await Assert.ThrowsExceptionAsync<NotSupportedException>(() => init.ContinueRestorationAsync(peer.Token));
        await Assert.ThrowsExceptionAsync<NotSupportedException>(() => init.ContinueSectorEntryAsync(peer.Token));
        Assert.AreEqual(true, await init.ContinueRegionEntryAsync(peer.Token));
        var actor = session.ActiveHero!.Actor;
        var references = session.References.Available;
        var triggers = session.Replicas.TriggerReferences.Available;
        var nodes = startup.Objects.Creatures!.Factory.Activation.Nodes.Count;
        var seed = session.ActiveHero.RandomState;
        var before = startup.Execution.ExecutedCommands;
        Assert.IsTrue(await init.ContinueSectorEntryAsync(peer.Token));
        Assert.IsTrue(init.SectorEntry!.IsCompleted); Assert.IsNull(init.SectorEntry.Blocker);
        Assert.AreEqual((ushort)17004, init.SectorEntry.FunctionIndex);
        Assert.AreEqual((byte)1, session.NetworkSpeedSettings);
        Assert.AreEqual(3, startup.Objects.Creatures.SkippedEntryCreatures);
        Assert.AreEqual(2, init.SectorEntry.PopulationCommands);
        Assert.AreEqual(1, startup.Execution.SpawnEquipment.Count);
        var expected = new Sacred.Core.Runtime.SacredRandomSequence(seed);
        for (var index = 0; index < 3; index++) _ = expected.Next();
        Assert.AreEqual(expected.State, session.ActiveHero.RandomState);
        Assert.AreEqual(before + 7, startup.Execution.ExecutedCommands);
        var environment = init.Environment.ToPayload();
        Assert.IsTrue(await init.ContinueSectorEntryAsync(peer.Token));
        CollectionAssert.AreEqual(environment, init.Environment.ToPayload());
        Assert.AreEqual(expected.State, session.ActiveHero.RandomState);
        Assert.AreEqual(before + 7, startup.Execution.ExecutedCommands);
        Assert.AreEqual(references, session.References.Available); Assert.AreEqual(triggers, session.Replicas.TriggerReferences.Available);
        Assert.AreEqual(nodes, startup.Objects.Creatures.Factory.Activation.Nodes.Count);
        Assert.AreSame(actor, session.ActiveHero.Actor);
        Assert.AreEqual(GoldSectorWorkState.Executing, init.Instruction!.State);
        Assert.IsFalse(session.Replicas.SectorWork.IsComplete); Assert.IsFalse(session.AdmissionReceipt!.DeliveryComplete);
        Assert.IsFalse(startup.Execution.IsEnteringSector); Assert.IsNull(startup.Execution.CurrentSector);
        Assert.IsNull(startup.Execution.LocalNetworkSpeedSettings); Assert.IsFalse(startup.Execution.IsInvalidated);
        // Controlled module input for the permanent installed fixture; the live
        // driver leaves the actual shared sequence untouched.
        startup.Objects.Creatures.Random.Reset(0);
        await Assert.ThrowsExceptionAsync<NotSupportedException>(() => init.ContinueRestorationAsync(peer.Token));
        Assert.IsTrue(await init.ContinueAmbientAsync(peer.Token));
        var ambient = init.AmbientPopulation!;
        Assert.IsTrue(ambient.IsCompleted); Assert.AreEqual(2, ambient.Created);
        Assert.AreEqual(ambient.Created, ambient.Publications.Count);
        Assert.AreEqual(references - ambient.Created, session.References.Available);
        Assert.AreEqual(nodes + ambient.Created, startup.Objects.Creatures.Factory.Activation.Nodes.Count);
        Assert.IsTrue(ambient.References.All(reference => startup.Objects.Creatures.Factory.Actors[reference].Node.Active));
        Assert.IsTrue(ambient.References.All(reference => startup.Objects.Creatures.Terrain!.Membership.Get(2502).Contains(reference)));
        var published = new List<GoldMessage>();
        while (published.Count < ambient.Created)
            published.AddRange((await peer.ReadClientBatchAsync()).Where(message => message.Opcode == 0x16f));
        Assert.AreEqual(ambient.Created, published.Count);
        for (var index = 0; index < published.Count; index++) CollectionAssert.AreEqual(ambient.Publications[index].Payload, published[index].Payload);
        var sends = peer.Connection.Diagnostics.Recent.Count(row => row.Outgoing && row.Opcode == 0x16f);
        var afterAmbient = startup.Objects.Creatures.Random.State;
        Assert.IsTrue(await init.ContinueAmbientAsync(peer.Token));
        Assert.AreEqual(afterAmbient, startup.Objects.Creatures.Random.State);
        Assert.AreEqual(2, ambient.Created);
        Assert.AreEqual(sends, peer.Connection.Diagnostics.Recent.Count(row => row.Outgoing && row.Opcode == 0x16f));
        Assert.AreEqual(before + 7, startup.Execution.ExecutedCommands);
        Assert.IsFalse(session.Replicas.SectorWork.IsComplete); Assert.IsFalse(session.AdmissionReceipt.DeliveryComplete);
        var availableAfterAmbient = session.References.Available;
        Assert.IsTrue(await init.ContinueRestorationAsync(peer.Token));
        var restoration = init.Restoration!;
        Assert.IsTrue(restoration.IsCompleted); Assert.AreEqual(0, restoration.SourceCount); Assert.AreEqual(0, restoration.RestoredCount);
        Assert.IsTrue(await init.ContinueRestorationAsync(peer.Token)); Assert.AreSame(restoration, init.Restoration);
        Assert.AreEqual(afterAmbient, startup.Objects.Creatures.Random.State);
        Assert.AreEqual(availableAfterAmbient, session.References.Available);
        Assert.AreEqual(sends, peer.Connection.Diagnostics.Recent.Count(row => row.Outgoing && row.Opcode == 0x16f));
        Assert.AreEqual(GoldSectorWorkState.Executing, init.Instruction.State);
        Assert.IsFalse(session.Replicas.SectorWork.IsComplete); Assert.IsFalse(session.AdmissionReceipt.DeliveryComplete);
        var pendingMouseovers = startup.Execution.Mouseovers!.Pending.Where(value => value.X / 64 == init.Environment.Coordinate.X && value.Y / 64 == init.Environment.Coordinate.Y).ToArray();
        var pendingTriggers = startup.Triggers.PendingPositions.Where(value => value.Position.X / 64 == init.Environment.Coordinate.X && value.Position.Y / 64 == init.Environment.Coordinate.Y).ToArray();
        Assert.IsTrue(await init.ContinueFinalPatchesAsync(peer.Token));
        var patches = init.FinalPatches!;
        Assert.IsTrue(patches.IsCompleted);
        Assert.AreEqual(pendingMouseovers.Length, patches.MouseoverCount); Assert.AreEqual(pendingTriggers.Length, patches.TriggerPositionCount);
        foreach (var position in pendingTriggers.Where(value => value.Position.Z == 0))
            Assert.AreEqual(4, (int)startup.Objects.Creatures.Terrain!.Tiles.GetTile(position.Position.X, position.Position.Y)!.Value.PathFlags & 4);
        foreach (var patch in pendingMouseovers.Where(value => value.Surface == 0))
            Assert.AreEqual(128, (int)startup.Objects.Creatures.Terrain!.Tiles.GetTile(patch.X, patch.Y)!.Value.PathFlags & 128);
        Assert.IsTrue(await init.ContinueFinalPatchesAsync(peer.Token)); Assert.AreSame(patches, init.FinalPatches);
        Assert.AreEqual(afterAmbient, startup.Objects.Creatures.Random.State);
        Assert.AreEqual(availableAfterAmbient, session.References.Available);
        Assert.AreEqual(sends, peer.Connection.Diagnostics.Recent.Count(row => row.Outgoing && row.Opcode == 0x16f));
        Assert.AreEqual(GoldSectorWorkState.Executing, init.Instruction.State);
        Assert.IsFalse(session.Replicas.SectorWork.IsComplete); Assert.IsFalse(session.AdmissionReceipt.DeliveryComplete);
        var activationCount = startup.Objects.Creatures.Factory.Activation.ActivationCount;
        var activationRecords = startup.Objects.Creatures.Factory.Activation.Records.Count;
        var dormant = startup.Objects.Creatures.Terrain!.Membership.Get(2502)
            .Select(reference => startup.Objects.Creatures.Factory.Actors[reference]).Where(value => !value.Node.Active).ToArray();
        var retiringItems = dormant.SelectMany(value => value.EquipmentReferences).Where(value => value != 0).ToArray();
        var activeNodes = startup.Objects.Creatures.Factory.Activation.Nodes.Count;
        foreach (var reference in startup.Objects.Creatures.Terrain!.Membership.Get(2502))
        {
            var member = startup.Objects.Creatures.Factory.Actors[reference];
            if (member.Node.Active) continue;
            Console.WriteLine($"Activation member {reference}, type {member.Base.ObjectType}, position {member.Base.X}/{member.Base.Y}/{member.Base.Surface}, node {member.Node.Flags}, flags {member.Base.Flags:X8}.");
            foreach (var itemRef in member.EquipmentReferences.Where(value => value != 0))
            {
                var item = startup.Objects.Creatures.EquipmentFactory.Items[itemRef];
                Console.WriteLine($"Activation item {itemRef}, type {item.Physical.Base.ObjectType}, node {item.Node.Flags}, flags {item.Physical.Base.Flags:X8}, parent {item.Physical.AttachmentParent}, position {item.Physical.Base.X}/{item.Physical.Base.Y}/{item.Physical.Base.Surface}, sector {item.Physical.Base.Sector}.");
            }
        }
        Assert.IsTrue(await init.ContinueCompletionAsync(peer.Token));
        var completion = init.Completion!;
        Assert.IsTrue(completion.IsCompleted); Assert.AreEqual((ushort)2502, completion.Sector);
        Assert.AreEqual(GoldWeatherEventKind.EnterSector, completion.NotificationKind);
        Assert.AreEqual(ambient.Created + dormant.Length, completion.CheckedMembers);
        Assert.AreEqual(dormant.Length, completion.ActivatedActors);
        Assert.IsTrue(dormant.All(value => value.Node.Active));
        Assert.IsTrue(retiringItems.All(reference => !startup.Objects.Creatures.EquipmentFactory.Items[reference].Node.Active));
        Assert.AreEqual(GoldSectorWorkState.Completed, session.Replicas.SectorWork.Work[init.Instruction.Id].State);
        Assert.AreEqual(8, session.Replicas.SectorWork.PendingCount);
        Assert.IsTrue(session.Replicas.SectorWork.IsSectorComplete(2502));
        Assert.IsTrue(await init.ContinueCompletionAsync(peer.Token)); Assert.AreSame(completion, init.Completion);
        Assert.AreEqual(activationCount + (uint)dormant.Length, startup.Objects.Creatures.Factory.Activation.ActivationCount);
        Assert.AreEqual(activationRecords + dormant.Length, startup.Objects.Creatures.Factory.Activation.Records.Count);
        Assert.AreEqual(activeNodes + dormant.Length - retiringItems.Length, startup.Objects.Creatures.Factory.Activation.Nodes.Count);
        Assert.AreEqual(afterAmbient, startup.Objects.Creatures.Random.State);
        Assert.AreEqual(availableAfterAmbient, session.References.Available);
        Assert.AreEqual(sends, peer.Connection.Diagnostics.Recent.Count(row => row.Outgoing && row.Opcode == 0x16f));
        Assert.IsFalse(peer.Connection.Diagnostics.Recent.Any(row => row.Outgoing && row.Opcode == 0x14c));
        Assert.IsFalse(session.Replicas.SectorWork.IsComplete); Assert.IsFalse(session.AdmissionReceipt.DeliveryComplete);
        Assert.AreEqual(true, await session.InitializeSectorAsync(installation, ordered[1], peer.Token));
        var next = session.RegionInitialization.InitialSector!;
        Assert.AreNotSame(init, next); Assert.AreEqual(ordered[1], next.Environment.Sector);
        Assert.AreEqual(GoldSectorWorkState.Executing, next.Instruction!.State);
        var nextRandom = startup.Objects.Creatures.Random.State;
        var nextReferences = session.References.Available;
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => init.ContinueAmbientAsync(peer.Token));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => init.ContinueRegionEntryAsync(peer.Token));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => init.ContinueSectorEntryAsync(peer.Token));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => init.ContinueRestorationAsync(peer.Token));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => init.ContinueFinalPatchesAsync(peer.Token));
        Assert.IsTrue(await init.ContinueCompletionAsync(peer.Token));
        Assert.AreEqual(nextRandom, startup.Objects.Creatures.Random.State);
        Assert.AreEqual(nextReferences, session.References.Available);
        Assert.AreEqual(8, session.Replicas.SectorWork.PendingCount);
        Assert.IsTrue(session.Replicas.SectorWork.IsSectorComplete(2502));
        Assert.AreEqual((ushort)2503, next.Environment.Sector);
        _ = await next.ContinueRegionEntryAsync(peer.Token);
        var effects = startup.Objects.Creatures.Effects!;
        var priorEffects = effects.Created.Count;
        var beforeEnteredReferences = session.References.Available;
        var beforeEnteredNodes = startup.Objects.Creatures.Factory.Activation.Nodes.Count;
        var creatures = startup.Objects.Creatures;
        var preparedCount = creatures.Prepared.Count;
        Assert.IsTrue(await next.ContinueSectorEntryAsync(peer.Token));
        var entry = next.SectorEntry!;
        Assert.IsTrue(entry.IsCompleted);
        Assert.IsNull(entry.Blocker);
        Assert.AreEqual(priorEffects + 1, effects.Created.Count);
        var effect = effects.Created.Last();
        Assert.AreEqual((ushort)2503, effect.BaseState.Sector);
        Assert.AreEqual(180289, effect.BaseState.X); Assert.AreEqual(133385, effect.BaseState.Y);
        Assert.AreEqual(0x90000000u, effect.BaseState.Flags); Assert.AreEqual(0x10000000u, effect.State.BaseState.Flags);
        Assert.IsTrue(effect.Node.Active);
        Assert.IsTrue(startup.Objects.Creatures.Terrain.Membership.Get(2503).Contains(effect.BaseState.ObjectReference));
        Assert.AreEqual(effect.BaseState.ObjectReference, startup.Objects.Creatures.Terrain.Tiles.GetTile(3359,2485)!.Value.WorldObjectChainHeadId);
        Assert.AreEqual(preparedCount + 1, creatures.Prepared.Count);
        var npc = creatures.Prepared.Last();
        Assert.IsTrue(npc.PacketReady);
        Assert.AreEqual(0x90400000u, npc.Actor!.Base.Flags);
        Assert.IsTrue(npc.Actor.Node.Active);
        Assert.AreSame(npc.Actor, creatures.Factory.Actors[npc.Actor.Base.ObjectReference]);
        Assert.IsTrue(creatures.Terrain.Membership.Get(2503).Contains(npc.Actor.Base.ObjectReference));
        Assert.AreEqual(npc.Actor.Base.ObjectReference, creatures.Terrain.Tiles.GetTile(
            SacredWorldCoordinates.WorldToTile(npc.Actor.Base.X), SacredWorldCoordinates.WorldToTile(npc.Actor.Base.Y))!.Value.WorldObjectChainHeadId);
        Assert.AreEqual(beforeEnteredReferences - 4, session.References.Available);
        Assert.AreEqual(beforeEnteredNodes + 4, creatures.Factory.Activation.Nodes.Count);
        Assert.AreEqual(npc.RandomStateAfter, creatures.Random.State);
        Assert.IsFalse(npc.Equipment[0].Generated);
        Assert.IsTrue(npc.Equipment[1].Generated);
        CollectionAssert.AreEqual(new ushort[] { 0x16f, 0x16e, 0x16e }, npc.Publications.Select(message => message.Opcode).ToArray());
        CollectionAssert.AreEqual(new[] { npc.Actor.Base.ObjectReference, npc.Equipment[0].Physical.Base.ObjectReference,
            npc.Equipment[1].Physical.Base.ObjectReference },
            npc.Publications.Select(message => ((GoldObjectEvent)GoldGameEventDecoder.Decode(message)).BaseState.ObjectReference).ToArray());
        var expectedPublications = npc.Publications.Prepend(effect.Publication).ToArray();
        var receivedPublications = new List<GoldMessage>();
        while (receivedPublications.Count < expectedPublications.Length)
            receivedPublications.AddRange((await peer.ReadClientBatchAsync()).Where(message => message.Opcode is 0x16f or 0x16e));
        Assert.AreEqual(expectedPublications.Length, receivedPublications.Count);
        for (var index = 0; index < expectedPublications.Length; index++)
        {
            Assert.AreEqual(expectedPublications[index].Opcode, receivedPublications[index].Opcode);
            CollectionAssert.AreEqual(expectedPublications[index].Payload, receivedPublications[index].Payload);
        }
        var randomAfterEntry = creatures.Random.State;
        var sendsAfterEntry = peer.Connection.Diagnostics.Recent.Count(row => row.Outgoing && row.Opcode is 0x16f or 0x16e);
        Assert.IsTrue(await next.ContinueSectorEntryAsync(peer.Token));
        Assert.AreEqual(expectedPublications.Length, entry.SentPublications);
        Assert.AreEqual(sendsAfterEntry, peer.Connection.Diagnostics.Recent.Count(row => row.Outgoing && row.Opcode is 0x16f or 0x16e));
        Assert.AreEqual(randomAfterEntry, creatures.Random.State);
        Assert.AreEqual(beforeEnteredReferences - 4, session.References.Available);
        Assert.AreEqual(preparedCount + 1, creatures.Prepared.Count);
        Assert.AreEqual(priorEffects + 1, effects.Created.Count);
        Assert.IsNull(startup.Execution.CurrentSectorRuntimeFlags);
        Assert.IsFalse(session.AdmissionReceipt.DeliveryComplete);

        Assert.IsTrue(await next.ContinueAmbientAsync(peer.Token));
        Assert.IsTrue(await next.ContinueRestorationAsync(peer.Token));
        Assert.IsTrue(await next.ContinueFinalPatchesAsync(peer.Token));
        Assert.IsTrue(await next.ContinueCompletionAsync(peer.Token));
        Assert.IsTrue(session.Replicas.SectorWork.IsSectorComplete(2503));
        Assert.AreEqual(7, session.Replicas.SectorWork.PendingCount);
        await session.DisposeAsync();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => init.ContinueSectorEntryAsync(peer.Token));
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => init.ContinueRestorationAsync(peer.Token));
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => init.ContinueFinalPatchesAsync(peer.Token));
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => init.ContinueCompletionAsync(peer.Token));
    }
    private static GoldMessage Words(ushort opcode, uint[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var index = 0; index < values.Length; index++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * 4), values[index]);
        return new(opcode, 0, 0, 1, bytes);
    }
}
