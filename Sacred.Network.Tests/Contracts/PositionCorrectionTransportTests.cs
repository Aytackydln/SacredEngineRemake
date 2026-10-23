using System.Buffers.Binary;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Gameplay;
using Sacred.Network.Tests.Infrastructure;
using Sacred.World.Networking;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Packet")]
public sealed class PositionCorrectionTransportTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TcpCorrectionWakesWithoutResettingActionAndDefersOnlyWhenFlagged(bool deferred)
    {
        var world = Seed(0x40000 | (deferred ? 0x20000u : 0));
        var before = world.Objects[500];
        var playback = new SacredNetworkMovementPlayback(); playback.Update(before, 0);
        var message = GoldCreaturePositionCorrectionCodec.Create(2, 500, new(-1234, 5678, 257, 0x12345), 0x42b40000);
        Assert.AreEqual((ushort)0x179, message.Opcode);
        await using var peer = await GoldLoopbackPeer.StartAsync();
        await peer.SendAsync(message with { Payload = [..message.Payload, 0xa5] });
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        var actor = world.Objects[500];
        Assert.IsFalse(actor.Creature!.IsSleeping); Assert.IsNull(actor.Sleep);
        Assert.AreEqual(0u, BinaryPrimitives.ReadUInt32LittleEndian(actor.Physical!.ToPayload().AsSpan(GoldCreatureStateLayout.Word200Offset)) & GoldCreatureStateLayout.SleepingFlag);
        Assert.AreEqual(777, actor.CurrentHitPoints); Assert.AreEqual(before.AttackVersion, actor.AttackVersion);
        Assert.AreSame(before.Movement, actor.Movement); Assert.AreEqual(before.MovementVersion, actor.MovementVersion);
        var displayed = playback.Update(actor, 0);
        if (deferred)
        {
            Assert.AreEqual(before.BaseState, actor.BaseState);
            Assert.AreEqual(before.PositionVersion, actor.PositionVersion);
            Assert.AreEqual(new GoldObjectPosition(-1234, 5678, 1, 0x2345), actor.DeferredPosition!.Position);
            Assert.IsFalse(playback.Snapped);
        }
        else
        {
            Assert.IsNull(actor.DeferredPosition); Assert.AreEqual(-1234, actor.BaseState.X);
            Assert.AreEqual(0x42b40000u, actor.FacingBits);
            Assert.AreEqual(SacredNetworkPosition.ToTiles(actor.BaseState), displayed);
            Assert.IsTrue(playback.Snapped);
        }
        Assert.AreEqual(0, world.CreatureControls.PendingReplies);
    }

    [TestMethod]
    public async Task ShortCorrectionRejectsWholeTcpBatch()
    {
        var world = Seed(); var before = world.Objects[500]; var revision = world.Revision;
        await using var peer = await GoldLoopbackPeer.StartAsync();
        var valid = GoldCreaturePositionCorrectionCodec.Create(2, 500, new(1, 2, 3, 4), 0);
        await peer.SendAsync(valid, valid with { Payload = valid.Payload[..23] });
        var received = await peer.ReceiveAsync();
        Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(received.Messages, world.Apply));
        Assert.AreSame(before, world.Objects[500]); Assert.AreEqual(revision, world.Revision);
    }

    [TestMethod]
    public void DeferredCorrectionsReplaceOneSlotAndFullStateRetiresIt()
    {
        var world = Seed(0x60000); var before = world.Objects[500];
        void Receive(int x) => world.Apply(GoldGameEventDecoder.Decode(GoldCreaturePositionCorrectionCodec.Create(2, 500, new(x, 3, 4, 5), 0)));
        Receive(1); Receive(2);
        Assert.AreEqual(2, world.Objects[500].DeferredPosition!.Position.X);
        Assert.AreEqual(before.BaseState, world.Objects[500].BaseState);
        var replacement = Prefix();
        (GoldObjectBaseState.FromPayload(replacement) with { ObjectReference = 500 }).ToPayload().CopyTo(replacement, 0);
        world.Apply(GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16e, 2, replacement)));
        Assert.IsNull(world.Objects[500].DeferredPosition);
        world.Clear(); Assert.AreEqual(0, world.Objects.Count);
    }

    [TestMethod]
    public void ParsedMetadataCannotRedirectCorrectionToAnotherReference()
    {
        var world = Seed();
        var message = GoldCreaturePositionCorrectionCodec.Create(2, 500, new(200, 300, 0, 2), 0);
        world.Apply(new GoldObjectPositionEvent(message, 600, new(0, 0, 0, 0), uint.MaxValue));
        Assert.AreEqual(200, world.Objects[500].BaseState.X);
        Assert.IsFalse(world.Objects.ContainsKey(600));
        foreach (var slot in new byte[] { 0, 17, 255 })
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => GoldCreaturePositionCorrectionCodec.Create(slot, 500, new(0, 0, 0, 0), 0));
    }

    [TestMethod]
    public void CorrectionKeepsActivePathAndItsFractionalProgress()
    {
        var path = new GoldMovementPathPlayback();
        path.ReadPath([new(0, 0), new(1000, 0)], 0, new(0, 0), 10);
        path.Advance(.05f); // Half a native unit retained in Q16.
        path.ApplyCorrection(new(50, 60));
        Assert.AreEqual(new GoldMovementPoint(50, 60), path.Advance(0));
        Assert.IsTrue(path.Moving); Assert.AreEqual(10, path.Speed);
        Assert.AreEqual(new GoldMovementPoint(1, 0), path.Advance(.05f));
    }

    private static GoldWorldReplica Seed(uint flags = 0x1000)
    {
        var world = new GoldWorldReplica(); PositionCorrectionChecks.Seed(world, Prefix(), creatureFlags: flags); return world;
    }
    private static byte[] Prefix()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(RepositoryFiles.Evidence("native-creature-state.json")));
        return Convert.FromHexString(fixture.RootElement.GetProperty("samples")[0].GetProperty("payload_hex").GetString()!);
    }
}
