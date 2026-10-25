using System.Buffers.Binary;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Gameplay;
using Sacred.Particles;
using Sacred.World.Particles;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class CombatReplicationConsumerTests
{
    [TestMethod]
    public void AttachedDeflectionUsesNeckOrRootAndExpiresWithoutDamagePacket()
    {
        foreach (var quality in Enum.GetValues<SacredParticleQuality>())
        foreach (var hasNeck in new[] { false, true })
        {
            var world = new GoldWorldReplica(); world.Apply(Actor());
            world.Apply(Event(0x149, Words(0x305, 10, 500, 0, 0, 0, 0)));
            var system = new WorldCreatureFxSystem();
            var output = new List<WorldParticle>();
            var position = new Vector2(10, 20);
            WorldObjectFxAttachment? Attachment(uint reference, ushort bone) =>
                bone == 3 && !hasNeck ? null : new(position, bone == 3 ? 40 : 5, 40)
                { InstanceVersion = world.Objects[reference].InstanceVersion };
            system.Synchronize(world, Attachment);
            system.Advance(SacredParticleCatalogue.LoadEmbedded(quality), .01f, output);
            Assert.AreEqual(40, output.Count);
            Assert.IsTrue(output.All(p => p.Untextured && p.CornerColors.HasValue));
            Assert.AreEqual(0, system.Pending.Count);
            var first = output[0]; position.X += 10;
            output.Clear(); system.Synchronize(world, Attachment);
            system.Advance(SacredParticleCatalogue.LoadEmbedded(quality), 0, output);
            Assert.AreEqual(first.WorldX + 10, output[0].WorldX, .0001f);
            Assert.AreEqual(first.Height, output[0].Height, .0001f);
            output.Clear(); system.Advance(SacredParticleCatalogue.LoadEmbedded(quality), .3f, output);
            Assert.AreEqual(0, output.Count); Assert.AreEqual(0, system.ActiveCount);
            system.Synchronize(world, Attachment);
            Assert.AreEqual(0, system.TrackedCount, "A receipt must not replay after expiry.");
            world.Clear(); system.Synchronize(world, (_, _) => null);
            Assert.AreEqual(0, system.Pending.Count);
        }
    }

    [TestMethod]
    public void SerializedDeflectionUsesCreatureWearerBoneAndDoesNotReloadEachFrame()
    {
        var world = new GoldWorldReplica(); world.Apply(Actor()); world.Apply(SerializedDeflection());
        var system = new WorldObjectFxSystem(); var output = new List<WorldParticle>();
        WorldObjectFxAttachment? Attachment(uint reference) => new(new(10, 20), 0, 40)
        {
            InstanceVersion = world.Objects[reference].InstanceVersion,
            BoneAttachment = bone => bone == 3 ? new(new(11, 21), 40, 40) : null
        };
        system.Synchronize(world.Objects.Values, Attachment);
        system.Advance(SacredParticleCatalogue.LoadEmbedded(), .01f, output);
        Assert.AreEqual(40, output.Count); Assert.AreEqual(0, system.Pending.Count);
        Assert.IsTrue(system.IsActive(600));
        system.Synchronize(world.Objects.Values, Attachment); Assert.AreEqual(1L, system.ReloadCount);
        output.Clear(); system.Advance(SacredParticleCatalogue.LoadEmbedded(), .3f, output);
        Assert.AreEqual(0, output.Count); Assert.IsFalse(system.IsActive(600));
    }

    [TestMethod]
    public void DifficultyTracksReceivedGameFlagsAndNativeHighestFlagPrecedence()
    {
        var world = new GoldWorldReplica(); Assert.AreEqual(0u, world.Session.Difficulty);
        foreach (var (flags, difficulty) in new[] { (0u, 0u), (0x100u, 1u), (0x200u, 2u),
                     (0x400u, 3u), (0x800u, 4u), (0xF00u, 4u), (0x40010u, 0u) })
        {
            world.Apply(Event(0x01E, Words(flags)));
            Assert.AreEqual(difficulty, world.Session.Difficulty);
        }
        world.Clear(); Assert.AreEqual(0u, world.Session.Difficulty);
    }

    [TestMethod]
    public void HitReactionUsesMappedGuardsWithoutConfusingMovementWithUnreplicatedKiEiState()
    {
        foreach (var (hp, damage, state, action, flags, expected) in new[] {
                     (100, 1u, (byte)0, 0u, 0u, true), (100, 0u, (byte)0, 0u, 0u, false),
                     (1, 1u, (byte)0, 0u, 0u, false), (0, 1u, (byte)0, 0u, 0u, false),
                     (100, 1u, (byte)9, 0u, 0u, true), (100, 1u, (byte)10, 0u, 0u, true),
                     (100, 1u, (byte)0, 6u, 0u, true), (100, 1u, (byte)0, 7u, 0u, true),
                     (100, 1u, (byte)0, 0u, GoldCreatureStateLayout.HitReactionSuppressedFlag, false) })
        {
            var world = new GoldWorldReplica(); var bytes = ((GoldObjectEvent)Actor()).ObjectState.ToArray();
            bytes[GoldCreatureStateLayout.Byte1FCOffset] = state;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.Word1F8Offset), action);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.Word200Offset), flags);
            world.Apply(GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16B, 1, bytes)));
            world.Apply(Event(0x128, Words(500, (uint)hp)));
            world.Apply(Event(0x17A, Words(500, damage, 0, 2, 0, 0)));
            Assert.AreEqual(expected, GoldCreatureHitReactionRules.ShouldPlay(world.Combat.Recent.Last()));
        }
    }

    internal static GoldGameEvent SerializedDeflection()
    {
        var bytes = new byte[GoldObjectFxStateLayout.MinimumSize];
        new GoldObjectBaseState(600, 0x305, 0, 3, 100, 200, 0, 0, 0, 0, 2, 0).ToPayload().CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldObjectFxStateLayout.MarkerOffset), GoldObjectFxStateLayout.Marker);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldObjectFxStateLayout.FxTypeOffset), 10);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(GoldObjectFxStateLayout.FlagsOffset), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(GoldObjectFxStateLayout.AttachBoneOffset), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldObjectFxStateLayout.WearerReferenceOffset), 500);
        return GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16F, 2, bytes));
    }
}
