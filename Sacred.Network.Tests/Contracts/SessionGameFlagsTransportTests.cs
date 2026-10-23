using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests.Contracts;

[TestClass,TestCategory("Packet")]
public sealed class SessionGameFlagsTransportTests
{
    [TestMethod]
    public async Task TcpFullFlagsAndLockTogglesRemainConsistentWithoutReplyOrDisconnect()
    {
        await using var peer=await GoldLoopbackPeer.StartAsync();var world=new GoldWorldReplica();
        await peer.SendAsync(Flags(0x10,[1,2,3]),new GoldMessage(0x1a,0,0,1,[]));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages,world.Apply);
        Assert.AreEqual(0x14u,world.Session.GameFlags);Assert.IsTrue(world.Session.IsLocked);Assert.AreEqual((ushort)0,world.Session.NativeControlWord);
        await peer.SendAsync(Flags(0x40000),new GoldMessage(0x19,0,0,2,[]));
        GoldGameDispatcher.Dispatch((await peer.ReceiveAsync()).Messages,world.Apply);
        Assert.AreEqual(0x40000u,world.Session.GameFlags);Assert.IsFalse(world.Session.IsLocked);Assert.AreEqual((ushort)1,world.Session.NativeControlWord);
        Assert.AreEqual(2L,world.Session.GameFlagsNotificationCount);Assert.AreEqual(2L,world.Session.LockNotificationCount);
        Assert.IsFalse(peer.Connection.IsStopping);Assert.AreEqual(0L,peer.Connection.Diagnostics.SentMessages);
        Assert.AreEqual(0,world.CreatureControls.PendingReplies);
    }
    [TestMethod]
    public void FlagsSurviveFirstContactAndDoNotGrantOrClearWorldApproval()
    {
        var tracker=new GoldAdmissionTracker();tracker.ProcessBatch([Flags(uint.MaxValue)]);tracker.Begin();
        Assert.AreEqual(uint.MaxValue,tracker.Status.SessionGameFlags);
        tracker.ProcessBatch([new(0x11,0,0,0,[4,3,0,0,0,0]),new(0x17,0,0,0,[]),new(0x19,0,0,0,[])]);
        Assert.AreEqual(uint.MaxValue&~4u,tracker.Status.SessionGameFlags);Assert.IsFalse(tracker.Status.SessionLocked);
        tracker.ProcessBatch([Flags(0)]);Assert.IsFalse(tracker.Status.SessionLocked);
        Assert.AreEqual((byte)4,tracker.Status.Flags);Assert.IsTrue(tracker.Status.WorldStartup.HostWorldAvailable);
        Assert.IsTrue(tracker.Status.WorldStartup.ParticipantStartApproved);Assert.IsFalse(tracker.Status.WorldStartup.AssignedClientHost);
    }
    [TestMethod]
    public void InvalidIdentityOrShortWordRollsBackAllAdmissionAndReplicaChanges()
    {
        var tracker=new GoldAdmissionTracker();tracker.ProcessBatch([Flags(0x10)]);var before=tracker.Status;
        foreach(var bad in new[]{Flags(0)with{Sender=3,Origin=3},Flags(0)with{Origin=3},new GoldMessage(0x1e,0,0,0,[0,0,0])})
        {
            Assert.ThrowsException<InvalidDataException>(()=>tracker.ProcessBatch([Flags(4),bad]));Assert.AreSame(before,tracker.Status);
        }
        var world=new GoldWorldReplica();
        Assert.ThrowsException<InvalidDataException>(()=>GoldGameDispatcher.Dispatch([Flags(4),new(0x1e,0,0,0,[0])],world.Apply));
        Assert.IsNull(world.Session.GameFlags);Assert.IsNull(world.Session.IsLocked);Assert.AreEqual(0L,world.Revision);
    }
    [TestMethod]
    public void OwnedWireOverridesForgedFlagsAndRetainsIgnoredSuffix()
    {
        var message=Flags(0x10,[9,8,7]);var entry=GoldGameEventDecoder.Decode(message);Array.Fill(message.Payload,(byte)0xcc);
        var world=new GoldWorldReplica();world.Apply(entry);
        Assert.AreEqual(0x10u,world.Session.GameFlags);Assert.AreEqual((byte)7,entry.Message.Payload[^1]);
        world.Apply(new GoldSessionGameFlagsEvent(Flags(0x40004),0));
        Assert.AreEqual(0x40004u,world.Session.GameFlags);Assert.IsTrue(world.Session.IsLocked);
        Assert.AreEqual((ushort)1,world.Session.NativeControlWord);
    }
    [TestMethod]
    public void LockOnlyObservationDoesNotInventOtherFlagsAndFullFlagsReplaceIt()
    {
        var world=new GoldWorldReplica();var tracker=new GoldAdmissionTracker();var locked=new GoldMessage(0x1a,0,0,0,[]);
        world.Apply(GoldGameEventDecoder.Decode(locked));tracker.ProcessBatch([locked]);
        Assert.IsTrue(world.Session.IsLocked);Assert.IsNull(world.Session.GameFlags);Assert.IsNull(world.Session.NativeControlWord);
        Assert.IsNull(tracker.Status.SessionGameFlags);
        world.Apply(GoldGameEventDecoder.Decode(Flags(0)));tracker.ProcessBatch([Flags(0)]);
        Assert.IsFalse(world.Session.IsLocked);Assert.IsFalse(tracker.Status.SessionLocked);
        Assert.AreEqual(0u,world.Session.GameFlags);Assert.AreEqual(0u,tracker.Status.SessionGameFlags);
    }
    [TestMethod]
    public void RepeatedFlagsCountNotificationsAndDisconnectClearsEverything()
    {
        var world=new GoldWorldReplica();var message=Flags(uint.MaxValue);
        world.Apply(GoldGameEventDecoder.Decode(message));world.Apply(GoldGameEventDecoder.Decode(message));
        Assert.AreEqual(2L,world.Session.GameFlagsNotificationCount);Assert.AreEqual(0L,world.Session.LockNotificationCount);
        world.Clear();Assert.IsNull(world.Session.GameFlags);Assert.IsNull(world.Session.NativeControlWord);Assert.IsNull(world.Session.IsLocked);
        Assert.AreEqual(0L,world.Session.GameFlagsNotificationCount);Assert.IsNull(new GoldAdmissionTracker().Status.SessionGameFlags);
    }
    private static GoldMessage Flags(uint flags,byte[]? suffix=null)
    {
        var bytes=new byte[4];BinaryPrimitives.WriteUInt32LittleEndian(bytes,flags);return new(0x1e,0,0,0,[..bytes,..suffix??[]]);
    }
}
