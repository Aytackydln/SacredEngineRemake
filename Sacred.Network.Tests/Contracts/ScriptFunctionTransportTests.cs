using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Networking;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Packet")]
public sealed class ScriptFunctionTransportTests
{
    [TestMethod]
    public async Task TcpReplacementChangesLaterLookupAndCodeRangeWithoutExecutingOnReceipt()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync();
        var world = new GoldWorldReplica();
        var vectors = Vectors(Record("reserved"), Record("old", length: 4));
        var authored = SacredScriptFunctionReader.Read(vectors, 12);
        var context = new WorldScriptExecutionContext(new(), authored, new byte[] { 62, 0, 4, 0, 62, 0, 4, 0, 62, 0, 4, 0 }, true);
        var executor = new WorldScriptExecutor(context);
        world.WorldBanks.Functions.Configure(vectors);
        Assert.IsTrue(executor.InvokeOptional("old")); Assert.AreEqual(1, context.ExecutedCommands);
        await peer.SendAsync(Message(1, Record("n\u00e9w", start: 4, length: 8, number: 999, deleted: 0x12340000), 0, [0xa5]));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        WorldNetworkScriptFunctions.Apply(world.WorldBanks.Functions, context);
        Assert.AreEqual(1, context.ExecutedCommands, "Receipt must not execute the updated function.");
        Assert.IsFalse(executor.InvokeOptional("old")); Assert.IsTrue(executor.InvokeOptional("n\u00e9w"));
        Assert.AreEqual(3, context.ExecutedCommands, "Both instructions in the received byte range execute.");
        Assert.AreEqual(999, context.Functions[1].Number, "Slot index and Number are independent.");
        Assert.AreEqual("old", authored[1].Name, "Authored campaign definitions remain isolated.");
        await peer.SendAsync(Message(1, Record("deleted", length: 4, deleted: 0x12340001), 2));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        WorldNetworkScriptFunctions.Apply(world.WorldBanks.Functions, context);
        Assert.IsTrue(executor.InvokeOptional("deleted")); Assert.AreEqual(3, context.ExecutedCommands);
        Assert.AreEqual(0, world.CreatureControls.PendingReplies);
    }

    [TestMethod]
    public async Task TcpUpdatesDuringLoadingUseLatestSlotValueAndRejectOutOfRangeIndices()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        await peer.SendAsync(Message(1, Record("first")), Message(1, Record("last"), 2), Message(uint.MaxValue, Record("ignore"), 3));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        var functions = world.WorldBanks.Functions;
        Assert.IsFalse(functions.IsConfigured); Assert.AreEqual(2, functions.PendingCount);
        functions.Configure(Vectors(Record("zero"), Record("initial")));
        Assert.AreEqual(2, functions.Definitions.Count); Assert.AreEqual("last", functions.Definitions[1].Name);
        Assert.AreEqual(1, functions.Updates.Count); Assert.AreEqual(0, functions.PendingCount);
        functions.Configure(Vectors(Record("would replace")));
        Assert.AreEqual(2, functions.Definitions.Count, "Repeated setup cannot erase received state.");
        world.Clear(); Assert.IsFalse(functions.IsConfigured);
        functions.Configure(Vectors(Record("new session")));
        Assert.AreEqual("new session", functions.Definitions[0].Name); Assert.AreEqual(0, functions.Updates.Count);
    }

    [TestMethod]
    public async Task MalformedTcpBatchDoesNotPartiallyReplaceAFunction()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        world.WorldBanks.Functions.Configure(Vectors(Record("old")));
        await peer.SendAsync(Message(0, Record("new")), new GoldMessage(0x1c4, 0, 1, 1, new byte[87]));
        var received = await peer.ReceiveAsync();
        Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(received.Messages, world.Apply));
        Assert.AreEqual("old", world.WorldBanks.Functions.Definitions[0].Name); Assert.AreEqual(0L, world.Revision);
    }

    [TestMethod]
    public void InvalidCodeRangeIsRetainedButCannotExecuteAndForgedMetadataCannotOverrideWire()
    {
        var world = new GoldWorldReplica(); var vectors = Vectors(Record("zero"), Record("old"));
        world.WorldBanks.Functions.Configure(vectors);
        var context = new WorldScriptExecutionContext(new(), SacredScriptFunctionReader.Read(vectors, 0), Array.Empty<byte>(), true);
        var message = Message(1, Record("invalid", start: int.MaxValue, length: 4));
        world.Apply(new GoldScriptFunctionEvent(message, 0, SacredScriptFunctionDefinition.FromPayload(Record("forged"))));
        WorldNetworkScriptFunctions.Apply(world.WorldBanks.Functions, context);
        Assert.AreEqual("zero", context.Functions[0].Name); Assert.AreEqual("invalid", context.Functions[1].Name);
        Assert.ThrowsException<InvalidDataException>(() => context.RequireFunction(1));
        Assert.AreEqual(int.MaxValue, world.WorldBanks.Functions.Definitions[1].Start);
    }

    [TestMethod]
    public void TruncatedVectorDataDoesNotConsumePendingUpdates()
    {
        var world = new GoldWorldReplica(); world.Apply(GoldGameEventDecoder.Decode(Message(0, Record("pending"))));
        var functions = world.WorldBanks.Functions;
        Assert.ThrowsException<InvalidDataException>(() => functions.Configure(new byte[3]));
        Assert.ThrowsException<InvalidDataException>(() => functions.Configure(new byte[] { 1, 0, 0, 0 }));
        Assert.ThrowsException<InvalidDataException>(() => functions.Configure(new byte[] { 255, 255, 255, 255 }));
        Assert.IsFalse(functions.IsConfigured); Assert.AreEqual(1, functions.PendingCount);
        functions.Configure(Vectors(Record("baseline")));
        Assert.AreEqual("pending", functions.Definitions[0].Name);
    }

    private static byte[] Record(string name, int start = 0, int length = 0, int number = 0, uint deleted = 0)
    {
        var bytes = new byte[84]; Encoding.Latin1.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(64), start);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(68), length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(72), -1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(76), number);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(80), deleted); return bytes;
    }
    private static byte[] Vectors(params byte[][] records)
    {
        var bytes = new byte[4 + records.Sum(record => record.Length)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, records.Length); var offset = 4;
        foreach (var record in records) { record.CopyTo(bytes, offset); offset += record.Length; }
        return bytes;
    }
    private static GoldMessage Message(uint index, byte[] record, byte sender = 0, byte[]? suffix = null)
    {
        var bytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, index);
        return new(0x1c4, sender, 1, 0, [..bytes, ..record, ..suffix ?? []]);
    }
}
