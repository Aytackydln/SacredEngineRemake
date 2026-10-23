using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.GameplayHost.World;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Packet")]
public sealed class SessionPauseTransportTests
{
    [TestMethod]
    public async Task TcpServerPauseKeepsReceivingWithoutEchoAndResumeRestoresTime()
    {
        await using var peer = await GoldLoopbackPeer.StartAsync();
        var world = new GoldWorldReplica();
        await peer.SendAsync(Pause(0, [1, 2, 3]));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        Assert.IsTrue(world.Session.IsPaused); Assert.AreEqual((byte)0, world.Session.PausedBy);
        Assert.AreEqual(0f, world.Session.SimulationSeconds(10));
        await peer.SendAsync(new GoldMessage(0x1e, 0, 0, 0, [4, 0, 0, 0]), Pause(16));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        Assert.AreEqual(4u, world.Session.GameFlags); Assert.IsTrue(world.Session.IsLocked);
        Assert.AreEqual((byte)16, world.Session.PausedBy); Assert.AreEqual(2L, world.Session.PauseNotificationCount);
        await peer.SendAsync(Resume(16, [9, 8, 7]));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages, world.Apply);
        Assert.IsFalse(world.Session.IsPaused); Assert.IsNull(world.Session.PausedBy);
        Assert.AreEqual(0.125f, world.Session.SimulationSeconds(0.125f));
        Assert.IsFalse(peer.Connection.IsStopping); Assert.AreEqual(0L, peer.Connection.Diagnostics.SentMessages);
        Assert.AreEqual(0, world.CreatureControls.PendingReplies);
    }

    [TestMethod]
    public void EarlyPauseSurvivesAdmissionWithoutLosingWorldApprovalOrRoleFlags()
    {
        var tracker = new GoldAdmissionTracker(); tracker.ProcessBatch([Pause(0)]); tracker.Begin();
        Assert.IsTrue(tracker.Status.SessionPaused); Assert.AreEqual((byte)0, tracker.Status.PausedBy);
        tracker.ProcessBatch([new(0x11, 0, 0, 0, [4, 3, 0, 0, 0, 0]), new(0x17, 0, 0, 0, []), Pause(31)]);
        Assert.IsTrue(tracker.Status.SessionPaused); Assert.AreEqual((byte)31, tracker.Status.PausedBy);
        tracker.ProcessBatch([Resume(31)]);
        Assert.IsFalse(tracker.Status.SessionPaused); Assert.IsNull(tracker.Status.PausedBy);
        Assert.AreEqual((byte)4, tracker.Status.Flags); Assert.AreEqual((byte)3, tracker.Status.PlayerSlot);
        Assert.IsTrue(tracker.Status.WorldStartup.ParticipantStartApproved);
        Assert.IsTrue(tracker.Status.WorldStartup.HostWorldAvailable);
        Assert.IsFalse(tracker.Status.WorldStartup.AssignedClientHost);
        Assert.AreEqual(GoldAdmissionPhase.SlotAssigned, tracker.Status.Phase);
    }

    [TestMethod]
    public void WrongSenderOrMalformedLaterPacketRollsBackAdmissionAndReplicaBatch()
    {
        var tracker = new GoldAdmissionTracker(); tracker.ProcessBatch([Pause(2)]); var before = tracker.Status;
        foreach (var bad in new[] { Resume(2) with { Sender = 2 }, Resume(32), new GoldMessage(0x43, 0, 0, 0, [0]) })
        {
            Assert.ThrowsException<InvalidDataException>(() => tracker.ProcessBatch([Resume(0), bad]));
            Assert.AreSame(before, tracker.Status);
        }
        var world = new GoldWorldReplica();
        Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch([Pause(2), new(0x43, 0, 0, 0, [0])], world.Apply));
        Assert.IsNull(world.Session.IsPaused); Assert.AreEqual(0L, world.Session.PauseNotificationCount);
        Assert.AreEqual(0L, world.Revision);
    }

    [TestMethod]
    public void PausedSimulationHoldsTheRealThinkClockAndResumeDoesNotCatchUp()
    {
        var world = new GoldWorldReplica(); var clock = new GameplayCreatureThinkClock(1, 0.5f);
        Apply(world, Pause(0));
        for (var i = 0; i < 100; i++) Assert.IsFalse(clock.Advance(world.Session.SimulationSeconds(10), false));
        Assert.AreEqual(0.5f, clock.Elapsed);
        Apply(world, Resume(0));
        Assert.IsFalse(clock.Advance(world.Session.SimulationSeconds(0.5f), false));
        Assert.IsTrue(clock.Advance(world.Session.SimulationSeconds(0.125f), false));
        Assert.AreEqual(0.125f, clock.Elapsed);
    }

    [TestMethod]
    public void AuthoritativeObjectUpdatesStillApplyWhileSimulationIsPaused()
    {
        var world = new GoldWorldReplica(); Apply(world, Pause(0));
        Apply(world, PacketCatalog.All.First(row => row.Opcode == 0x16e).Message());
        Assert.IsTrue(world.Objects.ContainsKey(500));
        var revision = world.Revision;
        Apply(world, new(0x167, 0, 0, 0, BitConverter.GetBytes(500u)));
        Assert.IsTrue(world.Revision > revision); Assert.IsTrue(world.Session.IsPaused);
        Assert.AreEqual(0f, world.Session.SimulationSeconds(1));
    }

    [TestMethod]
    public void PayloadIsOwnedAndReducerRejectsUnrelatedOpcodes()
    {
        var bytes = new byte[] { 1, 2, 3 }; var entry = GoldGameEventDecoder.Decode(Pause(7, bytes));
        Array.Fill(bytes, (byte)0xcc); var world = new GoldWorldReplica(); world.Apply(entry);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, entry.Message.Payload);
        Assert.AreEqual((byte)7, world.Session.PausedBy);
        Assert.ThrowsException<InvalidDataException>(() => world.Apply(new GoldSessionPauseEvent(new(0xffff, 0, 0, 0, []))));
        Assert.AreEqual(1L, world.Revision);
    }

    [TestMethod]
    public void RepeatedControlsCountAndDisconnectClearsTheSimulationGate()
    {
        var world = new GoldWorldReplica();
        foreach (var message in new[] { Pause(2), Pause(2), Resume(0), Resume(0), Pause(0) }) Apply(world, message);
        Assert.IsTrue(world.Session.IsPaused); Assert.AreEqual(5L, world.Session.PauseNotificationCount);
        world.Clear(); Assert.IsNull(world.Session.IsPaused); Assert.IsNull(world.Session.PausedBy);
        Assert.AreEqual(0L, world.Session.PauseNotificationCount); Assert.AreEqual(0.25f, world.Session.SimulationSeconds(0.25f));
        Assert.IsNull(new GoldAdmissionTracker().Status.SessionPaused);
    }

    private static void Apply(GoldWorldReplica world, GoldMessage message) => world.Apply(GoldGameEventDecoder.Decode(message));
    private static GoldMessage Pause(byte origin, byte[]? bytes = null) => new(0x135, 0, origin, 0, bytes ?? []);
    private static GoldMessage Resume(byte origin, byte[]? bytes = null) => new(0x134, 0, origin, 0, bytes ?? []);
}
