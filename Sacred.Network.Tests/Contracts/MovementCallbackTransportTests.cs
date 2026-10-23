using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.GameRes;
using Sacred.GameplayHost.Networking;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Packet")]
public sealed class MovementCallbackTransportTests
{
    [TestMethod]
    public async Task TcpRegistrationsAppendWithoutExecutingAndPreserveLocalCallbacks()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        var context = MovementCallbackChecks.Context(["reserved", "Enter", "Other"]);
        context.AppendReceivedMovementCallback("Gate", 1); // Earlier local registration.
        await peer.SendAsync(Message(2, "gATE"), Message(2, "Gate"), Message(uint.MaxValue, "Gate"));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        Assert.AreEqual(1, context.MovementCallbacks("Gate").Count);
        WorldNetworkMovementCallbacks.ApplyPending(world.WorldBanks.MovementCallbacks, context);
        CollectionAssert.AreEqual(new[] { 1, 2, 2, -1 }, context.MovementCallbacks("GATE").ToArray());
        Assert.AreEqual("gATE", world.WorldBanks.MovementCallbacks.Groups.Single().Name);
        Assert.AreEqual(0, context.ExecutedCommands); Assert.AreEqual(0, world.CreatureControls.PendingReplies);
        Assert.ThrowsException<InvalidDataException>(() => context.RequireFunction(context.MovementCallbacks("Gate").Last()));
    }

    [TestMethod]
    public async Task TcpBankReplacesAndEmptyBankClearsUnlikeDefinitionBank()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        var context = MovementCallbackChecks.Context(["", "Enter", "ENTER"]);
        MovementCallbackChecks.ConfigureFunctions(world, ["", "Enter", "ENTER"]);
        var hash = SacredResourceHash.ComputeDefaultCLocale("Enter"u8);
        await peer.SendAsync(Message(99, "old"), MovementCallbackChecks.Bank(MovementCallbackChecks.Name("Gate"), [hash, hash, uint.MaxValue]), Message(2, "Gate"));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        WorldNetworkMovementCallbacks.ApplyPending(world.WorldBanks.MovementCallbacks, context);
        Assert.AreEqual(0, context.MovementCallbacks("old").Count);
        CollectionAssert.AreEqual(new[] { 1, 1, 0, 2 }, context.MovementCallbacks("Gate").ToArray());
        await peer.SendAsync(new GoldMessage(0x1b2, 0, 1, 0, new byte[40]));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        WorldNetworkMovementCallbacks.ApplyPending(world.WorldBanks.MovementCallbacks, context);
        Assert.AreEqual(0, context.MovementCallbacks("Gate").Count);
        Assert.AreEqual(0, world.WorldBanks.MovementCallbacks.Groups.Count);
    }

    [TestMethod]
    public async Task TcpNameCopyTruncatesAt127AndDoesNotFoldHighBytes()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        await peer.SendAsync(Message(1, new string('A', 180)), Message(2, new string('a', 127)), Message(3, "\u00e4"), Message(4, "\u00c4"), Message(5, ""));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        var groups = world.WorldBanks.MovementCallbacks.Groups;
        Assert.AreEqual(4, groups.Count); Assert.AreEqual(127, groups[0].Name.Length);
        Assert.AreEqual(2, groups[0].Functions.Count); Assert.AreEqual("", groups[3].Name);
        world.Clear(); Assert.AreEqual(0, world.WorldBanks.MovementCallbacks.PendingChangeCount);
    }

    [TestMethod]
    public async Task UnterminatedShortNameRejectsWholeTcpBatch()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        await peer.SendAsync(Message(1, "Gate"), new GoldMessage(0x1d1, 0, 1, 0, new byte[] { 1, 0, 0, 0, 65 }));
        var received = await peer.ReceiveAsync();
        Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(received.Messages, world.Apply));
        Assert.AreEqual(0L, world.Revision); Assert.AreEqual(0, world.WorldBanks.MovementCallbacks.Groups.Count);
    }

    [TestMethod]
    public void Full127ByteUnterminatedNameIsValidAndWireOverridesForgedMetadata()
    {
        var bytes = new byte[131]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, 42); Array.Fill(bytes, (byte)'X', 4, 127);
        var world = new GoldWorldReplica();
        world.Apply(new GoldMovementCallbackEvent(new(0x1d1, 0, 1, 0, bytes), 999, "forged"));
        var group = world.WorldBanks.MovementCallbacks.Groups.Single();
        Assert.AreEqual(new string('X', 127), group.Name); Assert.AreEqual(42u, group.Functions.Single().Value);
        Assert.IsTrue(world.WorldBanks.MovementCallbacks.TryPeekChange(out var change));
        Assert.ThrowsException<InvalidOperationException>(() => world.WorldBanks.MovementCallbacks.CompleteChange(new(null, null)));
        world.WorldBanks.MovementCallbacks.CompleteChange(change!);
        Assert.AreEqual(0, world.WorldBanks.MovementCallbacks.PendingChangeCount);
    }
    [TestMethod]
    public void BankHashResolutionUsesFunctionNamesAtReceiptBeforeLaterRenaming()
    {
        var world = new GoldWorldReplica(); var context = MovementCallbackChecks.Context(["", "Enter"]);
        MovementCallbackChecks.ConfigureFunctions(world, ["", "Enter"]);
        var hash = SacredResourceHash.ComputeDefaultCLocale("Enter"u8);
        world.Apply(GoldGameEventDecoder.Decode(MovementCallbackChecks.Bank(MovementCallbackChecks.Name("Gate"), [hash])));
        world.Apply(GoldGameEventDecoder.Decode(FunctionMessage("Renamed")));
        WorldNetworkScriptFunctions.Apply(world.WorldBanks.Functions, context);
        WorldNetworkMovementCallbacks.ApplyPending(world.WorldBanks.MovementCallbacks, context);
        Assert.AreEqual("Renamed", context.Functions[1].Name);
        CollectionAssert.AreEqual(new[] { 1 }, context.MovementCallbacks("Gate").ToArray());
    }
    [TestMethod]
    public void LateCampaignLoadingPreservesFunctionUpdateOrderAroundBankReceipt()
    {
        var world = new GoldWorldReplica(); var context = MovementCallbackChecks.Context(["", "Original"]);
        var oldSnapshot = world.WorldBanks.Functions.Capture();
        world.Apply(GoldGameEventDecoder.Decode(FunctionMessage("BeforeBank")));
        var hash = SacredResourceHash.ComputeDefaultCLocale("BeforeBank"u8);
        world.Apply(GoldGameEventDecoder.Decode(MovementCallbackChecks.Bank(MovementCallbackChecks.Name("Gate"), [hash])));
        world.Apply(GoldGameEventDecoder.Decode(FunctionMessage("AfterBank")));
        WorldNetworkMovementCallbacks.ApplyPending(world.WorldBanks.MovementCallbacks, context);
        Assert.AreEqual(1, world.WorldBanks.MovementCallbacks.PendingChangeCount, "Missing function baseline retains work.");
        MovementCallbackChecks.ConfigureFunctions(world, ["", "Original"]);
        WorldNetworkScriptFunctions.Apply(world.WorldBanks.Functions, context);
        WorldNetworkMovementCallbacks.ApplyPending(world.WorldBanks.MovementCallbacks, context);
        Assert.AreEqual("AfterBank", context.Functions[1].Name);
        CollectionAssert.AreEqual(new[] { 1 }, context.MovementCallbacks("Gate").ToArray());
        Assert.AreEqual(0, world.WorldBanks.MovementCallbacks.PendingChangeCount);
        world.Clear(); MovementCallbackChecks.ConfigureFunctions(world, ["new session"]);
        Assert.IsFalse(oldSnapshot.TryGetDefinitions(out _), "An unresolved old-session view must not bind to a new campaign.");
    }
    private static GoldMessage FunctionMessage(string name)
    {
        var bytes = new byte[88]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1);
        Encoding.Latin1.GetBytes(name).CopyTo(bytes, 4); return new(0x1c4, 0, 1, 0, bytes);
    }
    private static GoldMessage Message(uint function, string name)
    {
        var bytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, function);
        return new(0x1d1, 0, 1, 0, [..bytes, ..Encoding.Latin1.GetBytes(name), 0]);
    }
}
