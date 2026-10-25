using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Gameplay;
using Sacred.Network.Tests.Infrastructure;
using Sacred.World.Particles;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class FxConsumerTests
{
    [TestMethod]
    public void FullAndPartialFxReloadOnceWhilePositionAndFlagsPreservePlayback()
    {
        var world = new GoldWorldReplica(); world.Apply(Fx());
        var system = new WorldObjectFxSystem();
        void Synchronize() => system.Synchronize(world.Objects.Values, _ => null);
        Synchronize(); Assert.AreEqual(1L, system.ReloadCount);
        Synchronize(); Assert.AreEqual(1L, system.ReloadCount);
        var old = world.Objects[500];
        var partial = RemainingReceivePackets.PartialFx();
        BinaryPrimitives.WriteUInt16LittleEndian(partial.AsSpan(8 + GoldObjectFxStateLayout.FlagsOffset - GoldObjectStateLayout.Size), 1);
        world.Apply(Event(0x14B, partial)); Synchronize();
        Assert.AreEqual(old.InstanceVersion, world.Objects[500].InstanceVersion);
        Assert.AreEqual(old.FxReloadVersion + 1, world.Objects[500].FxReloadVersion);
        Assert.AreEqual(2L, system.ReloadCount);
        world.Apply(Event(0x182, Words(500))); Synchronize(); Assert.AreEqual(2L, system.ReloadCount);
        world.Apply(Fx()); Synchronize(); Assert.AreEqual(3L, system.ReloadCount);
        Assert.AreEqual((ushort)2, world.Objects[500].SpecialFx!.ParticleFlags);
        world.Clear(); system.Synchronize(world.Objects.Values, _ => null);
        Assert.IsFalse(system.IsActive(500));
    }

    [TestMethod]
    public void ParticleNotificationsReplaceWholeWordAndCannotLeakIntoReusedReferences()
    {
        var world = new GoldWorldReplica(); world.Apply(Fx());
        var before = world.Objects[500].InstanceVersion;
        world.Apply(Event(0x182, Words(0x800001F4)));
        Assert.AreEqual((ushort)2, world.Objects[500].SpecialFx!.ParticleFlags);
        world.Apply(Event(0x184, Words(500)));
        Assert.AreEqual((ushort)8, world.Objects[500].SpecialFx!.ParticleFlags, "Native setter replaces; 2|8 would wrongly retain the earlier hit flag.");
        var version = world.Objects[500].SpecialFx!.Version;
        world.Apply(Event(0x184, Words(500))); Assert.AreEqual(version + 1, world.Objects[500].SpecialFx!.Version);
        world.Apply(Event(0x167, Words(500))); world.Apply(Fx());
        Assert.AreNotEqual(before, world.Objects[500].InstanceVersion); Assert.IsNull(world.Objects[500].SpecialFx);
        world.Apply(Event(0x184, Words(500))); Assert.AreEqual((ushort)8, world.Objects[500].SpecialFx!.ParticleFlags);
    }

    [TestMethod]
    public void NotificationGuardCastsToFxWithoutInventingInactiveRejection()
    {
        var world = new GoldWorldReplica(); world.Apply(Fx(0x10)); world.Apply(Event(0x184, Words(500)));
        Assert.AreEqual((ushort)8, world.Objects[500].SpecialFx!.ParticleFlags);
        world.Clear(); world.Apply(Actor()); world.Apply(Event(0x182, Words(500)));
        Assert.IsNull(world.Objects[500].SpecialFx, "A physical creature is not a cObjectFX.");
    }

    [TestMethod]
    public void AttachedDeliveryOutlivesPrunedDiagnosticHistoryAndDoesNotReplayPerFrame()
    {
        var world = new GoldWorldReplica(); world.Apply(Actor());
        for (var i = 0; i < 1050; i++) world.Apply(Event(0x149, Words(1, 44, 500, 0, 0, 0, 0)));
        Assert.AreEqual(1024, world.AttachedFx.Entries.Count);
        var system = new WorldCreatureFxSystem();
        WorldObjectFxAttachment? Attachment(uint reference, ushort bone) => new(new(), 0, 1)
        { InstanceVersion = world.Objects[reference].InstanceVersion };
        system.Synchronize(world, Attachment); Assert.AreEqual(1050, system.TrackedCount);
        system.Synchronize(world, Attachment); Assert.AreEqual(1050, system.TrackedCount);
        world.Clear(); system.Synchronize(world, (_, _) => null); Assert.AreEqual(0, system.TrackedCount);
    }

    private static GoldGameEvent Fx(uint baseFlags = 0)
    {
        var basis = new GoldObjectBaseState(500, 1, baseFlags, 3, 100, 200, 0, 0, 0, 0, 2, 0);
        var bytes = new byte[GoldObjectFxStateLayout.MinimumSize]; basis.ToPayload().CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldObjectFxStateLayout.MarkerOffset), GoldObjectFxStateLayout.Marker);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldObjectFxStateLayout.FxTypeOffset), 0x20);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(GoldObjectFxStateLayout.FlagsOffset), 1);
        return GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16F, 1, bytes));
    }
}
