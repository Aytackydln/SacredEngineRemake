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
public sealed class ScriptRegistrationTransportTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    [TestMethod]
    public async Task TcpRegistrationsPreserveDuplicatesPriorityAndCallbackState()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync();
        var first = Patch(0); var priority = Patch(0); priority[0] = 12; priority[15] = 128;
        var duplicate = first.ToArray(); duplicate[87] = 255;
        await peer.SendAsync(new GoldMessage(0x1cf, 0, 1, 0, new byte[60]), new GoldMessage(0x1cf, 0, 1, 1, new byte[60]),
            new GoldMessage(0x1d0, 0, 1, 2, first), new GoldMessage(0x1d0, 0, 1, 3, priority), new GoldMessage(0x1d0, 0, 1, 4, duplicate));
        var world = new GoldWorldReplica();
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        var state = world.WorldBanks.Registrations;
        Assert.AreEqual(2, state.SpawnRenewals.Count);
        Assert.AreEqual(2, state.Mouseovers.Active.Count);
        CollectionAssert.AreEqual(priority, state.Mouseovers.Active[0].ToPayload());
        CollectionAssert.AreEqual(first, state.Mouseovers.Active[1].ToPayload());
        Assert.AreEqual(2, state.PendingMouseovers.Count);
        Assert.AreEqual(0, world.CreatureControls.PendingReplies);
        Assert.AreEqual(0, world.Objects.Count);
        Assert.AreEqual(5L, world.Revision);
    }

    [TestMethod]
    [DataRow((ushort)0x1cf, 59)]
    [DataRow((ushort)0x1d0, 87)]
    public async Task ShortRegistrationRejectsWholeTcpBatch(ushort opcode, int length)
    {
        await using var peer = await GoldLoopbackPeer.StartAsync();
        await peer.SendAsync(new GoldMessage(0x1cf, 0, 1, 0, new byte[60]), new GoldMessage(opcode, 0, 1, 1, new byte[length]));
        var world = new GoldWorldReplica(); var messages = (await peer.ReceiveAsync()).Messages;
        Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(messages, world.Apply));
        Assert.AreEqual(0L, world.Revision);
        Assert.AreEqual(0, world.WorldBanks.Registrations.SpawnRenewals.Count);
        Assert.AreEqual(0, world.WorldBanks.Registrations.Mouseovers.Active.Count);
    }

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(0, true)]
    [DataRow(1, false)]
    [DataRow(1, true)]
    [DataRow(256, false)]
    [DataRow(256, true)]
    public void TileMarkUsesReceiptEnteredFlagAndFullSurface(int surface, bool entered)
    {
        var world = new GoldWorldReplica(); SetEntered(world, entered);
        var patch = Patch((ushort)surface);
        world.Apply(GoldGameEventDecoder.Decode(new(0x1d0, 0, 1, 0, patch)));
        SetEntered(world, !entered); // Frame consumption must not replace the receipt-time gate.
        var source = SectorPatchChecks.Sector();
        source.Pathing[20, 20] = new(WorldPathFlags.Indoor, default);
        source.TileAnchorDeltas![10, 11] = (10, 9); source.StaticChainHeads![20, 20] = 1;
        var floor = new WorldPathingLayer(64, 64);
        source.IndoorTileGroups.Add(new(new(source.Coord, 0), 0, 0, 64, 64, default, 1, floor, new(64, 64), []));
        using var fixture = JsonDocument.Parse(File.ReadAllText(RepositoryFiles.Evidence("native-sector-patches.json")));
        var records = fixture.RootElement.GetProperty("static_records_hex").EnumerateArray()
            .Select(row => MemoryMarshal.Read<StaticObjectRecord>(Convert.FromHexString(row.GetString()!))).ToArray();
        var copiesType = typeof(SacredNetworkSectorPatches).Assembly.GetType("Sacred.World.Networking.SacredNetworkSectorCopies", true)!;
        var copies = Activator.CreateInstance(copiesType)!;
        var copy = (Sector)copiesType.GetMethod("Create")!.Invoke(copies, [source])!;
        copiesType.GetMethod("Synchronize")!.Invoke(copies, [copy]);
        Func<uint, StaticObjectRecord?> read = id => id is >= 1 and <= 4 ? records[id-1] : null;
        var service = CreateService(read, null, world);
        var changed = ApplyReceived(service, new Dictionary<ushort, Sector> { [1] = copy });
        Assert.AreEqual(entered && surface <= 1, changed);
        Assert.AreEqual(entered && surface == 0 ? WorldPathFlags.Byte80 : 0, copy.Pathing[10, 11].Flags);
        Assert.AreEqual(entered && surface == 1 ? WorldPathFlags.Byte80 : 0, copy.IndoorTileGroups.Groups[0].Pathing[10, 11].Flags);
        Assert.AreEqual((WorldPathFlags)0, source.Pathing[10, 11].Flags);
        Assert.AreEqual((WorldPathFlags)0, floor[10, 11].Flags);
        Assert.AreEqual(0, world.WorldBanks.Registrations.PendingTileCount);
        Assert.AreSame(world.WorldBanks.Registrations.Mouseovers.Active, service.Active);
    }

    [TestMethod]
    public async Task PendingTerrainRetainsReceiptUntilSessionCopyCanBeMarked()
    {
        var world = new GoldWorldReplica(); SetEntered(world, true);
        world.Apply(GoldGameEventDecoder.Decode(new(0x1d0, 0, 1, 0, Patch(0))));
        var assembly = typeof(SacredNetworkSectorPatches).Assembly;
        var terrainType = assembly.GetType("Sacred.World.Networking.SacredNetworkPatchTerrain", true)!;
        var copies = Activator.CreateInstance(assembly.GetType("Sacred.World.Networking.SacredNetworkSectorCopies", true)!)!;
        var loaded = new TaskCompletionSource<Sector?>(); var loads = 0;
        Func<SectorCoord, Task<Sector?>> load = _ => { loads++; return loaded.Task; };
        Func<ushort, SectorCoord?> coordinate = id => id == 1 ? new SectorCoord(0, 0) : null;
        using var terrain = (IDisposable)Activator.CreateInstance(terrainType, [load, coordinate, copies])!;
        var service = CreateService(_ => null, terrain, world);
        Assert.IsFalse(ApplyReceived(service, new Dictionary<ushort, Sector>()));
        Assert.AreEqual(1, world.WorldBanks.Registrations.PendingTileCount);
        Assert.IsFalse(ApplyReceived(service, new Dictionary<ushort, Sector>())); Assert.AreEqual(1, loads);
        var source = SectorPatchChecks.Sector(); loaded.SetResult(source);
        Assert.IsTrue(ApplyReceived(service, new Dictionary<ushort, Sector>()));
        Assert.AreEqual(0, world.WorldBanks.Registrations.PendingTileCount);
        var future = await (Task<Sector?>)terrainType.GetMethod("LoadVisible")!.Invoke(terrain, [(ushort)1])!;
        Assert.AreEqual(WorldPathFlags.Byte80, future!.Pathing[10, 11].Flags);
        Assert.AreEqual((WorldPathFlags)0, source.Pathing[10, 11].Flags);
    }

    private static SacredNetworkSectorPatches CreateService(Func<uint, StaticObjectRecord?> read, object? terrain, GoldWorldReplica world) =>
        (SacredNetworkSectorPatches)Activator.CreateInstance(typeof(SacredNetworkSectorPatches), Hidden, null,
            [read, terrain, world.WorldBanks.Registrations], null)!;
    private static bool ApplyReceived(SacredNetworkSectorPatches service, IReadOnlyDictionary<ushort, Sector> sectors)
    {
        Func<ushort, SectorCoord?> coordinate = id => id == 1 ? new SectorCoord(0, 0) : null;
        return (bool)typeof(SacredNetworkSectorPatches).GetMethod("ApplyReceived", Hidden)!.Invoke(service, [sectors, coordinate])!;
    }
    private static byte[] Patch(ushort surface)
    {
        var bytes = new byte[88];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, 10); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 11);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), surface); return bytes;
    }
    private static void SetEntered(GoldWorldReplica world, bool entered)
    {
        var incoming = new ushort[9]; incoming[0] = entered ? (ushort)1 : (ushort)2;
        var outgoing = new ushort[9]; outgoing[0] = world.SectorInterest.Center;
        world.BeginSectorInterest(GoldSectorInterestRequest.Create(incoming[0], world.SectorInterest.Center, incoming, outgoing, 0));
    }
}
