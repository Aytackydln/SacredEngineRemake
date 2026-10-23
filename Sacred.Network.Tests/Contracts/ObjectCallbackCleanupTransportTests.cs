using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Packet")]
public sealed class ObjectCallbackCleanupTransportTests
{
    [TestMethod]
    public async Task TcpBankAndCleanupRemoveEligibleCallbacksWithoutReplyOrDisconnect()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        await peer.SendAsync(Bank(Record(500, "Death"), Record(500, "Use:door"), Record(501, "Other"), Record(500, "sEe:watch")));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        Assert.AreEqual(4, world.DeathScripts.Registrations.Count);
        await peer.SendAsync(Cleanup(500, [9, 8, 7]));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        CollectionAssert.AreEqual(new[] { "Other", "Use:door" }, world.DeathScripts.Registrations.Select(row => row.ScriptText).ToArray());
        Assert.AreEqual(2L, world.DeathScripts.RemovedRegistrationCount);
        Assert.IsFalse(peer.Connection.IsStopping); Assert.AreEqual(0L, peer.Connection.Diagnostics.SentMessages);
        Assert.AreEqual(0, world.CreatureControls.PendingReplies); Assert.AreEqual(0, world.WorldBanks.Functions.PendingCount);
    }

    [TestMethod]
    public void DeathModeKeepsOtherColonCallbacksAndWegwMarkers()
    {
        var world = new GoldWorldReplica();
        foreach (var name in new[] { "Use:door", "Take:item", "Other:callback", "wegw", "WeGwMarker", "wegw:kept", "Death", "see:watch", "TakeNoColon", "" })
            world.DeathScripts.Register(Record(500, name));
        Apply(world, Cleanup(500));
        CollectionAssert.AreEquivalent(new[] { "Use:door", "Take:item", "Other:callback", "wegw", "WeGwMarker", "wegw:kept" },
            world.DeathScripts.Registrations.Select(row => row.ScriptText).ToArray());
        Assert.AreEqual(4L, world.DeathScripts.RemovedRegistrationCount);
        Apply(world, Cleanup(500)); Assert.AreEqual(4L, world.DeathScripts.RemovedRegistrationCount);
        Assert.AreEqual(2L, world.DeathScripts.CleanupNotificationCount);
    }

    [TestMethod]
    public void NonemptyBankReplacesIncrementalStateButEmptyBankPreservesIt()
    {
        var world = new GoldWorldReplica();
        Apply(world, Registration(500, "Old")); Apply(world, Bank(Record(501, "New"), Record(500, "Use:door")));
        Assert.AreEqual(2, world.DeathScripts.Registrations.Count);
        Apply(world, Bank()); Assert.AreEqual(2, world.DeathScripts.Registrations.Count);
        Apply(world, Registration(500, "Death")); Apply(world, Cleanup(500));
        Assert.AreEqual(2, world.DeathScripts.Registrations.Count); Assert.AreEqual(1L, world.DeathScripts.RemovedRegistrationCount);
        Apply(world, Bank(Record(500, "See:new"))); Assert.AreEqual(1, world.DeathScripts.Registrations.Count);
        Apply(world, Cleanup(500)); Assert.AreEqual(0, world.DeathScripts.Registrations.Count);
    }

    [TestMethod]
    public void InvalidLaterPacketRejectsTheWholeCleanupBatch()
    {
        var world = new GoldWorldReplica(); world.DeathScripts.Register(Record(500, "Death"));
        Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch([Cleanup(500), new(0x1c1, 0, 0, 0, [0, 0, 0])], world.Apply));
        Assert.AreEqual(1, world.DeathScripts.Registrations.Count); Assert.AreEqual(0L, world.DeathScripts.CleanupNotificationCount);
        Assert.AreEqual(0L, world.Revision);
    }

    [TestMethod]
    public void OwnedWireWinsOverForgedReferenceAndInvalidOpcodeIsRejected()
    {
        var world = new GoldWorldReplica();
        world.DeathScripts.Register(Record(500, "Death")); world.DeathScripts.Register(Record(501, "Other"));
        var wire = Cleanup(500, [9, 8, 7]); var entry = GoldGameEventDecoder.Decode(wire); Array.Fill(wire.Payload, (byte)0xcc);
        world.Apply(entry); Assert.AreEqual(501u, world.DeathScripts.Registrations.Single().ObjectReference);
        Assert.AreEqual((byte)7, entry.Message.Payload[^1]);
        world.Apply(new GoldObjectCallbackCleanupEvent(Cleanup(501), 500)); Assert.AreEqual(0, world.DeathScripts.Registrations.Count);
        Assert.ThrowsException<InvalidDataException>(() => world.Apply(new GoldObjectCallbackCleanupEvent(new(0xffff, 0, 0, 0, []), 0)));
        Assert.AreEqual(2L, world.Revision);
    }

    [TestMethod]
    public void ReferencesNeedNoLiveObjectAndCleanupStillAppliesDuringPause()
    {
        var world = new GoldWorldReplica(); Apply(world, new(0x135, 0, 0, 0, []));
        foreach (var reference in new uint[] { 0, 500, uint.MaxValue })
        {
            Apply(world, Registration(reference, "Death")); Apply(world, Cleanup(reference));
        }
        Assert.AreEqual(0, world.Objects.Count); Assert.IsTrue(world.Session.IsPaused);
        Assert.AreEqual(0, world.DeathScripts.Registrations.Count); Assert.AreEqual(3L, world.DeathScripts.CleanupNotificationCount);
        world.Clear(); Assert.AreEqual(0L, world.DeathScripts.CleanupNotificationCount); Assert.AreEqual(0L, world.DeathScripts.RemovedRegistrationCount);
    }

    [TestMethod]
    public void OversizedTableClearsEvenWhenTheRequestedReferenceIsAbsent()
    {
        var world = new GoldWorldReplica(); var row = Record(501, "Use:kept");
        for (var index = 0; index < 60001; index++) world.DeathScripts.Register(row);
        Apply(world, Cleanup(500));
        Assert.AreEqual(0, world.DeathScripts.Registrations.Count); Assert.AreEqual(60001L, world.DeathScripts.RemovedRegistrationCount);
        Assert.AreEqual(1L, world.DeathScripts.OversizedResetCount);
    }

    private static void Apply(GoldWorldReplica world, GoldMessage message) => world.Apply(GoldGameEventDecoder.Decode(message));
    private static GoldMessage Cleanup(uint reference, byte[]? suffix = null)
    {
        var bytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, reference);
        return new(0x1c1, 0, 3, 0, [..bytes, ..suffix ?? []]);
    }
    private static GoldDeathScriptRegistration Record(uint reference, string name)
    {
        var text = new byte[64]; Encoding.Latin1.GetBytes(name, text);
        return GoldDeathScriptRegistration.Create(reference, text);
    }
    private static GoldMessage Registration(uint reference, string name) => new(0x1c2, 0, 3, 0, Record(reference, name).ToPayload());
    private static GoldMessage Bank(params GoldDeathScriptRegistration[] rows) => ScriptResourceChecks.Bank(0x1b2, 3, rows.Select(row => row.ToPayload()).ToArray());
}
