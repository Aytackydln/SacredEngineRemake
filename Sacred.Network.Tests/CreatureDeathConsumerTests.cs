using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Particles;
using Sacred.World.Networking;
using Sacred.World.Particles;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

namespace Sacred.Network.Tests;

[TestClass,TestCategory("Packet")]
public sealed class CreatureDeathConsumerTests
{
    [TestMethod]
    public void DeathUsesDecodedOperandStopsCombatSetsHpAndKeepsCorpseAliveUntilRemoval()
    {
        var world = new GoldWorldReplica(); world.Apply(Actor());
        world.Apply(Event(0x17C,Words(500,300,400,0,2581,84,0x3F800000,0)));
        var instance = world.Objects[500].InstanceVersion;
        var death = new GoldMessage(0x17D,0,2,0,Words(500,77));
        world.Apply(new GoldCreatureDeathCommandEvent(death,999,999));
        var corpse=world.Objects[500];
        Assert.IsTrue(corpse.IsDead); Assert.AreEqual(0,corpse.CurrentHitPoints);
        Assert.IsNull(corpse.Attack); Assert.IsNull(corpse.Movement);
        Assert.AreEqual(77u,corpse.Death!.PlayerWord); Assert.AreEqual(instance,corpse.InstanceVersion);
        Assert.AreNotEqual(0u,corpse.Physical!.WordB0 & GoldPhysicalStateLayout.CorpseFlag);
        Assert.AreEqual(0u,corpse.BaseState.Flags & 0x10);
        world.Apply(Event(0x17D,Words(500,77))); Assert.AreEqual(2L,world.Objects[500].DeathVersion);
        world.Apply(Event(0x128,Words(500,60))); Assert.IsFalse(world.Objects[500].IsDead);
        world.Apply(Event(0x167,Words(500))); world.Apply(Actor());
        Assert.IsNull(world.Objects[500].Death); Assert.AreNotEqual(instance,world.Objects[500].InstanceVersion);
    }

    [TestMethod]
    public void CorpseFxCopiesPoseOnceWithoutRetiringReferenceAndDrains()
    {
        var world = new GoldWorldReplica(); world.Apply(Actor()); world.Apply(Event(0x17D,Words(500,2)));
        var effects=new WorldReceivedFxSystem(); effects.Synchronize(world,_=>null,_=>0);
        var vertices=Enumerable.Range(0,2000).Select(i=>new Vector3(i,0,-20)).ToArray();
        var actor=world.Objects[500];
        Assert.IsTrue(effects.EmitCorpseDisappearance(actor,new(new(10,20),30,0,vertices){InstanceVersion=actor.InstanceVersion}));
        Array.Fill(vertices,new Vector3(float.NaN));
        var particles=new List<WorldParticle>(); effects.Advance(SacredParticleCatalogue.LoadEmbedded(),0,particles);
        Assert.AreEqual(500,particles.Count); Assert.IsTrue(particles.All(p=>float.IsFinite(p.WorldX) && float.IsFinite(p.Height)));
        Assert.AreEqual(0u,world.Objects[500].BaseState.Flags & 0x10);
        Assert.AreEqual(1L,effects.StartedCount);
        particles.Clear(); effects.Advance(SacredParticleCatalogue.LoadEmbedded(),10,particles);
        Assert.AreEqual(0,effects.ActiveCount);
        world.Clear(); effects.Synchronize(world,_=>null,_=>0); Assert.AreEqual(0L,effects.StartedCount);
    }

    [TestMethod]
    public void RetiringMeshRequestsExposeTheirOriginalInstanceUntilPoseArrives()
    {
        var world=new GoldWorldReplica(); world.Apply(Actor()); var source=world.Objects[500];
        world.Apply(Event(0x14E,Words(500,300,400,0)));
        var effects=new WorldReceivedFxSystem(); effects.Synchronize(world,_=>null,_=>0);
        Assert.AreEqual(source.InstanceVersion,effects.AwaitingActors.Single().InstanceVersion);
        effects.Synchronize(world,s=>new(new(),0,0,[Vector3.Zero]){InstanceVersion=s.InstanceVersion},_=>0);
        Assert.AreEqual(0,effects.AwaitingActors.Count());
    }

    [TestMethod]
    public void OrdinaryCleanupExcludesPlayersProtectedCorpsesAndLiveActors()
    {
        var world=new GoldWorldReplica(); world.Apply(Actor());
        Assert.IsFalse(SacredNetworkCorpseRules.CanDisappear(world.Objects[500],2));
        world.Apply(Event(0x17D,Words(500,2))); var corpse=world.Objects[500];
        Assert.IsTrue(SacredNetworkCorpseRules.CanDisappear(corpse,2));
        Assert.IsFalse(SacredNetworkCorpseRules.CanDisappear(corpse,500));
        Assert.IsFalse(SacredNetworkCorpseRules.CanDisappear(corpse with { BaseState=corpse.BaseState with { Flags=0x10000000 } },2));
    }
}
