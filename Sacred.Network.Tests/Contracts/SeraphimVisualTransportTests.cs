using System.Numerics;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.CombatArts;
using Sacred.GameplayHost.World;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;
using Sacred.Particles;
using Sacred.World.Particles;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Transport")]
public sealed class SeraphimVisualTransportTests
{
    private static readonly SacredParticleCatalogue Catalogue = SacredParticleCatalogue.LoadEmbedded();
    private static readonly MethodInfo AttachedUpdate = typeof(WorldAttachedParticleEffects).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo ObjectUpdate = typeof(WorldObjectFxSystem).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static void Receive(GoldWorldReplica world, params GoldMessage[] messages) => GoldGameDispatcher.Dispatch(messages, world.Apply);
    private static List<WorldParticle> Frame(WorldAttachedParticleEffects effects)
    { var output = new List<WorldParticle>(); AttachedUpdate.Invoke(effects, [Catalogue, ParticleSimulationMode.CpuSimd, null, output, new List<WorldGpuParticleBatch>()]); return output; }
    private static List<WorldParticle> Frame(WorldObjectFxSystem effects, float seconds)
    { var output = new List<WorldParticle>(); ObjectUpdate.Invoke(effects, [Catalogue, seconds, ParticleSimulationMode.CpuSimd, null, output, new List<WorldGpuParticleBatch>()]); return output; }
    private static GameplayStatusEffects Effects(GoldWorldReplica world) => new(world, _ => { });
    private static void Release(GameplayStatusEffects effects, GoldWorldReplica world, float duration = 2)
    { effects.Advance(0); effects.SetActionTiming(world.Objects[500], duration); effects.Advance(duration); }
    private static WorldObjectFxAttachment Anchor(GoldWorldReplica world, Vector2 position) =>
        new(position, 7, 0) { InstanceVersion = world.Objects[501].InstanceVersion };

    [DataTestMethod, DataRow(15u, 0u), DataRow(17u, 501u), DataRow(17u, 0u)]
    public async Task TcpCastReleasesAndRendersNativeGroundOrWearerVisual(uint code, uint target)
    {
        await using var peer = await GoldLoopbackPeer.StartAsync();
        var world = new GoldWorldReplica();
        await peer.SendAsync(SeraphimVisualPackets.Creature(500), SeraphimVisualPackets.Creature(501), SeraphimVisualPackets.Cast(code, target));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        var effects = Effects(world); effects.Advance(0); effects.SetActionTiming(world.Objects[500], 2);
        var release = SacredCombatVisualCatalogue.Find(SacredCombatArtKind.Spell, code).Single().ReleaseProgress * 2;
        effects.Advance(release - .01f); Assert.AreEqual(0, effects.Entries.Count);
        effects.Advance(.02f);
        var effect = effects.Entries.Values.Single();
        Assert.IsTrue(effect.VisualAvailable); Assert.IsNull(effect.PendingReason);
        Assert.AreEqual(code == 15 ? 25f : 45f, effect.Duration);
        Assert.AreEqual(code == 15 ? 0u : target == 0 ? 500u : 501u, effect.Owner);
        var anchor = new WorldObjectFxAttachment(new(10, 20), 7, 0) { InstanceVersion = effect.OwnerInstance };
        var state = GameplayStatusEffectParticles.Create(effect, (_, _) => anchor, _ => 13)!;
        Assert.AreEqual(code == 15 ? 13f : 7f, state.Height);
        if (code == 15)
        { Assert.AreEqual(180000, effect.GroundTarget!.Value.X); Assert.AreEqual(2581u, effect.GroundTarget.Value.Sector); }
        else Assert.AreEqual((uint?)0, state.ActorBlockRadius, "Zero is a supplied Items.pak radius and resolves to the native fallback of 50.");
        var renderer = new WorldAttachedParticleEffects(); renderer.Synchronize([state]);
        var particles = Frame(renderer);
        Assert.AreEqual(1, renderer.ActiveCount);
        if (code == 17) Assert.AreEqual(503, particles.Count);
        else
        {
            Assert.AreEqual(14, particles.Count(p => p.Untextured));
            Assert.IsTrue(particles.Count > 16, "Celestial Light must also emit its standard particles.");
        }
        var next = GameplayStatusEffectParticles.Create(effect, (_, _) => anchor with { Position = new(12, 23) }, _ => 13)!;
        renderer.Synchronize([next]); var moved = Frame(renderer);
        Assert.AreEqual(code == 15 ? 0 : 2, moved[0].WorldX - particles[0].WorldX, .0001f);
        Assert.AreEqual(code == 15 ? 0 : 3, moved[0].WorldY - particles[0].WorldY, .0001f);
        effects.Advance(46); Assert.AreEqual(0, effects.Entries.Count);
        renderer.Synchronize([]); Assert.AreEqual(0, Frame(renderer).Count);
    }

