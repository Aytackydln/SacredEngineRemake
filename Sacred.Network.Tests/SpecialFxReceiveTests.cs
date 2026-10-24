using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Gameplay;
using Sacred.Network.Tests.Infrastructure;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;
using static Sacred.Network.Tests.Infrastructure.CounterReceivePackets;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class SpecialFxReceiveTests
{
    [TestMethod]
    public void LateKeyxResolutionAndIndependentFxFieldsKeepLatestState()
    {
        var world = new GoldWorldReplica(); world.Apply(Fx()); var before = world.Objects[500];
        world.Apply(Event(0x1a6, Ball())); Assert.AreEqual(1, world.SpecialFx.PendingRelocationCount);
        Assert.AreEqual(100, world.Objects[500].BaseState.X);
        world.Apply(Event(0x1a4, Words(500, 42)));
        Assert.AreEqual(42u, world.Objects[500].Fx!.WearerReference);
        var revision = world.Revision;
        world.SpecialFx.ConfigureSectors(MovementTriggerInsertionChecks.Index([(1u, 0, 0)]));
        Assert.IsTrue(world.Revision > revision, "Late sector completion invalidates world consumers.");
        var current = world.Objects[500]; Assert.AreEqual(0, world.SpecialFx.PendingRelocationCount);
        Assert.AreEqual(300, current.BaseState.X); Assert.AreEqual(400, current.BaseState.Y); Assert.AreEqual((ushort)1, current.BaseState.Sector);
        Assert.AreEqual(42u, current.Fx!.WearerReference); Assert.AreEqual((ushort)0x1a6, current.PositionOpcode);
        Assert.AreEqual(current.BaseState.X, current.Fx.BaseState.X); Assert.AreEqual(before.InstanceVersion, current.InstanceVersion);
        Assert.AreEqual(0u, before.Fx!.WearerReference, "Previous snapshots stay immutable.");
        Assert.AreEqual(100, before.Fx.BaseState.X);
    }

    [TestMethod]
    public void FullSnapshotsOverrideSerializedFieldsAndReuseClearsRuntimeState()
    {
        var world = new GoldWorldReplica(); world.Apply(Fx()); world.Apply(Event(0x1a6, Ball()));
        world.Apply(Event(0x1a4, Words(500, 42))); var first = world.Objects[500];
        world.Apply(Fx());
        Assert.AreEqual(0u, world.Objects[500].Fx!.WearerReference, "1A4 updates an existing serialized field; later full snapshot wins.");
        Assert.AreEqual(first.SpecialFx!.ParticleClockWord, world.Objects[500].SpecialFx!.ParticleClockWord, "Particle runtime fields absent from 16F survive a same-instance snapshot.");
        Assert.IsFalse(world.Objects[500].SpecialFx!.RelocationPending, "A newer full position supersedes the older deferred relocation.");
        world.SpecialFx.ConfigureSectors(MovementTriggerInsertionChecks.Index([(1u, 0, 0)]));
        Assert.AreEqual(100, world.Objects[500].BaseState.X);
        world.Apply(Event(0x167, Words(500))); world.Apply(Fx());
        Assert.AreNotEqual(first.InstanceVersion, world.Objects[500].InstanceVersion);
        Assert.IsNull(world.Objects[500].SpecialFx);
        world.SpecialFx.ConfigureSectors(MovementTriggerInsertionChecks.Index([(1u, 0, 0)]));
        Assert.AreEqual(100, world.Objects[500].BaseState.X, "Stale pending work cannot relocate the reused reference.");
    }

    [TestMethod]
    public void FalseBallFlagRetainsPreviousFlagsAndGhostKeepsOtherPayloadFields()
    {
        var world = new GoldWorldReplica(); world.Apply(Fx()); world.Apply(Event(0x1a6, Ball()));
        var direction = world.Objects[500].Fx!.MoveDirection;
        world.Apply(Event(0x1a6, Ball(0x7fc00001, 0)));
        Assert.AreEqual((ushort)8, world.Objects[500].SpecialFx!.ParticleFlags); Assert.AreEqual(0u, world.Objects[500].SpecialFx!.ParticleClockWord);
        world.Apply(Event(0x1a9, Words(500, 300, 400, 0, 0x80000000, 0x3f800000, 0x7fc00001, 0x3fc00000, uint.MaxValue, 0x80000000, 123)));
        var current = world.Objects[500]; Assert.AreEqual(direction.X, current.Fx!.MoveDirection.X);
        Assert.AreEqual(1f, current.Fx.MoveDirection.Y); Assert.AreEqual(uint.MaxValue, current.Fx.IntegerData1);
        Assert.AreEqual(123u, current.Fx.IntegerData3); Assert.AreEqual(1.5f, current.Fx.FloatData1);
        Assert.AreEqual((ushort)8, current.SpecialFx!.ParticleFlags); Assert.IsNull(current.SpecialFx.ParticlePosition);
        Assert.AreEqual(0u, current.SpecialFx.ParticleClockWord);
    }

    [TestMethod]
    public void InactiveFxCannotBeRelocatedByLateSectorAvailability()
    {
        var world = new GoldWorldReplica(); world.Apply(Fx()); world.Apply(Event(0x1a6, Ball()));
        world.Apply(Event(0x167, Words(500)));
        Assert.AreEqual(0, world.SpecialFx.PendingRelocationCount);
        world.SpecialFx.ConfigureSectors(MovementTriggerInsertionChecks.Index([(1u, 0, 0)]));
        Assert.AreEqual(100, world.Objects[500].BaseState.X);
    }

    [TestMethod]
    public async Task FragmentedTcpAppliesFxFieldsAndLateMalformedPacketHasNoEffects()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica(); world.Apply(Fx());
        await peer.SendAsync(new(0x1a6, 0, 2, 1, Ball()), new(0x1a5, 0, 2, 2, Words(500, 42)));
        var received = await peer.ReceiveAsync(); GoldGameDispatcher.Dispatch(received.Messages, world.Apply);
        Assert.AreEqual(42u, world.Objects[500].Fx!.WearerReference); Assert.AreEqual(2L, world.SpecialFx.AppliedCount);
        await peer.SendAsync(new(0x1a4, 0, 2, 3, Words(500, 999)), new(0x1a9, 0, 2, 4, new byte[43]));
        received = await peer.ReceiveAsync(); Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(received.Messages, world.Apply));
        Assert.AreEqual(42u, world.Objects[500].Fx!.WearerReference); Assert.AreEqual(2L, world.SpecialFx.AppliedCount);
    }
}
