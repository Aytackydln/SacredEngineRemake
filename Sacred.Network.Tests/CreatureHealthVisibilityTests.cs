using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.GameBin.Balance;
using Sacred.Network.Gameplay;
using Sacred.Network.Tests.Infrastructure;
using Sacred.World.Networking;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class CreatureHealthVisibilityTests
{
    private static readonly SacredCreatureMarkerBalance Tuning = new(0xE0FF3030, 0xFFFF8030, .85f, 1.1f, 1);
    private static readonly SacredCreatureBalance Levels = new(
        Enumerable.Range(1, 5).Select(i => new SacredCreatureHealthLevel(i, 1)), Enumerable.Repeat(1f, 6),
        MemoryMarshal.Read<SacredSkillCurveLayout>(Words(0, 0x3F800000, 0x3F800000)), new int[6], new int[6]);

    [TestMethod]
    public void InitialHealthVisibilityMatchesOriginalNativePlayerAndEnemyGates()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RepositoryFiles.Evidence("native-health-visibility.json")));
        foreach (var row in doc.RootElement.GetProperty("visibility").EnumerateArray().Where(r => r.GetProperty("timer").GetSingle() == 0))
        {
            var world = World(); var display = new SacredNetworkCreatureMarkers();
            var reference = row.GetProperty("local").GetBoolean() ? 2u : row.GetProperty("hostile").GetBoolean() ? 500u : 501u;
            world.Apply(Event(0x119, Words(reference, row.GetProperty("fight").GetBoolean() ? GoldCreatureStateLayout.FightModeFlag : 0)));
            display.Update(world, 2, [new(world.Objects[reference], new(), 0, 40)], Tuning, Levels, 1, 0);
            Assert.AreEqual(row.GetProperty("show").GetBoolean(), display.Markers.Any(m => m.ShowHealth), row.ToString());
        }
    }

    [TestMethod]
    public void EnemyCombatAndDamageDoNotShowHealthWithoutHoverAndHoverFadesInOneSecond()
    {
        var world = World(); var display = new SacredNetworkCreatureMarkers();
        void Update(bool hover, float seconds = 0) => display.Update(world, 2, [new(world.Objects[500], new(), 0, 40, hover)], Tuning, Levels, 1, seconds);
        world.Apply(Event(0x119, Words(500, GoldCreatureStateLayout.FightModeFlag)));
        world.Apply(Event(0x17C, Words(500,300,400,0,2581,84,0x3F800000,0)));
        world.Apply(Event(0x17A, Words(500,25,0,2,0,0)));
        Update(false); Assert.AreEqual(0, display.Markers.Count);
        Update(true); Assert.AreEqual(.75f, display.Markers.Single().HealthFraction);
        Update(true, 10); Assert.AreEqual(255u, display.Markers.Single().HealthColor >> 24);
        Update(false, .75f); Assert.AreEqual(128u, display.Markers.Single().HealthColor >> 24);
        Update(false, .25f); Assert.AreEqual(0, display.Markers.Count);
        world.Apply(Event(0x17A, Words(500,1,0,2,0,0)));
        Update(false); Assert.AreEqual(0, display.Markers.Count);
    }

    [TestMethod]
    public void FriendlyNpcAndIdlePlayerHoverStayHiddenWhileLocalCombatKeepsItsOwnRingVisible()
    {
        var world = World(); var display = new SacredNetworkCreatureMarkers();
        void Update(float seconds = 0) => display.Update(world, 2,
            [new(world.Objects[2], new(), 0, 40, true), new(world.Objects[501], new(), 0, 40, true)], Tuning, Levels, 1, seconds);
        Update(); Assert.AreEqual(0, display.Markers.Count);
        world.Apply(Event(0x119, Words(2, GoldCreatureStateLayout.FightModeFlag)));
        Update(10); Assert.AreEqual(2u, display.Markers.Single().Reference);
        Update(10); Assert.IsTrue(display.Markers.Single().ShowHealth);
        world.Apply(Event(0x119, Words(2, 0)));
        Update(.75f); Assert.AreEqual(128u, display.Markers.Single().HealthColor >> 24);
        Update(.25f); Assert.AreEqual(0, display.Markers.Count);
    }

    [TestMethod]
    public void HealthUpdatesDoNotCreateVisibilityAndDeadOrHiddenHoveredActorsHaveNoRing()
    {
        var world = World(); var display = new SacredNetworkCreatureMarkers();
        void Update(bool hover) => display.Update(world, 2, [new(world.Objects[500], Vector2.Zero, 0, 40, hover)], Tuning, Levels, 1, 0);
        world.Apply(Event(0x128, Words(500, 50))); Update(false); Assert.AreEqual(0, display.Markers.Count);
        Update(true); Assert.AreEqual(.5f, display.Markers.Single().HealthFraction);
        world.Apply(Event(0x17D, Words(500, 2))); Update(true); Assert.AreEqual(0, display.Markers.Count);
        world.Apply(Event(0x128, Words(500, 50)));
        var bytes = world.Objects[500].Creature!.ToPayload();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldObjectStateLayout.FlagsOffset), GoldObjectStateLayout.HiddenFlag);
        world.Apply(GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16B, 2, bytes)));
        Update(true); Assert.AreEqual(0, display.Markers.Count);
    }

    private static GoldWorldReplica World()
    {
        var world = new GoldWorldReplica();
        foreach (var (reference, relation) in new (uint, uint)[] { (2,1), (500,2), (501,3) })
        {
            var bytes = ((GoldObjectEvent)Actor()).ObjectState.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldObjectStateLayout.ReferenceOffset), reference);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.RelationClassOffset), relation);
            world.Apply(GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16B, 2, bytes)));
            var values = new byte[GoldCreatureValuesLayout.CompactMinimumSize];
            BinaryPrimitives.WriteUInt32LittleEndian(values, GoldCreatureValuesLayout.Marker);
            BinaryPrimitives.WriteInt32LittleEndian(values.AsSpan(GoldCreatureValuesLayout.MaximumHitPointsOffset), 100);
            BinaryPrimitives.WriteInt32LittleEndian(values.AsSpan(GoldCreatureValuesLayout.CurrentHitPointsOffset), 100);
            BinaryPrimitives.WriteUInt16LittleEndian(values.AsSpan(GoldCreatureValuesLayout.CompactWord56Offset), 10);
            world.Apply(GoldGameEventDecoder.Decode(GoldCreatureUpdateCodec.CreateValues(2, reference, 1, GoldCreatureValuesState.FromPayload(values, 0x11B))));
        }
        return world;
    }
}
