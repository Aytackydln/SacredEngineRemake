using System.Buffers.Binary;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Gameplay;
using Sacred.Particles;
using Sacred.World.Networking;
using Sacred.World.Particles;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class MeleeDeflectionPredictionTests
{
    [TestMethod]
    public void ReceivedMeleeWithoutZeroDamagePacketCreatesOneNeckBurstAfterCompletion()
    {
        var (world, prediction, fx) = Fixture();
        void Tick(float seconds)
        {
            fx.Synchronize(world, Anchor, _ => true);
            prediction.Update(world, seconds, Anchor, _ => .5f, fx.EmitPredicted);
            fx.Synchronize(world, Anchor, _ => true);
        }
        Attack(world); Tick(.3f); Assert.AreEqual(0, fx.ActiveCount);
        Tick(.33f); Assert.AreEqual(1, fx.ActiveCount);
        var output = new List<WorldParticle>();
        var catalogue = SacredParticleCatalogue.LoadEmbedded();
        fx.Advance(catalogue, .01f, output);
        var heightScale = catalogue.Projection.HeightFactor * catalogue.Projection.VerticalScale;
        Assert.AreEqual(40, output.Count);
        Assert.IsTrue(output.Any(p => MathF.Abs(p.Height - 40 * heightScale) < 10 * heightScale));
        Assert.AreEqual(100, world.Objects[501].CurrentHitPoints);
        Tick(.5f); Assert.AreEqual(1L, prediction.PredictedCount);
    }

    [TestMethod]
    public void PositiveAndExplicitZeroDamageSuppressPredictedDuplicates()
    {
        foreach (uint amount in new uint[] { 0, 12 })
        {
            var (world, prediction, fx) = Fixture(); Attack(world);
            prediction.Update(world, .3f, Anchor, _ => .5f, fx.EmitPredicted);
            world.Apply(Event(0x17A, Words(501, amount, 0, 500, 0, 0)));
            prediction.Update(world, .4f, Anchor, _ => .5f, fx.EmitPredicted);
            Assert.AreEqual(0L, prediction.PredictedCount);
            fx.Synchronize(world, Anchor, _ => true);
            Assert.AreEqual(amount == 0 ? 1 : 0, fx.ActiveCount);
        }
    }

    [TestMethod]
    public void ZeroStaticBlockingRadiusStillAllowsCreatureContactAndCompletedSwingSurvivesIdle()
    {
        var (world, prediction, fx) = Fixture(); Attack(world);
        WorldObjectFxAttachment? Pose(GoldReplicatedObject subject) => Anchor(subject) with { BlockRadius = 0 };
        prediction.Update(world, .51f, Pose, _ => .5f, fx.EmitPredicted);
        world.Apply(Event(0x164, Words(500)));
        prediction.Update(world, .12f, Pose, _ => null, fx.EmitPredicted);
        Assert.AreEqual(1L, prediction.PredictedCount);
        Assert.AreEqual(1, fx.ActiveCount);
    }

    [TestMethod]
    public void AmbiguousBodiesWrongFacingOutOfReachAndBowDoNotInventMisses()
    {
        for (var scenario = 0; scenario < 4; scenario++)
        {
            var (world, prediction, fx) = Fixture();
            if (scenario == 0) Add(world, 502);
            Attack(world, scenario == 3);
            WorldObjectFxAttachment? Pose(GoldReplicatedObject subject) => Anchor(subject) with
            { Position = subject.BaseState.ObjectReference == 500 ? Vector2.Zero :
                scenario == 1 ? new(-1, 0) : scenario == 2 ? new(100, 0) : new(1, 0) };
            prediction.Update(world, 1, Pose, _ => .5f, fx.EmitPredicted);
            Assert.AreEqual(0, fx.ActiveCount, $"scenario {scenario}");
        }
    }

    [TestMethod]
    public void RemovedAttackerVictimOrSessionAndCanceledSwingCannotEmit()
    {
        for (var scenario = 0; scenario < 4; scenario++)
        {
            var (world, prediction, fx) = Fixture(); Attack(world);
            prediction.Update(world, .3f, Anchor, _ => .5f, fx.EmitPredicted);
            if (scenario < 2) world.Apply(Event(0x167, Words((uint)(500 + scenario))));
            else if (scenario == 2) world.Clear();
            else world.Apply(Event(0x164, Words(500)));
            prediction.Update(world, scenario == 3 ? .1f : .4f, Anchor, _ => .5f, fx.EmitPredicted);
            Assert.AreEqual(0, fx.ActiveCount, $"scenario {scenario}");
        }
    }

    private static (GoldWorldReplica, WorldMeleeDeflectionPrediction, WorldCombatDeflectionSystem) Fixture()
    { var world = new GoldWorldReplica(); Add(world, 500); Add(world, 501); return (world, new(), new()); }

    private static void Add(GoldWorldReplica world, uint reference)
    {
        var bytes = ((GoldObjectEvent)Actor()).ObjectState.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldObjectStateLayout.ReferenceOffset), reference);
        world.Apply(GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16B, 1, bytes)));
        var values = new byte[GoldCreatureValuesLayout.CompactMinimumSize];
        BinaryPrimitives.WriteUInt32LittleEndian(values, GoldCreatureValuesLayout.Marker);
        BinaryPrimitives.WriteInt32LittleEndian(values.AsSpan(GoldCreatureValuesLayout.CurrentHitPointsOffset), 100);
        world.Apply(GoldGameEventDecoder.Decode(GoldCreatureUpdateCodec.CreateValues(1, reference, 1,
            GoldCreatureValuesState.FromPayload(values, 0x11B))));
    }

    private static void Attack(GoldWorldReplica world, bool bow = false)
    {
        var words = Words(500, 0, 0, 0, 0, 114, 0x3F800000, SacredNetworkFacing.FromTileDirection(Vector2.UnitX));
        world.Apply(Event(bow ? (ushort)0x17B : (ushort)0x17C, bow ? words.Concat(new byte[4]).ToArray() : words));
    }

    private static WorldObjectFxAttachment Anchor(GoldReplicatedObject subject) =>
        new(subject.BaseState.ObjectReference == 500 ? Vector2.Zero : Vector2.UnitX, 0, 40)
        { InstanceVersion = subject.InstanceVersion, BoneAttachment = bone => bone == 3
            ? new(Vector2.UnitX, 40, 40) { InstanceVersion = subject.InstanceVersion } : null };
}
