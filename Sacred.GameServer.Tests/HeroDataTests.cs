using System.Buffers.Binary;
using Sacred.Core.Pax.Hero.Objects;
using Sacred.GameServer.Config;
using Sacred.GameServer.Session;

namespace Sacred.GameServer.Tests;

/// <summary>Player data for the web UI from a real hero upload (saves under SACRED_HERO_SAVES; inconclusive without them).</summary>
[TestClass]
public class HeroDataTests
{
    [TestMethod]
    public void OnlineHeroShowsValuesSkillsAndItems()
    {
        var save = HeroWriterTests.Saves().FirstOrDefault(p => p.Contains("hero07", StringComparison.OrdinalIgnoreCase)) ?? HeroWriterTests.Saves().FirstOrDefault();
        if (save is null) { Assert.Inconclusive("no hero saves (set SACRED_HERO_SAVES)"); return; }
        var decoded = HeroWriterTests.Decode(File.ReadAllBytes(save));
        var saved = SacredSavedHero.FromPayload(decoded.Hero, decoded.Version).Creature.Values;

        var config = new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5 };
        var host = new GameServerHost(config, new AdmissionRules(null), null) { TrackHeroes = true, Names = NameServiceTests.Shared };
        var thread = new Thread(host.Run) { IsBackground = true };
        thread.Start();
        try
        {
            using var player = RawClient.Admit(host.TcpPort, "acct.Hero", 7);
            player.Send(0x016, default); player.Send(0x018, BitConverter.GetBytes(0x1964u)); player.WaitFor(0x018);
            var (live, bodies) = HeroWriterTests.Upload(decoded, player.Slot);
            byte[] State(uint reference, byte[] body)
            {
                var payload = new byte[8 + body.Length];
                BinaryPrimitives.WriteUInt32LittleEndian(payload, reference);
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8)));
                body.CopyTo(payload, 8);
                return payload;
            }
            foreach (var (reference, body) in bodies) player.Send(0x16E, State(reference, body));
            player.Send(0x16E, State(player.Slot, live.HeroBody.ToArray()));
            player.Send(0x122, [.. BitConverter.GetBytes((uint)player.Slot), .. live.InventoryStream.ToArray()]);
            Web.HeroInfo? hero = null;
            for (var i = 0; i < 100 && (hero is null || hero.Inventory.Length == 0); i++) { Thread.Sleep(30); hero = host.Heroes().FirstOrDefault(); }
            Assert.IsNotNull(hero);
            Assert.AreEqual("Hero", hero.Character);
            Assert.AreEqual("acct.Hero", hero.Account);
            Assert.AreEqual(saved.Level, hero.Level);
            Assert.AreEqual(saved.Gold, (uint)hero.Gold);
            Assert.AreEqual(saved.Experience, (uint)hero.Experience);
            Assert.AreEqual(saved.RemainingSkillPoints, hero.SkillPointsLeft);
            Assert.AreEqual(saved.Count22, hero.CombatArts.Length);
            Assert.AreEqual(saved.Count38, hero.Combos);
            var ids = saved.Block08.Slice(28, 8).ToArray().Where(b => b != 0).Select(b => (int)b).ToArray();
            CollectionAssert.AreEqual(ids, hero.Skills.Select(s => s.Id).ToArray());
            var anchors = 0;
            for (var cell = 0; cell < 256; cell++) if (BinaryPrimitives.ReadUInt32LittleEndian(decoded.Inventory.AsSpan(1036 + cell * 12)) != 0) anchors++;
            Assert.AreEqual(anchors, hero.Inventory.Length, "every inventory item is listed");
            Assert.IsTrue(hero.Inventory.All(i => i.Width >= 1 && i.Height >= 1 && i.Item.Type != 0));
            Assert.IsTrue(hero.Equipment.Length > 0, "equipped items");
            if (NameServiceTests.Shared is not null)
            {
                Assert.IsTrue(hero.Skills.All(sk => !string.IsNullOrEmpty(sk.Name)), "skill names");
                Assert.IsTrue(hero.CombatArts.All(a => !string.IsNullOrEmpty(a.Name)), "combat art names");
                Assert.IsTrue(hero.Equipment.Concat(hero.Inventory.Select(i => new Web.EquippedItem(i.Cell, i.Item))).All(e => !string.IsNullOrEmpty(e.Item.Name)), "item names");
                Console.WriteLine(string.Join(", ", hero.Equipment.Select(e => e.Item.Name)));
            }
        }
        finally { host.Stop(); thread.Join(); host.Dispose(); }
    }
}
