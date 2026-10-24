using System.Collections.Frozen;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.GameRes;
using Sacred.Network.Gameplay;
using Sacred.Network.Tests.Infrastructure;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class BuddyReceiveTests
{
    [TestMethod]
    public void UpdatesPreserveFirstDuplicateNameAndAlertOnlyOnStatusOrChannelChanges()
    {
        var world = new GoldWorldReplica(); var buddies = world.Session.Lobby.Buddies;
        var old = ((GoldBuddyEvent)Event(0x36, CreatureInteractionPackets.Buddy(name: "Original"))).Buddy;
        buddies.Seed([old, old with { Name = "Duplicate" }]);
        world.Apply(Event(0x36, CreatureInteractionPackets.Buddy(game: 99, name: "Ignored")));
        Assert.AreEqual("Original", buddies.Buddies[0].Name); Assert.AreEqual(99u, buddies.Buddies[0].GameWord);
        Assert.AreEqual(old with { Name = "Duplicate" }, buddies.Buddies[1]); Assert.AreEqual(0, buddies.Notices.Count);
        world.Apply(Event(0x36, CreatureInteractionPackets.Buddy(status: 1, channel: -1))); Assert.AreEqual(0, buddies.Notices.Count);
        world.Apply(Event(0x36, CreatureInteractionPackets.Buddy(status: 0, channel: -1))); Assert.AreEqual(1, buddies.Notices.Count);
        world.Apply(Event(0x36, CreatureInteractionPackets.Buddy(status: 1, channel: -1))); Assert.AreEqual(2, buddies.Notices.Count);
        Assert.AreEqual(17u, buddies.Notices[^1].NativeChatSelector);
        Assert.ThrowsException<InvalidOperationException>(() => buddies.Seed([]));
        world.Clear(); Assert.AreEqual(0, buddies.Buddies.Count); Assert.AreEqual(0, buddies.Notices.Count);
    }

    [TestMethod]
    public void QueryReadsOnlyNativeSelectedFieldsAndBoundsFixedUtf16Slots()
    {
        var unknown = (GoldBuddyEvent)Event(0x33, Words(0, 77, 3)); Assert.AreEqual("", unknown.Buddy.Name);
        var notFound = (GoldBuddyEvent)Event(0x33, [..Words(0, uint.MaxValue, 2), 0, 0]); Assert.AreEqual(uint.MaxValue, notFound.Buddy.Id);
        var offline = (GoldBuddyEvent)Event(0x33, [..Words(0, 77, 0), 0, 0]); Assert.AreEqual("", offline.Buddy.Name);
        var online = CreatureInteractionPackets.Buddy(status: 1, channel: -1)[..0x214];
        Assert.AreEqual(-1, ((GoldBuddyEvent)Event(0x33, online)).Buddy.ChannelIndex);
        Assert.ThrowsException<InvalidDataException>(() => Event(0x36, online));
        var invalid = CreatureInteractionPackets.Buddy(); invalid.AsSpan(12, 256).Fill(1);
        Assert.ThrowsException<InvalidDataException>(() => Event(0x36, invalid));
        var surrogate = CreatureInteractionPackets.Buddy(name: "\ud800");
        var entry = (GoldBuddyEvent)Event(0x36, surrogate); Assert.AreEqual("\ud800", entry.Buddy.Name);
        Array.Fill(surrogate, (byte)0xA5); Assert.AreEqual("\ud800", GoldBuddyCodec.Decode(entry.Message).Buddy.Name);
    }

    [TestMethod]
    public void LateLocalizationUsesNativePunctuationAndWrappingSignedChannel()
    {
        var world = new GoldWorldReplica(); var buddies = world.Session.Lobby.Buddies;
        world.Apply(Event(0x33, CreatureInteractionPackets.Buddy(channel: int.MaxValue)));
        var notice = buddies.Notices.Single(); Assert.IsNull(buddies.Resolve(notice));
        buddies.ConfigureResources(new GameResStore(new Dictionary<uint, string>
        {
            [SacredResourceHash.Compute("UI_MSG_USERINGAME")] = "is in game",
            [SacredResourceHash.Compute("UI_BUDDY_LIST")] = "Buddy",
            [SacredResourceHash.Compute("UI_NET_STATUS_OFFLINE")] = "offline"
        }.ToFrozenDictionary()));
        Assert.AreEqual("'User' is in game 'Game' (#-2147483648 'Lobby')!", buddies.Resolve(notice));
        world.Apply(Event(0x36, CreatureInteractionPackets.Buddy()));
        world.Apply(Event(0x36, CreatureInteractionPackets.Buddy(status: 0, name: "Wrong")));
        Assert.AreEqual("Buddy: User --> offline", buddies.Resolve(buddies.Notices[^1]));
        world.Apply(Event(0x33, Words(0, 77, 99))); Assert.AreEqual("", buddies.Resolve(buddies.Notices[^1]));
    }
}
