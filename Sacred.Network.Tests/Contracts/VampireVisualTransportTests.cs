using System.Numerics;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.CombatArts;
using Sacred.GameplayHost.World;
using Sacred.Granny.Animation;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;
using Sacred.Particles;
using Sacred.World.Geometry;
using Sacred.World.Particles;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Transport")]
public sealed class VampireVisualTransportTests
{
    private static readonly SacredParticleCatalogue Catalogue = SacredParticleCatalogue.LoadEmbedded();
    private static readonly MethodInfo Update = typeof(WorldAttachedParticleEffects).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static List<WorldParticle> Frame(WorldAttachedParticleEffects renderer)
    {
        var output = new List<WorldParticle>();
        Update.Invoke(renderer, [Catalogue, ParticleSimulationMode.CpuSimd, null, output, new List<WorldGpuParticleBatch>()]);
        return output;
    }
    private static GrnAnimationClip Clip()
    {
        var bones = new GrnSkeleton([
            new("__Root", 0, default, Quaternion.Identity, Matrix4x4.Identity, Matrix4x4.Identity, Matrix4x4.Identity),
            new("particle_source_01", 0, default, Quaternion.Identity, Matrix4x4.Identity, Matrix4x4.Identity, Matrix4x4.Identity)]);
        return new("fixture", bones, [null, new([0, 4], [new(60, 10, 30), new(20, 40, 90)], [], [], [], [])], 4);
    }
    private static WorldFxAnimations Animations() => new() { Load = _ => Task.FromResult<GrnAnimationClip?>(Clip()) };
    private static void Receive(GoldWorldReplica world, params GoldMessage[] messages) => GoldGameDispatcher.Dispatch(messages, world.Apply);

    [DataTestMethod, DataRow((ushort)1022), DataRow((ushort)1023)]
    public async Task TcpTransitionReleasesFourAnimatedChannelsAndDrains(ushort code)
    {
        await using var peer = await GoldLoopbackPeer.StartAsync();
        await peer.SendAsync(SeraphimVisualPackets.Creature(500), VampireVisualPackets.Special(code));
        var world = new GoldWorldReplica(); GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        var animations = Animations();
        var effects = new GameplayStatusEffects(world, _ => { }) { VisualLifetime = animations.Lifetime };
        effects.Advance(0);
        Assert.AreEqual(0, effects.Entries.Count);
        effects.SetActionTiming(world.Objects[500], 2); effects.Advance(.01f);
        Assert.AreEqual(4, effects.Entries.Count);
        Assert.IsTrue(effects.Entries.Values.All(e => e.VisualAvailable && e.PendingReason is null && e.Owner == 500));
        Assert.AreEqual(4, effects.Entries.Values.Select(e => e.Definition.Direction.X).Distinct().Count());
        Assert.IsTrue(effects.Entries.Values.All(e => e.Duration > 5 && e.Duration < 6));
        var renderer = new WorldAttachedParticleEffects { Animations = animations };
        var anchor = new WorldObjectFxAttachment(new(10, 20), 7, 50) { InstanceVersion = world.Objects[500].InstanceVersion };
        void Sync() => renderer.Synchronize(effects.Entries.Values.Select(e => GameplayStatusEffectParticles.Create(e, (_, _) => anchor, _ => 0)!));
        effects.Advance(.5f); Sync(); var particles = Frame(renderer);
        Assert.AreEqual(4, renderer.ActiveCount);
        Assert.AreEqual(4, particles.Count(p => p.Sprite.TextureName == "PARTICLE_GLOW03.TGA"));
        Assert.AreEqual(4, particles.Count(p => p.Sprite.TextureName == "PARTICLE_SPARK06.TGA"));
        Assert.IsTrue(particles.Count(p => p.Sprite.TextureName == "PARTICLE_MULTI01.TGA") > 100);
        var tips = particles.Where(p => p.Sprite.TextureName == "PARTICLE_GLOW03.TGA").ToArray();
        Assert.AreEqual(4, tips.Select(p => (p.WorldX, p.WorldY)).Distinct().Count());
        var mean = tips.Aggregate(Vector2.Zero, (sum, p) => sum + new Vector2(p.WorldX, p.WorldY)) / 4;
        Assert.AreEqual(10, mean.X, .001); Assert.AreEqual(20, mean.Y, .001);
        // Native 0x40 initializes this effect's origin once; walking afterwards leaves the burst in place.
        anchor = anchor with { Position = new(30, 40) }; Sync();
        var unchanged = Frame(renderer).Where(p => p.Sprite.TextureName == "PARTICLE_GLOW03.TGA").ToArray();
        CollectionAssert.AreEqual(tips, unchanged);
        effects.Advance(3.6f); Sync(); var tail = Frame(renderer);
        Assert.IsFalse(tail.Any(p => p.Sprite.TextureName == "PARTICLE_GLOW03.TGA"));
        Assert.IsTrue(tail.Count > 0);
        effects.Advance(2); Sync(); Assert.AreEqual(0, Frame(renderer).Count);
        Assert.AreEqual(0, effects.Entries.Count);
    }

