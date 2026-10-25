using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.GameBin.Balance;
using Sacred.Network.Gameplay;
using Sacred.Particles;
using Sacred.World.Networking;
using Sacred.World.Particles;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class CombatVisualConsumerTests
{
    private static SacredParticleCatalogue Catalogue => SacredParticleCatalogue.LoadEmbedded();
    private static readonly SacredCreatureMarkerBalance Tuning = new(0xE0FF3030, 0xFFFF8030, .85f, 1.1f, 1);
    private static readonly SacredCreatureBalance Levels = new(
        Enumerable.Range(1, 5).Select(i => new SacredCreatureHealthLevel(i, 1)),
        Enumerable.Repeat(1f, 6), MemoryMarshal.Read<SacredSkillCurveLayout>(Words(0, 0x3F800000, 0x3F800000)),
        new int[6], new int[6]);

    [TestMethod]
    public void EveryHitTextureUsesLowByteAndRepeatedReceiptsCreateSeparateRootFlashes()
    {
        foreach (var quality in Enum.GetValues<SacredParticleQuality>())
        {
            var catalogue = SacredParticleCatalogue.LoadEmbedded(quality);
            Assert.IsTrue(catalogue.TryGetEventDefinition(0x360, 0x416, out var recipe));
            var definition = recipe.HitFlash!;
            var world = World(); var fx = new WorldCombatFxSystem(); var output = new List<WorldParticle>();
            for (uint i = 0; i < definition.Textures.Count; i++)
                world.Apply(Event(0x17A, Words(500, 1, 0, 2, 9, 0xABCD0000 | i)));
            world.Apply(Event(0x17A, Words(500, 1, 0, 2, 1, 0)));
            fx.Synchronize(world, snapshot => Anchor(snapshot, new(10, 20)));
            fx.Advance(catalogue, .1f, output);
            Assert.AreEqual(definition.Textures.Count + 1, output.Count);
            CollectionAssert.AreEqual(definition.Textures.ToArray(), output.Take(definition.Textures.Count).Select(p => p.Sprite.TextureName).ToArray());
            Assert.IsTrue(output.All(p => p.WorldX == 10 && p.WorldY == 20 && p.GroundAligned && !p.Additive && !p.SourceColorOnly));
            Assert.IsTrue(output.All(p => p.Color == 0xEBFFFFFF && p.Rotation == -.1f));
            Assert.AreEqual(100 - output.Count, world.Objects[500].CurrentHitPoints);
            fx.Synchronize(world, _ => throw new Exception("Captured root must not follow subsequent victim movement"));
            output.Clear(); fx.Advance(catalogue, 2, output);
            Assert.AreEqual(0, output.Count); Assert.AreEqual(0, fx.ActiveCount);
        }
    }

    [TestMethod]
    public void PacketGateIsIndependentOfDamageAndDoesNotReplayAbsoluteHealthUpdates()
    {
        var world = World(); var fx = new WorldCombatFxSystem(); var output = new List<WorldParticle>();
        world.Apply(Event(0x17A, Words(500, 12, 0, 2, 0, 0)));
        world.Apply(Event(0x128, Words(500, 0)));
        world.Apply(Event(0x17A, Words(500, 0, 0, 2, 1, 2)));
        fx.Synchronize(world, snapshot => Anchor(snapshot, Vector2.Zero)); fx.Advance(Catalogue, 0, output);
        Assert.AreEqual(1, output.Count, "Enabled native creation still runs for zero damage and zero HP.");
        Assert.AreEqual("FXGROUND_FEUER.TGA", output[0].Sprite.TextureName);
        output.Clear(); fx.Synchronize(world, _ => null); fx.Advance(Catalogue, 0, output);
        Assert.AreEqual(1L, fx.StartedCount); Assert.AreEqual(0, world.Objects[500].CurrentHitPoints);
        world.Clear(); fx.Synchronize(world, _ => null); Assert.AreEqual(0, fx.ActiveCount);
    }

    [TestMethod]
    public void WaitingHitCannotAttachToReplacementAndLateAssetsDoNotRestartItsClock()
    {
        var world = World(); var fx = new WorldCombatFxSystem(); var output = new List<WorldParticle>();
        world.Apply(Event(0x17A, Words(500, 1, 0, 2, 1, 0)));
        fx.Synchronize(world, _ => null); fx.Advance(Catalogue, 1, output); Assert.AreEqual(0, output.Count);
        fx.Synchronize(world, snapshot => Anchor(snapshot, Vector2.Zero)); fx.Advance(Catalogue, 0, output);
        Assert.AreEqual(0x3FFFFFFFu, output.Single().Color);
        world.Apply(Event(0x17A, Words(500, 1, 0, 2, 1, 0)));
        fx.Synchronize(world, _ => null); world.Apply(Event(0x167, Words(500))); world.Apply(Actor());
        fx.Synchronize(world, _ => null);
        Assert.AreEqual(1, fx.ActiveCount, "Started flash survives, waiting flash cannot bind a reused reference.");
    }

    [TestMethod]
    public void GroundRingTracksDamageAbsoluteHpAndFadeAndSpecialFlagsPersistUntilDeath()
    {
        var world = World(elite: true, champion: true);
        var markers = new SacredNetworkCreatureMarkers();
        var focused = false;
        void Update(float seconds) => markers.Update(world, 2,
            [new(world.Objects[500], new(10, 20), 3, 40, focused)], Tuning, Levels, 1, seconds);
        Update(0); Assert.IsFalse(markers.Markers.Single().ShowHealth);
        world.Apply(Event(0x17A, Words(500, 25, 0, 2, 0, 0))); focused = true; Update(0);
        var state = markers.Markers.Single();
        Assert.IsTrue(state.Champion && state.Elite && state.ShowHealth);
        Assert.AreEqual(.75f, state.HealthFraction); Assert.AreEqual(20f, state.Radius);
        world.Apply(Event(0x128, Words(500, 50))); Update(0);
        Assert.AreEqual(.5f, markers.Markers.Single().HealthFraction);
        focused = false; Update(.75f); Assert.AreEqual(128u, markers.Markers.Single().HealthColor >> 24);
        Update(.25f); Assert.IsFalse(markers.Markers.Single().ShowHealth);
        var renderer = new WorldCreatureMarkerSystem { Markers = markers.Markers }; var output = new List<WorldParticle>();
        renderer.Advance(.1f, Catalogue.Projection, output);
        Assert.AreEqual(2, output.Count); Assert.IsTrue(output.All(p => p.GroundAligned && !p.Additive));
        world.Apply(Event(0x128, Words(500, 0))); Update(0); Assert.AreEqual(0, markers.Markers.Count);
    }

    [TestMethod]
    public void FocusRestoresRingAndReplacementDoesNotInheritCombatVisibility()
    {
        var world = World(); var markers = new SacredNetworkCreatureMarkers();
        void Update(bool focus) => markers.Update(world, 2, [new(world.Objects[500], new(), 0, 40, focus)], Tuning, Levels, 1, 0);
        Update(true); Assert.IsTrue(markers.Markers.Single().ShowHealth);
        var replacement = world.Objects[500].Creature!.ToPayload();
        world.Apply(Event(0x167, Words(500)));
        world.Apply(GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16B, 1, replacement))); Values(world);
        Update(false); Assert.AreEqual(0, markers.Markers.Count);
        Update(true); Assert.IsTrue(markers.Markers.Single().ShowHealth);
        world.Clear(); markers.Update(world, 2, [], Tuning, Levels, 1, 0); Assert.AreEqual(0, markers.Markers.Count);
    }

    [TestMethod]
    public void OrdinaryHitsEmitBloodWithoutOptionalFlashAndRespectDamageThreshold()
    {
        var world = World(); var blood = new WorldCombatBloodSystem(); var output = new List<WorldParticle>();
        world.Apply(Event(0x17A, Words(500, 1, 0, 2, 0, 0))); // max HP / 50 == 2
        world.Apply(Event(0x17A, Words(500, 2, 0, 2, 0, 0)));
        blood.Synchronize(world, snapshot => Anchor(snapshot, new(10, 20)), _ => 0xFF00AA00);
        blood.Advance(Catalogue, .01f, output);
        Assert.AreEqual(1L, blood.StartedCount);
        Assert.IsTrue(output.Count > 0, "Ordinary hits must create visible blood even when optional impact FX is disabled.");
        Assert.IsTrue(output.All(p => p.Sprite.TextureName == "PARTICLE_DOTS03.TGA" && (p.Color & 0xFFFFFF) == 0x00AA00));
        blood.Synchronize(world, snapshot => new(Vector2.Zero, 0, 40) { InstanceVersion = snapshot.InstanceVersion }, _ => null);
        output.Clear(); blood.Advance(Catalogue, .01f, output);
        Assert.IsTrue(output.Count > 0, "A temporarily unavailable evaluated bone must retain the preceding pose.");
        blood.Synchronize(world, snapshot => Anchor(snapshot, new(11, 20)), _ => null);
        output.Clear(); blood.Advance(Catalogue, .01f, output);
        Assert.IsTrue(output.All(p => Math.Abs(p.WorldX - 11) < 2));
        Assert.AreEqual(97, world.Objects[500].CurrentHitPoints);
        output.Clear();
        for (var i = 0; i < 100; i++) { output.Clear(); blood.Advance(Catalogue, .1f, output); }
        Assert.AreEqual(0, blood.ActiveCount, "A one-shot blood burst must drain without retaining its victim.");
        world.Clear(); blood.Synchronize(world, _ => null, _ => null); Assert.AreEqual(0, blood.ActiveCount);
    }

    private static WorldObjectFxAttachment Anchor(GoldReplicatedObject snapshot, Vector2 position) =>
        new(position, 0, 40) { InstanceVersion = snapshot.InstanceVersion, BoneAttachment = bone =>
            bone == 0 ? new(position, 0, 40) { InstanceVersion = snapshot.InstanceVersion } : null };

    [TestMethod]
    public void DamageAndRemovalInSameBatchRetainTheStruckInstancesLastPose()
    {
        var world = World(); var flash = new WorldCombatFxSystem(); var blood = new WorldCombatBloodSystem();
        var subject = world.Objects[500];
        world.Apply(Event(0x17A, Words(500, 25, 0, 2, 1, 0)));
        world.Apply(Event(0x167, Words(500)));
        WorldObjectFxAttachment? OldPose(GoldReplicatedObject snapshot) =>
            snapshot.InstanceVersion == subject.InstanceVersion ? Anchor(snapshot, new(10, 20)) : null;
        flash.Synchronize(world, OldPose); blood.Synchronize(world, OldPose, _ => 0xFFAA0000);
        // Scene model retirement occurs between these two synchronization passes.
        flash.Synchronize(world, _ => null); blood.Synchronize(world, _ => null, _ => null);
        var output = new List<WorldParticle>();
        flash.Advance(Catalogue, .01f, output); blood.Advance(Catalogue, .01f, output);
        Assert.AreEqual(1L, flash.StartedCount); Assert.AreEqual(1L, blood.StartedCount);
        Assert.IsTrue(output.Any(p => p.Sprite.TextureName == "FXGROUND_PHYSISCH.TGA"));
        Assert.IsTrue(output.Any(p => p.Sprite.TextureName == "PARTICLE_DOTS03.TGA"));
        world.Apply(Actor());
        flash.Synchronize(world, _ => throw new Exception("Old root must not rebind to replacement"));
        blood.Synchronize(world, _ => throw new Exception("Old blood must not follow replacement"), _ => null);
        for (var i = 0; i < 100; i++) { output.Clear(); flash.Advance(Catalogue, .1f, output); blood.Advance(Catalogue, .1f, output); }
        Assert.AreEqual(0, flash.ActiveCount); Assert.AreEqual(0, blood.ActiveCount);
    }
    private static GoldWorldReplica World(bool elite = false, bool champion = false)
    {
        var world = new GoldWorldReplica(); var actor = (GoldObjectEvent)Actor(); var bytes = actor.ObjectState.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.RelationClassOffset), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.Word1F4Offset), elite ? GoldCreatureStateLayout.EliteGroundSymbolFlag : 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldPhysicalStateLayout.WordB0Offset), champion ? GoldPhysicalStateLayout.ChampionGroundSymbolFlag : 0);
        world.Apply(GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16B, 1, bytes)));
        Values(world);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldObjectStateLayout.ReferenceOffset), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.RelationClassOffset), 1);
        world.Apply(GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16B, 1, bytes)));
        Values(world, reference: 2); return world;
    }
    private static void Values(GoldWorldReplica world, ushort level = 10, int hp = 100, uint reference = 500)
    {
        var bytes = new byte[GoldCreatureValuesLayout.CompactMinimumSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, GoldCreatureValuesLayout.Marker);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(GoldCreatureValuesLayout.MaximumHitPointsOffset), 100);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(GoldCreatureValuesLayout.CurrentHitPointsOffset), hp);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(GoldCreatureValuesLayout.CompactWord56Offset), level);
        world.Apply(GoldGameEventDecoder.Decode(GoldCreatureUpdateCodec.CreateValues(2, reference, 1,
            GoldCreatureValuesState.FromPayload(bytes, 0x11B))));
    }

    [TestMethod]
    public void FightModeMaintainsRingWithoutHitsAndValuesUpdatesRefreshHpAndLevelColor()
    {
        var world = World(); var markers = new SacredNetworkCreatureMarkers();
        // The local hero's level is a distinct replicated object.
        var hero = ((GoldObjectEvent)Actor()).ObjectState.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(hero.AsSpan(GoldObjectStateLayout.ReferenceOffset), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(hero.AsSpan(GoldCreatureStateLayout.RelationClassOffset), 1);
        world.Apply(GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16B, 2, hero)));
        Values(world, level: 30, reference: 2);
        var focused = true;
        void Update(float seconds) => markers.Update(world, 2, [new(world.Objects[500], new(10, 20), 0, 40, focused)], Tuning, Levels, 1, seconds);
        world.Apply(Event(0x119, Words(500, GoldCreatureStateLayout.FightModeFlag)));
        Update(10); Assert.IsTrue(markers.Markers.Single().ShowHealth);
        Assert.AreEqual(0xFF808080u, markers.Markers.Single().HealthColor);
        Values(world, 30, 50); Update(10);
        Assert.AreEqual(.5f, markers.Markers.Single().HealthFraction);
        Assert.AreEqual(0xFFDCF00Fu, markers.Markers.Single().HealthColor);
        Values(world, 50, 25); Update(10);
        Assert.AreEqual(.25f, markers.Markers.Single().HealthFraction);
        Assert.AreEqual(0xFFFF0000u, markers.Markers.Single().HealthColor);
        var renderer = new WorldCreatureMarkerSystem { Markers = markers.Markers }; var output = new List<WorldParticle>();
        renderer.Advance(0, Catalogue.Projection, output);
        Assert.IsTrue(output.All(p => p.UiBrightness && p.GroundPlaneDepth && p.GroundDecal));
        Assert.AreEqual(.25f, output.Single(p => p.HealthArc).Rotation);
        world.Apply(Event(0x119, Words(500, 0))); focused = false; Update(1); Assert.AreEqual(0, markers.Markers.Count);
    }

    [TestMethod]
    public void ZeroDamageCreatesIndependentGoldStreaksWithoutBloodOrOptionalFlash()
    {
        foreach (var quality in Enum.GetValues<SacredParticleQuality>())
        {
            var world = World(); var fx = new WorldCombatDeflectionSystem(); var output = new List<WorldParticle>();
            world.Apply(Event(0x17A, Words(500, 0, 0, 2, 0, 0)));
            world.Apply(Event(0x17A, Words(500, 0, 0, 2, 0, 0)));
            world.Apply(Event(0x17A, Words(500, 12, 0, 2, 0, 0)));
            fx.Synchronize(world, snapshot => Anchor(snapshot, new(10, 20)), _ => true);
            fx.Advance(SacredParticleCatalogue.LoadEmbedded(quality), .01f, output);
            Assert.AreEqual(2L, fx.StartedCount); Assert.AreEqual(80, output.Count);
            Assert.IsTrue(output.All(p => p.Untextured && p.CornerColors.HasValue && float.IsFinite(p.Rotation) && p.Size > 0));
            Assert.AreEqual(88, world.Objects[500].CurrentHitPoints);
            fx.Synchronize(world, snapshot => Anchor(snapshot, new(11, 20)), _ => true);
            output.Clear(); fx.Advance(Catalogue, .01f, output); Assert.AreEqual(2L, fx.StartedCount);
            output.Clear(); fx.Advance(Catalogue, .3f, output); Assert.AreEqual(0, output.Count); Assert.AreEqual(0, fx.ActiveCount);
        }
    }

    [TestMethod]
    public void DeflectionDoesNotBindReplacementOrReplayOutsideVisibleSectorAndClearsOnDisconnect()
    {
        var world = World(); var fx = new WorldCombatDeflectionSystem(); var output = new List<WorldParticle>();
        world.Apply(Event(0x17A, Words(500, 0, 0, 2, 0, 0)));
        fx.Synchronize(world, _ => null, _ => false); fx.Advance(Catalogue, 0, output);
        Assert.AreEqual(0, fx.ActiveCount);
        world.Apply(Event(0x17A, Words(500, 0, 0, 2, 0, 0)));
        fx.Synchronize(world, _ => null, _ => true);
        world.Apply(Event(0x167, Words(500))); world.Apply(Actor()); Values(world);
        fx.Synchronize(world, snapshot => new(Vector2.Zero, 0, 1) { InstanceVersion = world.Objects[500].InstanceVersion }, _ => true);
        Assert.AreEqual(0, fx.ActiveCount);
        world.Apply(Event(0x17A, Words(500, 0, 0, 2, 0, 0)));
        fx.Synchronize(world, snapshot => Anchor(snapshot, Vector2.Zero), _ => true); fx.Advance(Catalogue, 0, output);
        Assert.AreEqual(1L, fx.StartedCount);
        world.Clear(); fx.Synchronize(world, _ => null, _ => true); Assert.AreEqual(0, fx.ActiveCount);
    }

    [TestMethod]
    public void LateSkeletonCatchesUpDeflectionMotionAndDoesNotExtendItsLifetime()
    {
        var world = World(); var fx = new WorldCombatDeflectionSystem(); var output = new List<WorldParticle>();
        world.Apply(Event(0x17A, Words(500, 0, 0, 2, 0, 0)));
        fx.Synchronize(world, _ => null, _ => true); fx.Advance(Catalogue, .15f, output);
        Assert.AreEqual(0, output.Count);
        fx.Synchronize(world, snapshot => Anchor(snapshot, Vector2.Zero), _ => true); fx.Advance(Catalogue, 0, output);
        Assert.AreEqual(40, output.Count);
        Assert.IsTrue(output.Any(p => MathF.Abs(p.Height) > 10 ||
            new Vector2(p.WorldX, p.WorldY).Length() * SacredNetworkPosition.NativeUnitsPerTile > 10),
            "Delayed motion must already have travelled from the neck/root in its launch direction.");
        output.Clear(); fx.Advance(Catalogue, .11f, output); Assert.AreEqual(0, output.Count);
        Assert.AreEqual(0, fx.ActiveCount);
    }
}
