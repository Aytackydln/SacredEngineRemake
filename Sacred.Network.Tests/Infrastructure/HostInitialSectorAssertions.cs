using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.GameplayHost.Networking;
using System.Buffers.Binary;
using Sacred.Network.Gameplay;

namespace Sacred.Network.Tests.Infrastructure;

internal static class HostInitialSectorAssertions
{
    public static async Task RunAsync(WorldHostInitializationSession session, string installation, string root,
        CancellationToken token, GoldLoopbackPeer peer)
    {
        var startup=session.Startup!;var context=startup.Execution;var hero=session.ActiveHero!;
        var actor=hero.Actor;var before=context.ExecutedCommands;var grants=session.Replicas.TriggerReferences.Available;
        var objects=session.References.Available;
        await Assert.ThrowsExceptionAsync<NotSupportedException>(()=>session.InitializeSectorAsync(installation,actor.Base.Sector,token));
        Assert.AreEqual(before,context.ExecutedCommands);Assert.AreEqual(grants,session.Replicas.TriggerReferences.Available);
        var interest=session.RequestInitialInterestAsync(installation,token);
        GoldSectorInterestRequest? request=null;
        while(request is null)
            foreach(var message in await peer.ReadClientBatchAsync())
                if(message.Opcode==0x148) request=GoldSectorInterestRequest.FromPayload(message.Payload);
        Assert.AreEqual((uint)objects,request.FreeReferenceCount);
        var neighborhood=Enumerable.Range(0,9).Select(request.GetEnteredSector).Where(value=>value!=0).ToArray();
        // Explicit controlled server profile: center first. Original-server order
        // is separately logged and starts with a neighbor; production cannot skip it.
        var reply=new byte[62];var ordered=new[]{actor.Base.Sector}.Concat(neighborhood.Where(value=>value!=actor.Base.Sector)).ToArray();
        for(var index=0;index<ordered.Length;index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(reply.AsSpan(4+index*2),ordered[index]);
            reply[40+index]=1;reply[49+index]=1;
        }
        await peer.SendAsync(new Sacred.Network.Protocol.GoldMessage(0x148,0,0,1,reply));await interest;
        await session.RequestInitialInterestAsync(installation,token);
        Assert.AreEqual(1,peer.Connection.Diagnostics.Recent.Count(row=>row.Outgoing && row.Opcode==0x148));
        using var proof=HostSectorInitializationChecks.Read(root,"native-host-sector-init-effects");
        var native=proof.RootElement.GetProperty("cases")[0];
        var terrain=startup.Objects.Creatures!.Terrain!;
        var cells=native.GetProperty("marks").EnumerateArray().Select(row=>terrain.Tiles.GetCell(row[0].GetInt32(),row[1].GetInt32())!.Value).ToArray();
        var tiles=cells.Select(cell=>cell.Bytes.AsSpan(cell.Offset,32).ToArray()).ToArray();
        Assert.AreEqual(true,await session.InitializeSectorAsync(installation,actor.Base.Sector,token));
        var work=session.RegionInitialization!.InitialSector!;
        Assert.IsTrue(work.IsCompleted);Assert.IsNull(work.Blocker);
        Assert.IsNotNull(work.Instruction);Assert.AreEqual(GoldSectorWorkState.Executing,work.Instruction.State);
        Assert.IsFalse(session.Replicas.SectorWork.IsComplete,"Init callback alone cannot finish Enter/patch/notification work.");
        CollectionAssert.AreEqual(HostSectorInitializationChecks.Bytes(native,"environment_after_hex"),work.Environment.ToPayload());
        Assert.AreEqual(before+7,context.ExecutedCommands);
        Assert.AreEqual(grants-3,session.Replicas.TriggerReferences.Available);
        var beforeEntry=context.ExecutedCommands;
        Assert.AreEqual(true,await work.ContinueRegionEntryAsync(token));
        var entry=session.RegionInitialization.Entry!;
        Assert.IsFalse(entry.IsPending);Assert.IsFalse(entry.IsEntering);
        Assert.AreEqual(0,entry.PendingFunctions.Count);
        Assert.AreEqual(0x101u,session.RegionInitialization.Lifecycle.Records[1].Tag);
        Assert.IsNull(work.EnterRegionBlocker);
        Assert.IsTrue(context.ExecutedCommands>beforeEntry,"Actual region body must execute before alive is marked.");
        var afterEntry=context.ExecutedCommands;
        var variablePublications=context.Variables.Publications.Count;
        Assert.AreEqual(false,await work.ContinueRegionEntryAsync(token));
        Assert.AreEqual(afterEntry,context.ExecutedCommands,"Already alive native entry cannot repeat callback effects.");
        Assert.AreEqual(variablePublications,context.Variables.Publications.Count);
        Assert.IsFalse(context.IsEnteringRegion);Assert.IsFalse(session.Replicas.SectorWork.IsComplete);
        Assert.AreEqual(objects,session.References.Available);
        var publications=startup.Triggers.Publications;
        Assert.AreEqual(6,publications.Count);
        for(var index=0;index<publications.Count;index++)
        {
            // Compare each native creation/patch, rather than repeating the first pair.
            var nativeMessage=native.GetProperty("publications")[index];
            var bytes=HostSectorInitializationChecks.Bytes(nativeMessage,"payload_hex");
            var used=publications[index].Opcode==0x1ae ? bytes.Length : 20;
            CollectionAssert.AreEqual(bytes.AsSpan(0,used).ToArray(),publications[index].Payload.AsSpan(0,used).ToArray());
        }
        for(var index=0;index<cells.Length;index++)
        {
            tiles[index][0x1e]|=4;
            CollectionAssert.AreEqual(tiles[index],cells[index].Bytes.AsSpan(cells[index].Offset,32).ToArray());
        }
        Assert.AreSame(actor,hero.Actor);
        Assert.IsFalse(context.IsInitializingSector);Assert.IsNull(context.CurrentSector);
        Assert.IsFalse(session.AdmissionReceipt!.DeliveryComplete);
        var entryRng=startup.Objects.Creatures!.Random.State;
        Assert.IsFalse(await work.ContinueSectorEntryAsync(token),"The center-first fixture's unfiltered ordinary NPCs must remain pending.");
        Assert.IsNotNull(work.SectorEntry!.Blocker);
        Assert.IsFalse(await work.ContinueSectorEntryAsync(token));
        Assert.AreEqual(entryRng,startup.Objects.Creatures.Random.State,"Whole-body preflight cannot consume facing draws.");
        Assert.AreEqual(afterEntry,context.ExecutedCommands);
        Assert.AreEqual(0,startup.Objects.Creatures.SkippedEntryCreatures);
        session.RegionInitialization.Lifecycle.CurrentRegion=0xaabbccddu;
        Assert.AreEqual(true,await session.InitializeSectorAsync(installation,actor.Base.Sector,token));
        Assert.AreEqual(0xaabbccddu,session.RegionInitialization.Lifecycle.CurrentRegion,"Completed Init inspection cannot mutate the region temporary.");
        Assert.AreEqual(afterEntry,context.ExecutedCommands);Assert.AreEqual(6,publications.Count);
        Assert.AreEqual(grants-3,session.Replicas.TriggerReferences.Available);
    }
}
