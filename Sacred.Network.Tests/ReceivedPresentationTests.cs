using System.Buffers.Binary;
using System.Collections;
using System.Collections.Frozen;
using System.Numerics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.GameRes;
using Sacred.GameplayHost.World;
using Sacred.Network.Gameplay;
using Sacred.Network.Tests.Infrastructure;
using Sacred.Particles;
using Sacred.World.Networking;
using Sacred.World.Particles;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class ReceivedPresentationTests
{
    [TestMethod]
    public void PuzzlePointBurstsUseWireSelectorsAndDistinctReceiptsActuallyEmit()
    {
        var world = new GoldWorldReplica(); world.Apply(Actor());
        var fx = new WorldReceivedFxSystem(); var particles = new List<WorldParticle>();
        for (var i = 0; i < 2; i++) world.Apply(Event(0x14D, Words(500, 300, 400, 20, 2)));
        fx.Synchronize(world, _ => null, _ => 0);
        fx.Advance(SacredParticleCatalogue.LoadEmbedded(), 0, particles);
        Assert.AreEqual(2L, fx.StartedCount); Assert.AreEqual(2, fx.ActiveCount);
        Assert.AreEqual(100, particles.Count, "Two independent native 50-particle radial bursts must reach the world renderer.");
        Assert.AreEqual(0, fx.Pending.Count);
        particles.Clear(); fx.Synchronize(world, _ => null, _ => 0);
        fx.Advance(SacredParticleCatalogue.LoadEmbedded(), .01f, particles);
        Assert.AreEqual(2L, fx.StartedCount);
        Assert.AreEqual(100, particles.Count);
        world.Clear(); fx.Synchronize(world, _ => null, _ => 0); Assert.AreEqual(0, fx.ActiveCount);
    }

    [TestMethod]
    public void RetiringPuzzleBurstCopiesActorMeshAndSurvivesSourceRetirement()
    {
        var world = new GoldWorldReplica(); world.Apply(Actor()); var snapshot = world.Objects[500];
        var vertices = Enumerable.Range(0, 2000).Select(i => new Vector3(i, 0, 0)).ToArray();
        var observed = new ObservedVertices(vertices);
        world.Apply(Event(0x14E, Words(500, 300, 400, 0)));
        var fx = new WorldReceivedFxSystem(); var particles = new List<WorldParticle>();
        fx.Synchronize(world, source =>
        {
            Assert.AreEqual(snapshot.InstanceVersion, source.InstanceVersion);
            return new(new Vector2(300, 400) / SacredNetworkPosition.NativeUnitsPerTile, 0, 20, observed)
            { InstanceVersion = source.InstanceVersion };
        }, _ => 0);
        CollectionAssert.AreEqual(Enumerable.Range(0, 500).Select(i => i * 4).ToArray(), observed.Reads.ToArray(),
            "Native mesh seeding samples 500 vertices with stride four, without skinning the unused vertices.");
        Array.Fill(vertices, new Vector3(float.NaN));
        fx.Advance(SacredParticleCatalogue.LoadEmbedded(), 0, particles);
        Assert.AreEqual(1L, fx.StartedCount); Assert.AreEqual(500, particles.Count);
        Assert.AreEqual(0, fx.Pending.Count);
        world.Apply(Actor()); // A reused reference must not rebind or restart the existing burst.
        fx.Synchronize(world, _ => throw new Exception("Started bursts must not resample the actor"), _ => 0);
        particles.Clear(); fx.Advance(SacredParticleCatalogue.LoadEmbedded(), .01f, particles);
        Assert.AreEqual(1L, fx.StartedCount); Assert.IsTrue(particles.Count > 0);
    }

    [TestMethod]
    public void MissingMeshWaitsWithoutBlockingIndependentPointBursts()
    {
        var world = new GoldWorldReplica(); world.Apply(Actor());
        world.Apply(Event(0x14D, Words(500, 300, 400, 0, 0)));
        world.Apply(Event(0x14D, Words(500, 300, 400, 0, 3)));
        var fx = new WorldReceivedFxSystem(); var particles = new List<WorldParticle>();
        fx.Synchronize(world, _ => null, _ => 0); fx.Advance(SacredParticleCatalogue.LoadEmbedded(), 0, particles);
        Assert.AreEqual(1L, fx.StartedCount); Assert.AreEqual(75, particles.Count); Assert.AreEqual(1, fx.Pending.Count);
        fx.Synchronize(world, source => new(new(), 0, 20, [Vector3.Zero]) { InstanceVersion = source.InstanceVersion }, _ => 0);
        particles.Clear(); fx.Advance(SacredParticleCatalogue.LoadEmbedded(), 0, particles);
        Assert.AreEqual(2L, fx.StartedCount); Assert.AreEqual(0, fx.Pending.Count);
    }

    [TestMethod]
    public void AttachedHitSparksAcceptInitialVectorWhenNativeBoneInitializationReplacesIt()
    {
        var world = new GoldWorldReplica(); world.Apply(Actor());
        world.Apply(Event(0x149, Words(0x35C, 76, 500, 0, BitConverter.SingleToUInt32Bits(12), 0, 0)));
        var fx = new WorldCreatureFxSystem(); var particles = new List<WorldParticle>();
        fx.Synchronize(world, (_, _) => new(new(10, 20), 100, 20) { InstanceVersion = world.Objects[500].InstanceVersion });
        fx.Advance(SacredParticleCatalogue.LoadEmbedded(), 0, particles);
        Assert.AreEqual(1, fx.ActiveCount); Assert.AreEqual(152, particles.Count); Assert.AreEqual(0, fx.Pending.Count);
        Assert.IsTrue(particles.All(p => Math.Abs(p.WorldX - 10) < 2 && Math.Abs(p.WorldY - 20) < 2));
    }

    [TestMethod]
    public void BurningBoneUsesHeadAttachmentAndDrainsWhenWearerRetires()
    {
        var world = new GoldWorldReplica(); world.Apply(Actor());
        world.Apply(Event(0x14A, Words(500, 300, 400, 0, 1)));
        var fx = new WorldReceivedFxSystem(); var particles = new List<WorldParticle>();
        var head = new Vector2(10, 20);
        WorldObjectFxAttachment? ActorInput(GoldReplicatedObject source) => new(new(), 0, 20)
        { InstanceVersion = source.InstanceVersion, BoneAttachment = bone =>
            bone == 20 ? new(head, 100, 20) { InstanceVersion = source.InstanceVersion } : null };
        fx.Synchronize(world, ActorInput, _ => 0); fx.Advance(SacredParticleCatalogue.LoadEmbedded(), .1f, particles);
        Assert.AreEqual(1L, fx.StartedCount); Assert.IsTrue(particles.Count > 0);
        Assert.IsTrue(particles.All(p => Math.Abs(p.WorldX - head.X) < 1 && Math.Abs(p.WorldY - head.Y) < 1));
        head += new Vector2(10, 10); particles.Clear(); fx.Synchronize(world, ActorInput, _ => 0);
        fx.Advance(SacredParticleCatalogue.LoadEmbedded(), .1f, particles);
        Assert.IsTrue(particles.All(p => Math.Abs(p.WorldX - head.X) < 1 && Math.Abs(p.WorldY - head.Y) < 1));
        world.Apply(Event(0x167, Words(500))); fx.Synchronize(world, ActorInput, _ => 0);
        for (var i = 0; i < 100; i++) { particles.Clear(); fx.Advance(SacredParticleCatalogue.LoadEmbedded(), .1f, particles); }
        Assert.AreEqual(0, fx.ActiveCount);
    }

    [TestMethod]
    public void BatchTerminalFreezeImpactEmitsAndChainWorkRemainsExplicit()
    {
        var world = new GoldWorldReplica(); world.Apply(Actor());
        var payload = RemainingReceivePackets.Batch();
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8 + 0x730), 500);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8 + 0x74C), 1);
        world.Apply(Event(0x181, payload));
        var fx = new WorldReceivedFxSystem(); var particles = new List<WorldParticle>();
        fx.Synchronize(world, _ => null, _ => 0); fx.Advance(SacredParticleCatalogue.LoadEmbedded(), 0, particles);
        Assert.AreEqual(1L, fx.StartedCount); Assert.IsTrue(particles.Count >= 15);
        Assert.IsTrue(world.Consumers.Reports.Any(r => r.Outcome == GoldConsumerOutcome.Rejected && r.Detail.Contains("chain")));
    }

    [TestMethod]
    public void ChatUsesReceiptTimeNamesNativePriorityIgnoreGatesAndExpiry()
    {
        var world = new GoldWorldReplica(); world.Session.ChannelChat.SetConnectionActive(true);
        var chat = new GameplayReceivedChat(world);
        byte[] Payload(string text) => [0, 0, 0, 0, ..Encoding.Unicode.GetBytes(text + '\0')];
        world.Apply(GoldGameEventDecoder.Decode(new(0x003, 0, 0, 0, Payload("Private server"))));
        world.Apply(GoldGameEventDecoder.Decode(new(0x004, 0, 2, 0, Payload("Team"))));
        world.Session.ChannelChat.SetPlayerIgnored(2, true);
        world.Apply(GoldGameEventDecoder.Decode(new(0x005, 0, 2, 0, Payload("Ignored"))));
        chat.Advance(0, GameResStore.Empty); Assert.AreEqual(0, chat.Lines.Count);
        chat.Advance(0, Resources()); Assert.AreEqual(2, chat.Lines.Count);
        Assert.AreEqual("Private Server: Private server", chat.Lines[0].Text);
        Assert.AreEqual(0xFFFF6347u, chat.Lines[0].ColorArgb);
        Assert.AreEqual(0xFFFFE400u, chat.Lines[1].ColorArgb);
        chat.Advance(0, Resources()); Assert.AreEqual(2, chat.Lines.Count);
        chat.Advance(8.1f, Resources()); Assert.AreEqual(0, chat.Lines.Count);
        world.Clear(); chat.Advance(0, Resources()); Assert.AreEqual(0, chat.Lines.Count);
    }

    [TestMethod]
    public void AllTextLineHudModesPreserveColorAndReceiptOrder()
    {
        var world = new GoldWorldReplica(); var hud = new GameplayReceivedHud(world);
        foreach (var mode in new uint[] { 1, 2, 3, 4, 16 }) world.Apply(Event(0x19E, Words(mode, 1, 42)));
        hud.Advance(0, Resources());
        CollectionAssert.AreEqual(new uint[] { 0xFFFFFFFF, 0xFF00A0FF, 0xFFFF6347, 0xFF00FF50, 0xFFFFFFFF }, hud.Notices.Select(n => n.ColorArgb).ToArray());
    }

    [TestMethod]
    public void AnimationCompletionUsesLoadedClipAndCannotMutateReusedReference()
    {
        var world = new GoldWorldReplica(); world.Apply(Actor(flags: GoldContainerActivationCodec.ActivatedFlag, container: true));
        world.Apply(Event(0x170, Words(500, 0xAA, 0x3F800000, 0x3F800000)));
        var request = world.ObjectPresentation.Pending.Single();
        Assert.IsTrue(world.CompleteObjectAnimation(request, 0xAA));
        Assert.AreEqual(0xAAu, world.Objects[500].Physical!.Word64);
        Assert.IsTrue(SacredNetworkObjectAnimation.IsActivated(world.Objects[500], world));
        world.Apply(Event(0x18D, Words(500)));
        Assert.IsTrue(world.CompleteObjectAnimation(world.ObjectPresentation.Pending.Last(), 0xAB));
        Assert.AreEqual(0xABu, world.Objects[500].Physical!.Word64);
        Assert.IsFalse(SacredNetworkObjectAnimation.IsActivated(world.Objects[500], world));
        world.Apply(Actor(container: true));
        Assert.IsFalse(world.CompleteObjectAnimation(request, 0xAA), "A newer full snapshot supersedes a waiting animation.");
        world.Apply(Event(0x167, Words(500))); world.Apply(Actor(container: true));
        Assert.IsFalse(world.CompleteObjectAnimation(request, 0xAB));
    }

    private static GameResStore Resources() => new(new Dictionary<uint, string>
    { [42] = "Notice", [SacredResourceHash.Compute("UI_NET_PRIVATE")] = "Private", [SacredResourceHash.Compute("UI_NET_TEAM")] = "Team",
        [SacredResourceHash.Compute("UI_NET_SERVER")] = "Server" }.ToFrozenDictionary());

    private sealed class ObservedVertices(Vector3[] vertices) : IReadOnlyList<Vector3>
    {
        public List<int> Reads { get; } = [];
        public int Count => vertices.Length;
        public Vector3 this[int index] { get { Reads.Add(index); return vertices[index]; } }
        public IEnumerator<Vector3> GetEnumerator() => ((IEnumerable<Vector3>)vertices).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