    [TestMethod]
    public void GroundCastsCoexistAndKeepTheirPositionAfterCasterRemoval()
    {
        var world = new GoldWorldReplica(); Receive(world, SeraphimVisualPackets.Creature(500));
        var effects = Effects(world);
        Receive(world, SeraphimVisualPackets.Cast(15, 0)); Release(effects, world);
        var first = effects.Entries.Values.Single();
        var second = SeraphimVisualPackets.Cast(15, 0);
        SeraphimVisualPackets.Word(second.Payload, 16, 180300);
        Receive(world, second); Release(effects, world);
        Assert.AreEqual(2, effects.Entries.Count);
        Assert.AreEqual(180000, first.GroundTarget!.Value.X);
        Assert.IsTrue(effects.Entries.Values.Any(e => e.GroundTarget!.Value.X == 180300));
        Receive(world, SeraphimVisualPackets.Remove(500)); effects.Advance(1);
        Assert.AreEqual(2, effects.Entries.Count);
        foreach (var effect in effects.Entries.Values)
            Assert.IsNotNull(GameplayStatusEffectParticles.Create(effect, (_, _) => throw new Exception("Ground effect requested a wearer"), _ => 0));
    }

    [TestMethod]
    public void AttachedRefreshAndTargetInstanceGuardsRemainIntact()
    {
        var world = new GoldWorldReplica(); Receive(world, SeraphimVisualPackets.Creature(500), SeraphimVisualPackets.Creature(501));
        var effects = Effects(world); Receive(world, SeraphimVisualPackets.Cast(17, 501)); Release(effects, world);
        var previous = effects.Entries.Keys.Single();
        Receive(world, SeraphimVisualPackets.Cast(17, 501)); Release(effects, world);
        Assert.AreEqual(1, effects.Entries.Count); Assert.IsFalse(effects.Entries.ContainsKey(previous));
        Receive(world, SeraphimVisualPackets.Remove(500)); effects.Advance(0);
        Assert.AreEqual(1, effects.Entries.Count, "Released aura follows the surviving destination.");
        Receive(world, SeraphimVisualPackets.Remove(501), SeraphimVisualPackets.Creature(501)); effects.Advance(0);
        Assert.AreEqual(0, effects.Entries.Count);
    }

    [DataTestMethod, DataRow(15u, 0u), DataRow(17u, 501u)]
    public void SupersededActionDoesNotReleaseLate(uint code, uint target)
    {
        var world = new GoldWorldReplica(); Receive(world, SeraphimVisualPackets.Creature(500), SeraphimVisualPackets.Creature(501), SeraphimVisualPackets.Cast(code, target));
        var effects = Effects(world); effects.Advance(0); effects.SetActionTiming(world.Objects[500], 2);
        var idle = new byte[4]; SeraphimVisualPackets.Word(idle, 0, 500);
        Receive(world, new GoldMessage(0x164, 0, 1, 20, idle)); effects.Advance(3);
        Assert.AreEqual(0, effects.Entries.Count); Assert.AreEqual(0, effects.PendingActions);
    }

    [DataTestMethod, DataRow(804u, 35u, 0u), DataRow(814u, 41u, 501u)]
    public async Task TcpFullFxSnapshotUsesNativeArtInputsAndCleansUp(uint resource, uint subtype, uint wearer)
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        await peer.SendAsync(SeraphimVisualPackets.Creature(501), SeraphimVisualPackets.Fx(resource, subtype, wearer));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        var effects = new WorldObjectFxSystem();
        effects.Synchronize(world.Objects.Values, _ => Anchor(world, new(10, 20)), _ => 13);
        var particles = Frame(effects, .1f);
        Assert.AreEqual(1, effects.ActiveCount); Assert.AreEqual(0, effects.Pending.Count);
        Assert.AreEqual(resource == 814 ? 503 : 14, resource == 814 ? particles.Count : particles.Count(p => p.Untextured));
        if (resource == 804) Assert.IsTrue(particles.Count > 16, "Snapshot must start the native standard particle vector too.");
        effects.Synchronize(world.Objects.Values, _ => Anchor(world, new(12, 23)), _ => 13);
        var moved = Frame(effects, 0);
        Assert.AreEqual(resource == 814 ? 2f : 0f, moved[0].WorldX - particles[0].WorldX, .001f);
        Assert.AreEqual(resource == 814 ? 3f : 0f, moved[0].WorldY - particles[0].WorldY, .001f);
        Assert.IsTrue(Frame(effects, 2).Count > 0, "Object duration 1 must not replace the decoded CA lifetime.");
        Assert.AreEqual(0, Frame(effects, 46).Count); Assert.AreEqual(0, effects.ActiveCount);
        Receive(world, SeraphimVisualPackets.Remove(600)); effects.Synchronize(world.Objects.Values, _ => null);
        Assert.AreEqual(0, effects.Pending.Count);
    }

    [TestMethod]
    public void FullAuraNeverReattachesToReusedWearerReference()
    {
        var world = new GoldWorldReplica(); Receive(world, SeraphimVisualPackets.Creature(501), SeraphimVisualPackets.Fx(814, 41));
        var effects = new WorldObjectFxSystem();
        effects.Synchronize(world.Objects.Values, _ => Anchor(world, new(10, 20))); Assert.AreEqual(503, Frame(effects, .1f).Count);
        Receive(world, SeraphimVisualPackets.Remove(501), SeraphimVisualPackets.Creature(501));
        effects.Synchronize(world.Objects.Values, _ => Anchor(world, new(30, 40)));
        Assert.AreEqual(0, Frame(effects, .1f).Count); Assert.AreEqual(0, effects.ActiveCount);
    }
}
