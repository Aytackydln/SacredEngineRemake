using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.World.Stairs;
using Sacred.GameplayHost.Networking;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Packet")]
public sealed class ScriptDefinitionTransportTests
{
    [TestMethod]
    public async Task TcpUpdatesChangeLaterPositionResolutionAndDeleteByExactKindAndName()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        var definitions = new WorldScriptDefinitions();
        world.WorldBanks.Definitions.Configure([Record("reserved"), Record("Gate", 10, 20)]);
        WorldNetworkScriptDefinitions.ApplyPending(world.WorldBanks.Definitions, definitions);
        Assert.AreEqual(new SacredScriptPosition(10, 20, 0), definitions.ResolvePosition("Gate"));
        await peer.SendAsync(Update(1, Record("Gate", 100, 200, surface: 2)), Update(uint.MaxValue, Record("Gate", 9, 8, kind: 2)));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        Assert.AreEqual(new SacredScriptPosition(10, 20, 0), definitions.ResolvePosition("Gate"), "Receipt queues changes before the callback boundary.");
        WorldNetworkScriptDefinitions.ApplyPending(world.WorldBanks.Definitions, definitions);
        Assert.AreEqual(new SacredScriptPosition(100, 200, 2), definitions.ResolvePosition("Gate"));
        Assert.AreEqual(3, definitions.Records.Count); Assert.AreEqual(0, world.WorldBanks.Definitions.PendingChangeCount);
        await peer.SendAsync(Delete(0, "gate"), Delete(0, "Gate"));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        WorldNetworkScriptDefinitions.ApplyPending(world.WorldBanks.Definitions, definitions);
        Assert.ThrowsException<InvalidDataException>(() => definitions.ResolvePosition("Gate"));
        Assert.AreEqual(2, definitions.Records.Count);
        Assert.AreEqual(WorldDefinitionKind.PoolPosition, definitions.Records[1].Kind);
        Assert.AreEqual(0, world.CreatureControls.PendingReplies);
    }

    [TestMethod]
    public void DeltaReplayPreservesLocalScriptDefinitionsAndUsesTheContextCurrentCount()
    {
        var world = new GoldWorldReplica(); var source = world.WorldBanks.Definitions; var definitions = new WorldScriptDefinitions();
        source.Configure([Record("reserved"), Record("Remote", 1, 2)]);
        WorldNetworkScriptDefinitions.ApplyPending(source, definitions);
        definitions.DefinePosition("Local", 31, 32);
        world.Apply(GoldGameEventDecoder.Decode(Update(1, Record("Remote", 11, 12))));
        WorldNetworkScriptDefinitions.ApplyPending(source, definitions);
        Assert.AreEqual(new SacredScriptPosition(31, 32, 0), definitions.ResolvePosition("Local"));
        // The received-only view lacks this locally created slot. The actual script context must still apply it.
        world.Apply(GoldGameEventDecoder.Decode(Update(2, Record("Local", 41, 42))));
        WorldNetworkScriptDefinitions.ApplyPending(source, definitions);
        Assert.AreEqual(new SacredScriptPosition(41, 42, 0), definitions.ResolvePosition("Local"));
        Assert.AreEqual(new SacredScriptPosition(11, 12, 0), definitions.ResolvePosition("Remote"));
        Assert.AreEqual(2, source.Records.Count); Assert.AreEqual(3, definitions.Records.Count);
        Assert.AreEqual(0, source.PendingChangeCount);
    }

    [TestMethod]
    public async Task LaterBankSupersedesPendingUpdatesAndZeroCountPreservesTheTable()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        await peer.SendAsync(Update(uint.MaxValue, Record("earlier")), Bank(Record("reserved"), Record("snapshot", 4, 5)));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        var source = world.WorldBanks.Definitions; var definitions = new WorldScriptDefinitions();
        Assert.IsTrue(source.IsInitialized); Assert.AreEqual(0, source.PendingCount);
        source.Configure([Record("must not overwrite")]);
        WorldNetworkScriptDefinitions.ApplyPending(source, definitions);
        Assert.AreEqual(2, definitions.Records.Count); Assert.AreEqual("snapshot", definitions.Records[1].Name);
        definitions.DefinePosition("local", 6, 7);
        await peer.SendAsync(Bank()); GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        WorldNetworkScriptDefinitions.ApplyPending(source, definitions);
        Assert.AreEqual(2, source.Records.Count); Assert.AreEqual(3, definitions.Records.Count);
        await peer.SendAsync(Bank(Record("new snapshot")));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        WorldNetworkScriptDefinitions.ApplyPending(source, definitions);
        Assert.AreEqual(1, definitions.Records.Count); Assert.AreEqual("new snapshot", definitions.Records[0].Name);
        world.Clear(); Assert.IsFalse(source.IsInitialized); Assert.AreEqual(0, source.PendingChangeCount);
    }

    [TestMethod]
    [DataRow((ushort)0x1bf)]
    [DataRow((ushort)0x1c0)]
    public async Task MalformedSiblingRejectsWholeTcpBatch(ushort opcode)
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        world.WorldBanks.Definitions.Configure([Record("old")]);
        var bad = opcode == 0x1bf ? new byte[103] : new byte[] { 0, 0, 0, 0, 65 };
        await peer.SendAsync(Update(0, Record("new")), new GoldMessage(opcode, 0, 1, 1, bad));
        var received = await peer.ReceiveAsync();
        Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(received.Messages, world.Apply));
        Assert.AreEqual("old", world.WorldBanks.Definitions.Records[0].Name); Assert.AreEqual(0L, world.Revision);
    }

    [TestMethod]
    public void EmptyDeletionAndUnterminatedStoredNameRemainMemorySafe()
    {
        var world = new GoldWorldReplica(); var source = world.WorldBanks.Definitions;
        source.Configure([]); world.Apply(GoldGameEventDecoder.Decode(Delete(0, "")));
        Assert.AreEqual(0, source.Records.Count);
        var bytes = Record("reserved").ToPayload(); Array.Fill(bytes, (byte)'A', 4, 64);
        world.Apply(GoldGameEventDecoder.Decode(Update(uint.MaxValue, Record("reserved"))));
        world.Apply(GoldGameEventDecoder.Decode(Update(uint.MaxValue, SacredScriptDefinition.FromPayload(bytes))));
        world.Apply(GoldGameEventDecoder.Decode(Delete(0, new string('A', 64))));
        Assert.AreEqual(2, source.Records.Count, "No native overread across malformed name storage.");
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => source.Records[0].Parameter(8));
    }

    [TestMethod]
    public void WireOverridesForgedMetadataAndPendingUpdatesReplayInOrder()
    {
        var world = new GoldWorldReplica(); var source = world.WorldBanks.Definitions;
        world.Apply(new GoldScriptDefinitionUpdateEvent(Update(uint.MaxValue, Record("n\u00e9w", -10, 20)), 0, Record("forged")));
        world.Apply(new GoldScriptDefinitionDeleteEvent(Delete(0, "old"), 1, "forged"));
        Assert.AreEqual(2, source.PendingCount); Assert.IsFalse(source.IsInitialized);
        source.Configure([Record("reserved"), Record("old")]);
        Assert.AreEqual(2, source.Records.Count); Assert.AreEqual("n\u00e9w", source.Records[1].Name);
        Assert.AreEqual(-10, source.Records[1].Parameter(0)); Assert.AreEqual(0, source.PendingCount);
        Assert.IsTrue(source.TryPeekChange(out var first));
        Assert.ThrowsException<InvalidOperationException>(() => source.CompleteChange(new(null, null)));
        source.CompleteChange(first!);
        world.Clear(); Assert.AreEqual(0, source.PendingChangeCount);
    }

    private static SacredScriptDefinition Record(string name, int x = 0, int y = 0, uint kind = 0, int surface = 0)
    {
        var bytes = new byte[100]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, kind);
        Encoding.Latin1.GetBytes(name).CopyTo(bytes, 4);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(68), x); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(72), y);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(76), -1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(80), surface);
        return SacredScriptDefinition.FromPayload(bytes);
    }
    private static GoldMessage Update(uint index, SacredScriptDefinition record)
    {
        var prefix = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(prefix, index);
        return new(0x1bf, 0, 1, 0, [..prefix, ..record.ToPayload()]);
    }
    private static GoldMessage Delete(uint kind, string name)
    {
        var prefix = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(prefix, kind);
        return new(0x1c0, 0, 1, 0, [..prefix, ..Encoding.Latin1.GetBytes(name), 0]);
    }
    private static GoldMessage Bank(params SacredScriptDefinition[] records)
    {
        var header = new byte[40]; BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(20), records.Length);
        return new(0x1b2, 0, 1, 0, [..header, ..records.SelectMany(r => r.ToPayload())]);
    }
}
