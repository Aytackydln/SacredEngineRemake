using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.World;
using Sacred.Core.World.Pathing;
using Sacred.Core.World.Sector;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;
using Sacred.World.Networking;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Packet")]
public sealed class TriggerPatchTransportTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    [TestMethod]
    public async Task TcpMappingAndPositionDuplicatesEndTheWholeBatchInNativeOrder()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        var first = TriggerPatchChecks.Message(3, new GoldTriggerPatchCell(10,11,0), new GoldTriggerPatchCell(10,11,256), new GoldTriggerPatchCell(12,11,2));
        await peer.SendAsync(first); GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        var state = world.WorldBanks.TriggerPatches;
        Assert.AreEqual(2, state.Mappings.Count, "The second mapping changes before the position duplicate exits.");
        Assert.AreEqual(1, state.Positions.Count); Assert.AreEqual(1, state.PendingTileCount);
        Assert.IsFalse(state.Mappings.ContainsKey(new GoldTriggerPatchCell(12,11,2).Key));
        var next = TriggerPatchChecks.Message(2, new GoldTriggerPatchCell(10,11,1), new GoldTriggerPatchCell(13,11,0));
        await peer.SendAsync(next); GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        Assert.AreEqual(1, state.Positions.Count, "Surface 0/1 shares the key, so the second packet ends immediately.");
    }
    [TestMethod]
    public async Task TcpShortFixedArrayRejectsWholeBatchEvenWhenDeclaredCountIsZero()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        await peer.SendAsync(TriggerPatchChecks.Message(1, new GoldTriggerPatchCell(10,11,0)), new GoldMessage(0x1ce,0,1,0,new byte[1207]));
        var received = await peer.ReceiveAsync();
        Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(received.Messages, world.Apply));
        Assert.AreEqual(0L,world.Revision); Assert.AreEqual(0,world.WorldBanks.TriggerPatches.Positions.Count);
    }
    [TestMethod]
    public void BanksReplaceIndependentTablesButZeroCountsPreserveThem()
    {
        var world = new GoldWorldReplica(); world.Apply(GoldGameEventDecoder.Decode(TriggerPatchChecks.Message(1,new GoldTriggerPatchCell(10,11,0))));
        var state = world.WorldBanks.TriggerPatches;
        world.Apply(GoldGameEventDecoder.Decode(TriggerPatchChecks.MapBank((99,4),(99,5))));
        Assert.AreEqual(1,state.Mappings.Count); Assert.AreEqual(4u,state.Mappings[99]); Assert.AreEqual(2,state.MappingEntries.Count); Assert.AreEqual(1,state.Positions.Count);
        var bytes = new byte[16]; bytes[0]=7; bytes[2]=0xfe; BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4),20);
        world.Apply(GoldGameEventDecoder.Decode(TriggerPatchChecks.PositionBank(bytes)));
        Assert.AreEqual((ushort)7,state.Positions.Single().Sector); Assert.AreEqual(0,state.IncrementalPositions.Count);
        Assert.AreEqual(1,state.PendingTileCount,"Earlier live tile marks still need application after a later bank.");
        world.Apply(GoldGameEventDecoder.Decode(TriggerPatchChecks.MapBank()));
        world.Apply(GoldGameEventDecoder.Decode(TriggerPatchChecks.PositionBank([])));
        Assert.AreEqual(4u,state.Mappings[99]); CollectionAssert.AreEqual(bytes,state.Positions.Single().ToPayload());
    }
    [TestMethod]
    public void LateSectorBindingResolvesOnlyNewPositionsAndDisconnectDropsWork()
    {
        var world = new GoldWorldReplica(); world.Apply(GoldGameEventDecoder.Decode(TriggerPatchChecks.Message(2,new GoldTriggerPatchCell(10,11,0),new GoldTriggerPatchCell(70,11,0))));
        var state=world.WorldBanks.TriggerPatches; Assert.AreEqual(2,state.PendingRoutingCount);
        state.ConfigureSectors(MovementTriggerInsertionChecks.Index());
        CollectionAssert.AreEqual(new ushort[]{1,2},state.Positions.Select(p=>p.Sector).ToArray()); Assert.AreEqual(0,state.PendingRoutingCount);
        CollectionAssert.AreEqual(state.Positions.ToArray(),state.IncrementalPositions.ToArray());
        world.Clear(); state.ConfigureSectors(MovementTriggerInsertionChecks.Index([]));
        Assert.AreEqual(0,state.Mappings.Count);Assert.AreEqual(0,state.Positions.Count);Assert.AreEqual(0,state.PendingTileCount);
    }
    [TestMethod]
    [DataRow(-1, true, false)]
    [DataRow(0, true, false)]
    [DataRow(1, true, true)]
    [DataRow(2, false, false)]
    [DataRow(256, false, false)]
    [DataRow(65536, false, false)]
    public void LiveMarkUsesRawSignedSurfaceAndSessionCopies(int surface,bool outdoor,bool firstFloor)
    {
        var world=new GoldWorldReplica();world.Apply(GoldGameEventDecoder.Decode(TriggerPatchChecks.Message(1,new GoldTriggerPatchCell(10,11,surface))));
        var source=SectorPatchChecks.Sector();source.Pathing[20,20]=new(WorldPathFlags.Indoor,default);
        source.TileAnchorDeltas![10,11]=(10,9);source.StaticChainHeads![20,20]=1;
        var floor=new WorldPathingLayer(64,64);
        source.IndoorTileGroups.Add(new(new(source.Coord,0),0,0,64,64,default,1,floor,new(64,64),[]));
        using var fixture=JsonDocument.Parse(File.ReadAllText(RepositoryFiles.Evidence("native-sector-patches.json")));
        var records=fixture.RootElement.GetProperty("static_records_hex").EnumerateArray()
            .Select(row=>MemoryMarshal.Read<StaticObjectRecord>(Convert.FromHexString(row.GetString()!))).ToArray();
        var copiesType=typeof(SacredNetworkSectorPatches).Assembly.GetType("Sacred.World.Networking.SacredNetworkSectorCopies",true)!;
        var copies=Activator.CreateInstance(copiesType)!;
        var copy=(Sector)copiesType.GetMethod("Create")!.Invoke(copies,[source])!;
        copiesType.GetMethod("Synchronize")!.Invoke(copies,[copy]);
        var service=Service(world,id=>id is >=1 and <=4?records[id-1]:null);
        Apply(service,new Dictionary<ushort,Sector>{[1]=copy});
        Assert.AreEqual(outdoor?WorldPathFlags.Trigger:0,copy.Pathing[10,11].Flags);
        Assert.AreEqual(firstFloor?WorldPathFlags.Trigger:0,copy.IndoorTileGroups.Groups[0].Pathing[10,11].Flags);
        Assert.AreEqual((WorldPathFlags)0,source.Pathing[10,11].Flags);Assert.AreEqual((WorldPathFlags)0,floor[10,11].Flags);
        Assert.AreEqual(0,world.WorldBanks.TriggerPatches.PendingTileCount);
    }
    [TestMethod]
    public async Task TerrainWaitRetainsTheCellAndLoadsOnlyOnce()
    {
        var world=new GoldWorldReplica();world.Apply(GoldGameEventDecoder.Decode(TriggerPatchChecks.Message(1,new GoldTriggerPatchCell(10,11,0))));
        var assembly=typeof(SacredNetworkSectorPatches).Assembly;
        var terrainType=assembly.GetType("Sacred.World.Networking.SacredNetworkPatchTerrain",true)!;
        var copies=Activator.CreateInstance(assembly.GetType("Sacred.World.Networking.SacredNetworkSectorCopies",true)!)!;
        var loaded=new TaskCompletionSource<Sector?>();var loads=0;
        Func<SectorCoord,Task<Sector?>> load=_=>{loads++;return loaded.Task;};
        Func<ushort,SectorCoord?> coordinate=id=>id==1?new(0,0):null;
        using var terrain=(IDisposable)Activator.CreateInstance(terrainType,[load,coordinate,copies])!;
        var service=Service(world,_=>null,terrain); var visible=new Dictionary<ushort,Sector>();
        Assert.IsFalse(Apply(service,visible));Assert.IsFalse(Apply(service,visible));
        Assert.AreEqual(1,loads);Assert.AreEqual(1,world.WorldBanks.TriggerPatches.PendingTileCount);
        var source=SectorPatchChecks.Sector();loaded.SetResult(source);Assert.IsTrue(Apply(service,visible));
        Assert.AreEqual(0,world.WorldBanks.TriggerPatches.PendingTileCount);
        var future=await (Task<Sector?>)terrainType.GetMethod("LoadVisible")!.Invoke(terrain,[(ushort)1])!;
        Assert.AreEqual(WorldPathFlags.Trigger,future!.Pathing[10,11].Flags);Assert.AreEqual((WorldPathFlags)0,source.Pathing[10,11].Flags);
    }
    [TestMethod]
    public void LaterSectorPassUsesTheStoredByteSurface()
    {
        var world=new GoldWorldReplica();world.Apply(GoldGameEventDecoder.Decode(TriggerPatchChecks.Message(1,new GoldTriggerPatchCell(10,11,256))));
        var service=Service(world,_=>null);var sector=SectorPatchChecks.Sector();var sectors=new Dictionary<ushort,Sector>{[1]=sector};
        Apply(service,sectors);Assert.AreEqual((WorldPathFlags)0,sector.Pathing[10,11].Flags);
        var bank3=GoldWorldBankSnapshot.FromServerPayload(0x1b3,new byte[40]);
        var bank1=GoldWorldBankSnapshot.FromServerPayload(0x1b1,ScriptResourceChecks.Bank(0x1b1,2,[]).Payload);
        Assert.IsTrue((bool)typeof(SacredNetworkSectorPatches).GetMethod("Apply",Hidden)!.Invoke(service,[sector,sectors,bank3,bank1])!);
        Assert.AreEqual(WorldPathFlags.Trigger,sector.Pathing[10,11].Flags);
    }
    [TestMethod]
    public void RetainedWireWinsOverForgedFieldsAndCopiesAreImmutable()
    {
        var world=new GoldWorldReplica();var wire=TriggerPatchChecks.Message(1,new GoldTriggerPatchCell(10,11,0));
        world.Apply(new GoldTriggerPatchEvent(wire,0,999,[]));var state=world.WorldBanks.TriggerPatches;
        Assert.AreEqual(500u,state.Mappings[new GoldTriggerPatchCell(10,11,0).Key]);
        var bytes=state.Positions.Single().ToPayload();bytes[4]=0;Assert.AreEqual(10,state.Positions.Single().X);
        Assert.ThrowsException<InvalidOperationException>(()=>state.CompleteTile(new GoldTriggerPatchCell(99,99,0)));
    }
    [TestMethod]
    [DataRow(int.MinValue,0)]
    [DataRow(-1,0)]
    [DataRow(101,100)]
    public void SignedCountClampsAfterFullArrayDecode(int count,int active)
    {
        var entry=(GoldTriggerPatchEvent)GoldGameEventDecoder.Decode(TriggerPatchChecks.Message(count));
        Assert.AreEqual(count,entry.DeclaredCount);Assert.AreEqual(active,entry.ActiveCount);Assert.AreEqual(100,entry.Cells.Count);
    }
    private static SacredNetworkSectorPatches Service(GoldWorldReplica world,Func<uint,StaticObjectRecord?> read,object? terrain=null)
    {
        var service=(SacredNetworkSectorPatches)Activator.CreateInstance(typeof(SacredNetworkSectorPatches),Hidden,null,[read,terrain],null)!;
        typeof(SacredNetworkSectorPatches).GetMethod("BindTriggerPatches",Hidden)!.Invoke(service,[world.WorldBanks.TriggerPatches]);return service;
    }
    private static bool Apply(SacredNetworkSectorPatches service,IReadOnlyDictionary<ushort,Sector> sectors)
    {
        Func<ushort,SectorCoord?> coordinate=id=>id==1?new(0,0):null;
        return (bool)typeof(SacredNetworkSectorPatches).GetMethod("ApplyReceived",Hidden)!.Invoke(service,[sectors,coordinate])!;
    }
}
