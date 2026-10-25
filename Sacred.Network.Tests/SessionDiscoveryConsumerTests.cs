using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Discovery;
using Sacred.Network.Gameplay;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class SessionDiscoveryConsumerTests
{
    private static readonly IPAddress Local = IPAddress.Parse("192.168.178.204");
    private static GoldLanAnnouncement Announcement(uint flags = 0x10199, string name = "Sacred Gameserver") =>
        new(0xDCB8, new(Local, 2006), flags, 2, 4, name);

    [TestMethod]
    public void SelectedAnnouncementInitializesDifficultyWithoutAn01EReceipt()
    {
        var world = new GoldWorldReplica();
        var server = GoldLanSessionMatch.Find(new(Local, 2006), [Announcement()], new HashSet<IPAddress>());
        Assert.IsNotNull(server);
        Assert.IsTrue(world.Session.SeedAdvertisedGameFlags(server.Flags));
        Assert.AreEqual(1u, world.Session.Difficulty);
        Assert.AreEqual(0L, world.Session.GameFlagsNotificationCount);
        Assert.AreEqual(0x10199u, world.Session.GameFlags);
        Assert.IsFalse(world.Session.SeedAdvertisedGameFlags(0x800));
        world.Apply(Event(0x01E, Words(0x400)));
        Assert.AreEqual(3u, world.Session.Difficulty);
        Assert.AreEqual(1L, world.Session.GameFlagsNotificationCount);
        Assert.IsFalse(world.Session.SeedAdvertisedGameFlags(server.Flags));
        world.Clear(); Assert.IsTrue(world.Session.SeedAdvertisedGameFlags(0x200));
        Assert.AreEqual(2u, world.Session.Difficulty);
    }

    [TestMethod]
    public void NativeLanDatagramSuppliesInitialDifficultyAndPreservesAnEarlierLockUpdate()
    {
        var server = GoldLanAnnouncementCodec.Decode(Convert.FromHexString(
            "AE000000785EDB71E71AFB994D2B0E4832323030B00433243224331431A432A4302830B80379B94076311017319481C9810500480B0BF9"));
        var world = new GoldWorldReplica(); world.Apply(Event(GoldSessionLockCodec.LockOpcode, []));
        Assert.IsTrue(world.Session.SeedAdvertisedGameFlags(server.Flags));
        Assert.AreEqual(0x11Du, world.Session.GameFlags);
        Assert.AreEqual(1u, world.Session.Difficulty);
        Assert.AreEqual(0L, world.Session.GameFlagsNotificationCount);
    }

    [TestMethod]
    public void LoopbackJoinMatchesOnlyAnAddressOnThisMachineAndTheSameTcpPort()
    {
        var locals = new HashSet<IPAddress> { Local };
        var remote = new IPEndPoint(IPAddress.Loopback, 2006);
        Assert.IsNotNull(GoldLanSessionMatch.Find(remote, [Announcement()], locals));
        Assert.IsNotNull(GoldLanSessionMatch.Find(new(IPAddress.Parse("::ffff:127.0.0.1"), 2006), [Announcement()], locals));
        Assert.IsNull(GoldLanSessionMatch.Find(remote, [Announcement()], new HashSet<IPAddress>()));
        Assert.IsNull(GoldLanSessionMatch.Find(new(IPAddress.Loopback, 2007), [Announcement()], locals));
        Assert.IsNull(GoldLanSessionMatch.Find(new(IPAddress.Parse("192.168.178.205"), 2006), [Announcement()], locals));
    }

    [TestMethod]
    public void ConflictingAdvertisementsCannotChooseAnArbitraryDifficulty()
    {
        Assert.IsNull(GoldLanSessionMatch.Find(new(Local, 2006), [Announcement(), Announcement(0x800, "Other")],
            new HashSet<IPAddress>()));
        Assert.IsNotNull(GoldLanSessionMatch.Find(new(Local, 2006), [Announcement(), Announcement(name: "Alias")],
            new HashSet<IPAddress>()));
    }
}
