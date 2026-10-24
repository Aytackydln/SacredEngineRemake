using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class CreatureCommandTests
{
    [TestMethod]
    public void LinksPreserveDuplicateRecordsAndUseNativePlayerAndNpcMasks()
    {
        var world = new GoldWorldReplica(); world.Apply(CreatureCommandPackets.Actor(2)); world.Apply(CreatureCommandPackets.Actor(501));
        var before = world.Objects[501];
        world.Apply(Event(0x12f, Words(2, 501, 0x80000000)));
        var child = world.Objects[501].Creature!; Assert.AreEqual(2u, child.Word251); Assert.AreEqual(3u, child.Word1F0);
        Assert.AreEqual((0xabcdefefu & 0xeff62000) | 0x100, child.Word1F4);
        CollectionAssert.AreEqual(new byte[] { 245, 1, 0, 0, 0, 0, 0, 0, 1 }, world.Objects[2].LinkedCreatures!.ToPayload());
        world.Apply(Event(0x12f, Words(2, 501, 0))); Assert.AreEqual((byte)1, world.Objects[2].LinkedCreatures!.GetRecord(0)[8]);
        Assert.AreEqual(0u, before.Creature!.Word251); Assert.AreEqual(6u, before.Creature.Word1F0);
        world.Apply(CreatureCommandPackets.Actor(500)); world.Apply(Event(0x12f, Words(500, 501, 1)));
        Assert.AreEqual(1u, world.Objects[501].Creature!.Word1F4); Assert.AreEqual(6u, world.Objects[501].Creature!.Word1F0);
        Assert.AreEqual((byte)0, world.Objects[500].LinkedCreatures!.GetRecord(0)[8]);
    }

    [TestMethod]
    public void RemovalSwapsTheLastMatchingRecordAndLaterTailWins()
    {
        var roster = new byte[] { 245, 1, 0, 0, 77, 0, 0, 0, 3, 188, 2, 0, 0, 88, 0, 0, 0, 4, 245, 1, 0, 0, 99, 0, 0, 0, 5 };
        var world = new GoldWorldReplica(); world.Apply(CreatureCommandPackets.Actor(500, roster: roster)); world.Apply(CreatureCommandPackets.Actor(501));
        var before = world.Objects[500].LinkedCreatures!;
        world.Apply(Event(0x12e, Words(500, 501)));
        CollectionAssert.AreEqual(new byte[] { 245, 1, 0, 0, 99, 0, 0, 0, 5, 188, 2, 0, 0, 88, 0, 0, 0, 4 }, world.Objects[500].LinkedCreatures!.ToPayload());
        Assert.AreEqual(uint.MaxValue, world.Objects[501].Creature!.Word251); Assert.AreEqual(3, before.Count);
        world.Apply(CreatureCommandPackets.Actor(500, roster: [])); Assert.AreEqual(0, world.Objects[500].LinkedCreatures!.Count);
        world.Apply(CreatureCommandPackets.Actor(600, tail: false)); world.Apply(Event(0x12f, Words(600, 501, 1)));
        Assert.IsNull(world.Objects[600].LinkedCreatures, "An absent roster remains unknown rather than becoming a fabricated empty baseline.");
        world.Apply(CreatureCommandPackets.Actor(600, roster: [])); Assert.AreEqual(0, world.Objects[600].LinkedCreatures!.Count);
    }

    [TestMethod]
    public void MountTransitionsChangeFlagsWithoutRewritingIndependentPairing()
    {
        var world = new GoldWorldReplica(); world.Apply(CreatureCommandPackets.Actor(500)); world.Apply(CreatureCommandPackets.Actor(501, flags: 0x10));
        world.Apply(Event(0x160, Words(500, 501)));
        Assert.AreEqual(0xa000000fu, world.Objects[500].Creature!.Word200);
        Assert.AreEqual(0xa000000fu, world.Objects[501].Creature!.Word200, "Native downstream lookup permits an inactive partner.");
        Assert.AreEqual(0u, world.Objects[500].MountPartnerReference);
        world.Apply(Event(0x15e, Words(500, 501)));
        Assert.AreEqual(0xa000000bu, world.Objects[500].Creature!.Word200);
        Assert.AreEqual(0xa0000007u, world.Objects[501].Creature!.Word200);
        Assert.AreEqual((ushort)15, world.Objects[500].Creature!.Word2B7); Assert.AreEqual((ushort)15, world.Objects[501].Creature!.Word2B7);
        world.Apply(Event(0x167, Words(500))); world.Apply(Event(0x160, Words(500, 501)));
        Assert.AreEqual(2, world.CreatureCommands.Requests.Count, "The initiating creature still has an inactive guard.");
    }

    [TestMethod]
    public void HitNotificationsKeepExactHalfwordsAndOriginalTargetLifetimes()
    {
        var world = new GoldWorldReplica(); world.Apply(CreatureCommandPackets.Actor(500, "physical")); world.Apply(CreatureCommandPackets.Actor(501));
        world.Apply(Event(0x13c, CreatureCommandPackets.Hit())); var work = world.CreatureCommands.Requests.Single();
        Assert.AreEqual(2, work.HitNotifications.Count); Assert.AreEqual(0x44u, work.HitNotifications[0].NativeEventId);
        var secondary = work.HitNotifications[1]; Assert.AreEqual(0x20du, secondary.NativeEventId);
        Assert.AreEqual(uint.MaxValue, secondary.Arguments[0]); Assert.AreEqual(0xabcdu, secondary.Arguments[3]); Assert.AreEqual(0x1234u, secondary.Arguments[4]);
        world.Apply(Event(0x167, Words(501))); world.Apply(CreatureCommandPackets.Actor(501));
        Assert.AreNotEqual(secondary.Object.InstanceVersion, world.Objects[501].InstanceVersion);
        world.Apply(Event(0x167, Words(501))); world.Apply(Event(0x13c, CreatureCommandPackets.Hit()));
        Assert.AreEqual(1, world.CreatureCommands.Requests[^1].HitNotifications.Count);
    }

    [TestMethod]
    public void DefinitionContextAndValueInputsRevalidateWireWithoutDereferencingPointers()
    {
        var world = new GoldWorldReplica(); world.Apply(CreatureCommandPackets.Actor(500, flags: 0x10));
        var bytes = CreatureCommandPackets.Definition(); var entry = Event(0x114, bytes); Array.Fill(bytes, (byte)0xa5); world.Apply(entry);
        Assert.AreEqual(500u, world.CreatureCommands.Requests.Single().Subject!.BaseState.ObjectReference);
        var before = world.Objects[500]; Assert.AreEqual(0x10u, before.BaseState.Flags);
        world.Apply(Event(0x114, [..Words(0xdeadbeef, 999), ..new byte[348]])); Assert.IsNull(world.CreatureCommands.Requests[^1].Subject);
        world.Apply(CreatureCommandPackets.Actor(500));
        var wire = new GoldMessage(0x125, 0, 2, 0, Words(500, 30, 1, 0x7fc00001, uint.MaxValue, 2, 3));
        world.Apply(new GoldCreatureValueCommandEvent(wire, 999, new uint[] { 999 }));
        var value = (GoldCreatureValueCommandEvent)world.CreatureCommands.Requests[^1].Event;
        CollectionAssert.AreEqual(new uint[] { 30, 1, 0x7fc00001, uint.MaxValue, 2, 0, 3, 0 }, value.NativeArguments.ToArray());
        Assert.IsNotNull(world.Objects[500].Values, "No made-up value calculation replaces the last received baseline.");
    }

    [TestMethod]
    public void LocalHitChecksRequireLocalReferenceAndFxTargetUsesDistinctTypes()
    {
        var world = new GoldWorldReplica(); world.Apply(GoldGameEventDecoder.Decode(CreatureCommandPackets.Admission()));
        world.Apply(CreatureCommandPackets.Actor(2));
        world.Apply(Event(0x180, [..Words(500), ..new byte[32]])); Assert.AreEqual(0, world.CreatureCommands.Requests.Count);
        world.Apply(Event(0x180, [..Words(2), ..new byte[32]])); Assert.AreEqual(1, world.CreatureCommands.Requests.Count);
        world.Apply(CreatureCommandPackets.Actor(500, "fx")); world.Apply(CreatureCommandPackets.Actor(501, "physical"));
        world.Apply(Event(0x177, Words(500, 501, 0x80000000, 0x7fc00001, 0x3f800000, uint.MaxValue)));
        var work = world.CreatureCommands.Requests[^1]; Assert.IsNotNull(work.Subject!.Fx); Assert.IsNotNull(work.Other!.Physical);
        Assert.AreEqual(0x80000000u, ((GoldFxTargetEvent)work.Event).Vector.X);
        world.Apply(Event(0x167, Words(501))); world.Apply(Event(0x177, Words(500, 501, 1, 2, 3, 4)));
        Assert.AreEqual(2, world.CreatureCommands.Requests.Count);
    }

    [TestMethod]
    public async Task FragmentedTcpAppliesCommandsAndMalformedFinalCommandRejectsEntireBatch()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        world.Apply(GoldGameEventDecoder.Decode(CreatureCommandPackets.Admission()));
        world.Apply(CreatureCommandPackets.Actor(2)); world.Apply(CreatureCommandPackets.Actor(500)); world.Apply(CreatureCommandPackets.Actor(501));
        world.Apply(GoldGameEventDecoder.Decode(CreatureCommandPackets.Dialogs()));
        await peer.SendAsync(new(0x114, 0, 2, 1, CreatureCommandPackets.Definition()), new(0x125, 0, 2, 2, Words(500, 30, 1, 2, 3, 4, 5)),
            new(0x12f, 0, 2, 3, Words(2, 501, 1)), new(0x160, 0, 2, 4, Words(500, 501)), new(0x15e, 0, 2, 5, Words(500, 501)),
            new(0x13c, 0, 2, 6, CreatureCommandPackets.Hit()), new(0x180, 0, 2, 7, [..Words(2), ..new byte[32]]),
            new(0x1cd, 0, 2, 8, [..Words(900, 2, 7), 78, 112, 99, 0]), new(0x12e, 0, 2, 9, Words(2, 501)));
        var received = await peer.ReceiveAsync(); GoldGameDispatcher.Dispatch(received.Messages, world.Apply);
        Assert.AreEqual(9, world.CreatureCommands.Requests.Count); Assert.AreEqual(0, world.Objects[2].LinkedCreatures!.Count);
        Assert.AreEqual(2u, world.ScriptDialogs.Records[2].AttachedObjectReference, "The native resource scan selects the lowest matching reference.");
        world.Apply(CreatureCommandPackets.Actor(600, "fx"));
        await peer.SendAsync(new GoldMessage(0x177, 0, 2, 10, Words(600, 501, 1, 2, 3, 4)));
        received = await peer.ReceiveAsync(); GoldGameDispatcher.Dispatch(received.Messages, world.Apply); Assert.AreEqual(10, world.CreatureCommands.Requests.Count);
        var revision = world.Revision; var fields = world.Objects[501].Creature!.Word1F4;
        await peer.SendAsync(new(0x12f, 0, 2, 11, Words(2, 501, 1)), new(0x1cd, 0, 2, 12, [..Words(900, 3, 0), 65]));
        received = await peer.ReceiveAsync(); Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(received.Messages, world.Apply));
        Assert.AreEqual(revision, world.Revision); Assert.AreEqual(fields, world.Objects[501].Creature!.Word1F4);
        Assert.AreEqual(0, world.Objects[2].LinkedCreatures!.Count); Assert.AreEqual(10, world.CreatureCommands.Requests.Count);
        world.Clear(); Assert.AreEqual(0, world.CreatureCommands.Requests.Count); Assert.AreEqual(0, world.CreatureCommands.Dialogs.Groups.Count);
    }
}