    [TestMethod]
    public void RefreshCancellationAndOwnerReuseDoNotLeaveOldChannels()
    {
        var world = new GoldWorldReplica(); Receive(world, SeraphimVisualPackets.Creature(500));
        var effects = new GameplayStatusEffects(world, _ => { }) { VisualLifetime = Animations().Lifetime };
        effects.Apply(500, 0, SacredCombatArtKind.SpecialMove, 1022);
        effects.Apply(500, 0, SacredCombatArtKind.SpecialMove, 1022);
        Assert.AreEqual(4, effects.Entries.Count);
        effects.Clear(); Receive(world, VampireVisualPackets.Special(1023)); effects.Advance(0);
        effects.SetActionTiming(world.Objects[500], 2);
        Receive(world, new GoldMessage(0x164, 0, 1, 22, BitConverter.GetBytes(500u))); effects.Advance(3);
        Assert.AreEqual(0, effects.Entries.Count); Assert.AreEqual(0, effects.PendingActions);
        effects.Apply(500, 0, SacredCombatArtKind.SpecialMove, 1023);
        Receive(world, SeraphimVisualPackets.Remove(500), SeraphimVisualPackets.Creature(500)); effects.Advance(0);
        Assert.AreEqual(0, effects.Entries.Count);
    }

    [TestMethod]
    public void DelayedHelperLoadingCatchesUpAndKeepsNativeHeading()
    {
        var pending = new TaskCompletionSource<GrnAnimationClip?>();
        var renderer = new WorldAttachedParticleEffects { Animations = new() { Load = _ => pending.Task } };
        var state = new WorldAttachedParticleEffect(-1, 863, 0, default, 0, .5f) { Direction = new(MathF.PI / 2, 0, 0) };
        renderer.Synchronize([state]); Assert.AreEqual(0, Frame(renderer).Count);
        pending.SetResult(Clip());
        var glow = Frame(renderer).Single(p => p.Sprite.TextureName == "PARTICLE_GLOW03.TGA");
        var position = GrnAnimationBoneSampler.SamplePosition(Clip(), "particle_source_01", 29f / 60);
        var native = new Vector3(position.Y, position.X, 0);
        var projected = IsometricProjection.IsoToWorld(Catalogue.Projection.Project(native));
        Assert.AreEqual(projected.X, glow.WorldX, .002); Assert.AreEqual(projected.Y, glow.WorldY, .002);
        renderer.Synchronize([state with { Age = 8 }]); Assert.AreEqual(0, Frame(renderer).Count); Assert.AreEqual(0, renderer.ActiveCount);
    }

    [DataTestMethod, DataRow((int)SacredParticleQuality.Low), DataRow((int)SacredParticleQuality.Medium), DataRow((int)SacredParticleQuality.High)]
    public void AllParticleVariantsFollowTheAnimatedSource(int quality)
    {
        var clip = Clip();
        clip.Tracks[1] = new([0, 4], [new(600, 100, 500), new(600, 100, 500)], [], [], [], []);
        var renderer = new WorldAttachedParticleEffects
            { Animations = new() { Load = _ => Task.FromResult<GrnAnimationClip?>(clip) } };
        renderer.Synchronize([new WorldAttachedParticleEffect(-1, 863, 0, default, 0, .5f)]);
        var output = new List<WorldParticle>();
        var catalogue = SacredParticleCatalogue.LoadEmbedded((SacredParticleQuality)quality);
        Update.Invoke(renderer, [catalogue, ParticleSimulationMode.CpuSimd, null, output, new List<WorldGpuParticleBatch>()]);
        var particles = output.Where(p => p.Sprite.TextureName == "PARTICLE_MULTI01.TGA").ToArray();
        Assert.IsTrue(particles.Any(p => p.AtlasCell == 1));
        Assert.IsTrue(particles.Any(p => p.AtlasCell == 2));
        var sourceHeight = 100 * catalogue.Projection.HeightFactor * catalogue.Projection.VerticalScale;
        // The native initial count-mode birth can use a variant's authored zero
        // offset. Every later timed birth must use the moving shared release point.
        Assert.IsTrue(particles.Count(p => p.Height < sourceHeight) <= 1,
            "Secondary variants must travel with the source instead of accumulating at the wearer's feet.");
    }
}
