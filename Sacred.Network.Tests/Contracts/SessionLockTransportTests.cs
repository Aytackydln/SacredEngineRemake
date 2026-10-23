using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Packet")]
public sealed class SessionLockTransportTests
{
    [TestMethod]
    [DataRow((ushort)0x019, false)]
    [DataRow((ushort)0x01a, true)]
    public async Task TcpNotificationAppliesIgnoredPayloadWithoutReplyOrDisconnect(ushort opcode, bool locked)
    {
        await using var peer = await GoldLoopbackPeer.StartAsync(); var world = new GoldWorldReplica();
        await peer.SendAsync(new GoldMessage(opcode,0,0,0,[0xde,0xad,0xbe,0xef]));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages,world.Apply);
        Assert.AreEqual(locked,world.Session.IsLocked);Assert.AreEqual(1L,world.Session.LockNotificationCount);
        Assert.IsFalse(peer.Connection.IsStopping);Assert.AreEqual(0L,peer.Connection.Diagnostics.SentMessages);
        Assert.AreEqual(0,world.CreatureControls.PendingReplies);
        await peer.SendAsync(new GoldMessage(opcode,0,0,1,[]));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages,world.Apply);
        Assert.AreEqual(2L,world.Session.LockNotificationCount);
    }
    [TestMethod]
    public void LockCanArriveBeforeSlotAssignmentWithoutLosingItsState()
    {
        var tracker=new GoldAdmissionTracker();
        tracker.ProcessBatch([Lock()]);Assert.AreEqual(GoldAdmissionPhase.NotRequested,tracker.Status.Phase);
        tracker.Begin();Assert.IsTrue(tracker.Status.SessionLocked);tracker.ProcessBatch([Admission()]);
        Assert.IsTrue(tracker.Status.SessionLocked);Assert.AreEqual(GoldAdmissionPhase.SlotAssigned,tracker.Status.Phase);
        tracker.ProcessBatch([Unlock()]);Assert.IsFalse(tracker.Status.SessionLocked);
        Assert.AreEqual((byte)4,tracker.Status.Flags);Assert.IsTrue(tracker.Status.WorldStartup.HostWorldAvailable);
        Assert.IsFalse(tracker.Status.WorldStartup.ParticipantStartApproved);
    }
    [TestMethod]
    public void InvalidIdentityOrLaterPacketRollsBackTheCompleteAdmissionBatch()
    {
        var tracker=new GoldAdmissionTracker();tracker.Begin();tracker.ProcessBatch([Admission(),Lock()]);var before=tracker.Status;
        foreach(var bad in new[]{Lock() with{Sender=3,Origin=3},Lock() with{Origin=3},new GoldMessage(0x11,0,0,0,[0])})
        {
            Assert.ThrowsException<InvalidDataException>(()=>tracker.ProcessBatch([Unlock(),bad]));
            Assert.AreSame(before,tracker.Status);Assert.IsTrue(tracker.Status.SessionLocked);
        }
    }
    [TestMethod]
    public void InvalidLaterPacketPreventsAnyReplicaNotification()
    {
        var world=new GoldWorldReplica();
        Assert.ThrowsException<InvalidDataException>(()=>GoldGameDispatcher.Dispatch([Lock(),new(0x43,0,0,0,[0])],world.Apply));
        Assert.IsNull(world.Session.IsLocked);Assert.AreEqual(0L,world.Session.LockNotificationCount);Assert.AreEqual(0L,world.Revision);
    }
    [TestMethod]
    public void RepeatedAndAlternatingNotificationsPreserveWorldEntryAndClearOnDisconnect()
    {
        var world=new GoldWorldReplica();var tracker=new GoldAdmissionTracker();tracker.Begin();tracker.ProcessBatch([Admission(),new(0x17,0,0,0,[])]);
        foreach(var message in new[]{Lock(),Lock(),Unlock(),Unlock(),Lock()})
        {
            tracker.ProcessBatch([message]);world.Apply(GoldGameEventDecoder.Decode(message));
        }
        Assert.IsTrue(world.Session.IsLocked);Assert.AreEqual(5L,world.Session.LockNotificationCount);
        Assert.IsTrue(tracker.Status.WorldStartup.ParticipantStartApproved);Assert.IsTrue(tracker.Status.SessionLocked);
        Assert.AreEqual(GoldAdmissionPhase.SlotAssigned,tracker.Status.Phase);
        world.Clear();Assert.IsNull(world.Session.IsLocked);Assert.AreEqual(0L,world.Session.LockNotificationCount);
        Assert.IsNull(new GoldAdmissionTracker().Status.SessionLocked);
    }
    [TestMethod]
    public void SessionEventRejectsAnUnrelatedOpcodeAtTheReducerBoundary()
    {
        var world=new GoldWorldReplica();
        Assert.ThrowsException<InvalidDataException>(()=>world.Apply(new GoldSessionLockEvent(new(0xffff,0,0,0,[]))));
        Assert.IsNull(world.Session.IsLocked);Assert.AreEqual(0L,world.Revision);
    }
    private static GoldMessage Admission()=>new(0x11,0,0,0,[4,3,0,0,0,0]);
    private static GoldMessage Lock()=>new(0x1a,0,0,0,[]);
    private static GoldMessage Unlock()=>new(0x19,0,0,0,[]);
}
