using System.Buffers.Binary;
using Sacred.Network.Gameplay;

namespace Sacred.GameServer.Tests;

public partial class AuthoritativeTests
{
    private static byte[] ObjectState(uint reference, byte[] body, int prefix)
    {
        var payload = new byte[prefix + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, reference);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8)));
        body.CopyTo(payload, prefix);
        return payload;
    }

    [TestMethod]
    public void ImpossibleItemStatesAreDroppedAndCounted()
    {
        var items = ItemGuardTests.SavedItems();
        if (items.Count == 0) Assert.Inconclusive("no hero saves (set SACRED_HERO_SAVES)");
        var socket = items.First(b => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(GoldItemStateLayout.CountOffset)) > 0)
            .AsSpan(GoldItemStateLayout.RecordsOffset, 130).ToArray();
        var plain = items.First(b => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(GoldItemStateLayout.CountOffset)) == 0 &&
            !b.AsSpan(GoldItemStateLayout.RecordsOffset + 12, 8).ToArray().Any(t => t is >= 1 and <= 4));
        var forged = ItemGuardTests.WithSocket(plain, socket);

        using var server = new Server(c => c.Auth.MaxViolations = 0);
        var (host, joiner) = Started(server);
        using (host) using (joiner)
        {
            joiner.Send(0x169, ObjectState(0x2F001, forged, 24));             // refused: not relayed
            joiner.Send(0x169, ObjectState(0x2F002, plain, 24));              // plausible: relayed as before
            var relayed = host.WaitFor(0x169);
            Assert.AreEqual(0x2F002u, BinaryPrimitives.ReadUInt32LittleEndian(relayed.Payload) & 0x7FFFFFFF);
            Assert.AreEqual(1, server.Host.RejectedItems);
        }
    }

    [TestMethod]
    public void RelayModeDoesNotCheckItems()
    {
        var items = ItemGuardTests.SavedItems();
        if (items.Count == 0) Assert.Inconclusive("no hero saves (set SACRED_HERO_SAVES)");
        var socket = items.First(b => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(GoldItemStateLayout.CountOffset)) > 0)
            .AsSpan(GoldItemStateLayout.RecordsOffset, 130).ToArray();
        var forged = ItemGuardTests.WithSocket(items[0], socket);
        for (var i = 0; i < 9; i++) forged = ItemGuardTests.WithSocket(forged, socket);
        using var server = new Server(c => c.Auth.Enabled = false);
        var (host, joiner) = Started(server);
        using (host) using (joiner)
        {
            joiner.Send(0x169, ObjectState(0x2F001, forged, 24));
            host.WaitFor(0x169);
            Assert.AreEqual(0, server.Host.RejectedItems);
        }
    }

    [TestMethod]
    public void HeroAboveTheLevelCapIsRemoved()
    {
        var save = HeroWriterTests.Saves().FirstOrDefault();
        if (save is null) Assert.Inconclusive("no hero saves (set SACRED_HERO_SAVES)");
        using var server = new Server();
        var (host, joiner) = Started(server);
        using (host) using (joiner)
        {
            var (live, _) = HeroWriterTests.Upload(HeroWriterTests.Decode(File.ReadAllBytes(save)), joiner.Slot);
            var hero = live.HeroBody.ToArray();
            joiner.Send(0x169, ObjectState(joiner.Slot, hero, 24));          // a real hero: relayed
            host.WaitFor(0x169);
            BinaryPrimitives.WriteUInt16LittleEndian(hero.AsSpan(GoldCreatureStateLayout.PrefixSize + GoldCreatureValuesLayout.CompactWord56Offset), 500);
            joiner.Send(0x169, ObjectState(joiner.Slot, hero, 24));
            host.WaitFor(0x150);                                              // the joiner leaves the game
            Assert.IsTrue(host.Silent(0x169), "the impossible hero state is not relayed");
        }
    }
}
