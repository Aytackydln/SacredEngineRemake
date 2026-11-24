using System.Buffers.Binary;
using Sacred.GameServer.Auth;
using Sacred.GameServer.Config;
using Sacred.GameServer.Lobby;
using Sacred.GameServer.Session;

namespace Sacred.GameServer.Tests;

[TestClass]
public partial class AuthoritativeTests
{
    private sealed class Server : IDisposable
    {
        public readonly GameServerHost Host;
        public readonly LobbyClient? Lobby;
        private readonly Thread _thread;
        public Server(Action<GameServerConfig>? configure = null, LobbyTests.FakeLobby? fake = null, IHeroSerializer? serializer = null)
        {
            var config = new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5, LobbyChannel = 1 };
            config.Auth.Enabled = true;
            configure?.Invoke(config);
            Host = new GameServerHost(config, new AdmissionRules(null), null) { HeroSerializer = serializer };
            if (fake is not null)
            {
                Host.Start();
                var record = Host.LobbyRecord(0x1234, 0);
                Lobby = new LobbyClient("127.0.0.1", fake.Port, record);
                Host.AttachLobby(Lobby, record);
                Lobby.Start();
                var deadline = Environment.TickCount64 + 5000;
                while (!Lobby.IsOnline && Environment.TickCount64 < deadline) Thread.Sleep(10);
                Assert.IsTrue(Lobby.IsOnline);
                Thread.Sleep(50);
            }
            _thread = new Thread(Host.Run) { IsBackground = true };
            _thread.Start();
        }
        public int Port => Host.TcpPort;
        public void Dispose() { Host.Stop(); _thread.Join(); Lobby?.Dispose(); Host.Dispose(); }
    }

    internal sealed class FixedSerializer(byte[] block) : IHeroSerializer
    {
        public int Serialize(PlayerSession player, Span<byte> destination) { block.CopyTo(destination); return block.Length; }
    }

    internal static byte[] Word(uint value) { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, value); return b; }

    private static byte[] Damage(uint victim, uint damage, uint attacker)
    {
        var b = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(b, victim);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), damage);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), attacker);
        return b;
    }

    private static (RawClient Host, RawClient Joiner) Started(Server server)
    {
        var host = RawClient.Admit(server.Port, "Host", 1);
        host.Send(0x016, default);
        host.Send(0x018, Word(0x1964));
        host.WaitFor(0x018);
        var joiner = RawClient.Admit(server.Port, "Joiner", 2);
        joiner.Send(0x015, default);
        joiner.Send(0x017, default);
        joiner.WaitFor(0x017);
        return (host, joiner);
    }

    [TestMethod]
    public void DamageGuardRules()
    {
        var options = new AuthoritativeOptions { MaxDamage = 100 };
        Span<byte> copy = stackalloc byte[24];
        Assert.AreEqual(GuardVerdict.Allow, DamageGuard.Check(Damage(5, 50, 3), 3, -1, options, copy));      // own hero
        Assert.AreEqual(GuardVerdict.Reject, DamageGuard.Check(Damage(5, 50, 4), 3, -1, options, copy));     // another player's hero
        Assert.AreEqual(GuardVerdict.Allow, DamageGuard.Check(Damage(5, 50, 0), 3, -1, options, copy));      // script/trap
        Assert.AreEqual(GuardVerdict.Allow, DamageGuard.Check(Damage(5, 50, 0x1000), 3, 3, options, copy));  // owned creature
        Assert.AreEqual(GuardVerdict.Reject, DamageGuard.Check(Damage(5, 50, 0x1000), 3, 4, options, copy)); // foreign creature
        Assert.AreEqual(GuardVerdict.Allow, DamageGuard.Check(Damage(5, 50, 0x1000), 3, -1, options, copy)); // unknown, lenient
        options.StrictAttackers = true;
        Assert.AreEqual(GuardVerdict.Reject, DamageGuard.Check(Damage(5, 50, 0x1000), 3, -1, options, copy));
        Assert.AreEqual(GuardVerdict.Clamped, DamageGuard.Check(Damage(5, 5000, 3), 3, -1, options, copy));
        Assert.AreEqual(100u, BinaryPrimitives.ReadUInt32LittleEndian(copy[4..]));
        Assert.AreEqual(GuardVerdict.Reject, DamageGuard.Check(new byte[8], 3, -1, options, copy));
    }

    [TestMethod]
    public void OptionsParseFromConfigKeys()
    {
        var config = new GameServerConfig();
        config.ApplyLine("authoritative");
        config.ApplyLine("auth-max-damage=777");
        config.ApplyLine("AUTH_MAX_VIOLATIONS:3");
        Assert.IsTrue(config.Auth.Enabled);
        Assert.AreEqual(777u, config.Auth.MaxDamage);
        Assert.AreEqual(3, config.Auth.MaxViolations);
        Assert.IsFalse(new GameServerConfig().Auth.Enabled);
    }

    [TestMethod]
    public void ForeignDamageIsDroppedAndLimitedDamageClamped()
    {
        using var server = new Server(c => c.Auth.MaxDamage = 100);
        var (host, joiner) = Started(server);
        using (host) using (joiner)
        {
            joiner.Send(0x17A, Damage(host.Slot, 10, host.Slot));            // attacker is the host's hero: refused
            Assert.IsTrue(host.Silent(0x17A));
            joiner.Send(0x17A, Damage(host.Slot, 9999, joiner.Slot));        // own hero, too much damage: clamped
            Assert.AreEqual(100u, BinaryPrimitives.ReadUInt32LittleEndian(host.WaitFor(0x17A).Payload.AsSpan(4)));
            joiner.Send(0x17A, Damage(host.Slot, 40, joiner.Slot));          // plausible: unchanged
            Assert.AreEqual(40u, BinaryPrimitives.ReadUInt32LittleEndian(host.WaitFor(0x17A).Payload.AsSpan(4)));
        }
    }

    [TestMethod]
    public void RelayModeLeavesDamageAlone()
    {
        using var server = new Server(c => c.Auth.Enabled = false);
        var (host, joiner) = Started(server);
        using (host) using (joiner)
        {
            joiner.Send(0x17A, Damage(host.Slot, 999999, host.Slot));
            Assert.AreEqual(999999u, BinaryPrimitives.ReadUInt32LittleEndian(host.WaitFor(0x17A).Payload.AsSpan(4)));
        }
    }

    [TestMethod]
    public void TradeMessagesNameThePartnerAndAreNotViolations()
    {
        // 0x198 is [partner slot][sub command]: the first word is the other player, so it is not a "spoofed sender" (case 0x4C850E).
        using var server = new Server(c => c.Auth.MaxViolations = 2);
        var (host, joiner) = Started(server);
        using (host) using (joiner)
        {
            for (var i = 0; i < 4; i++)
            {
                var invite = new byte[8];
                BinaryPrimitives.WriteUInt32LittleEndian(invite, host.Slot);
                BinaryPrimitives.WriteUInt32LittleEndian(invite.AsSpan(4), i % 2 == 0 ? 0x20u : 0x22u);
                joiner.Send(0x198, invite);
                host.WaitFor(0x198);
            }
            Assert.IsTrue(joiner.Silent(0x157), "nobody is kicked for trading");
        }
    }

    [TestMethod]
    public void HostCannotChangeTheDifficulty()
    {
        using var server = new Server(c => c.Level = 2);                      // Silver (0x200)
        using var host = RawClient.Admit(server.Port, "Host", 1);
        host.Send(0x01E, Word(0x400 | 0x20));                                 // asks for Gold
        var echoed = BinaryPrimitives.ReadUInt32LittleEndian(host.WaitFor(0x01E).Payload);
        Assert.AreEqual(0x200u, echoed & 0xF00);
    }

    [TestMethod]
    public void HeroIsSavedToTheLobbyOnLeaveAndAfterATrade()
    {
        using var fake = new LobbyTests.FakeLobby();
        var block = new byte[0x300];
        new Random(9).NextBytes(block);
        using var server = new Server(c => { c.Auth.SaveDebounceMilliseconds = 20; c.Auth.SaveIntervalSeconds = 0; }, fake, new FixedSerializer(block));
        var player = RawClient.Admit(server.Port, "Hero", 500, block: 2);
        fake.WaitFor(LobbyProtocol.UserJoinedServer);
        var push = new byte[18 + 0x300];
        BinaryPrimitives.WriteInt32LittleEndian(push, 500);
        BinaryPrimitives.WriteUInt16LittleEndian(push.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(push.AsSpan(6), 0x300);
        BinaryPrimitives.WriteInt32LittleEndian(push.AsSpan(14), 0x300);
        fake.SendLobby(LobbyProtocol.PublicData, push);
        player.WaitFor(0x132);
        player.Send(0x016, default); player.Send(0x018, Word(0x1964)); player.WaitFor(0x018);
        var trade = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(trade, player.Slot);
        player.Send(0x198, trade);
        var upload = fake.WaitFor(LobbyProtocol.ServerDataUpload).Body;
        Assert.AreEqual(500, BinaryPrimitives.ReadInt32LittleEndian(upload));
        CollectionAssert.AreEqual(block, upload[18..]);
        player.Dispose();                                                      // disconnect saves again
        fake.WaitFor(LobbyProtocol.ServerDataUpload);
        Assert.IsTrue(server.Host.HeroSaves >= 2);
    }

    [TestMethod]
    public void GracefulQuitLeavesOnceAndTheHeroIsSavedOnce()
    {
        using var fake = new LobbyTests.FakeLobby();
        var block = new byte[0x300];
        new Random(11).NextBytes(block);
        using var server = new Server(c => { c.Auth.SaveDebounceMilliseconds = 20; c.Auth.SaveIntervalSeconds = 0; }, fake, new FixedSerializer(block));
        var log = new StringWriter();
        var previous = Console.Out;
        Console.SetOut(TextWriter.Synchronized(log));
        try
        {
            var player = RawClient.Admit(server.Port, "Hero", 500, block: 2);
            fake.WaitFor(LobbyProtocol.UserJoinedServer);
            var push = new byte[18 + 0x300];
            BinaryPrimitives.WriteInt32LittleEndian(push, 500);
            BinaryPrimitives.WriteUInt16LittleEndian(push.AsSpan(4), 2);
            BinaryPrimitives.WriteInt32LittleEndian(push.AsSpan(6), 0x300);
            BinaryPrimitives.WriteInt32LittleEndian(push.AsSpan(14), 0x300);
            fake.SendLobby(LobbyProtocol.PublicData, push);
            player.WaitFor(0x132);
            player.Send(0x016, default); player.Send(0x018, Word(0x1964)); player.WaitFor(0x018);
            using var other = RawClient.Admit(server.Port, "Other", 501);
            other.Send(0x017, default);
            other.WaitFor(0x017);

            player.Send(0x150, Word(0));                                          // the ESC menu's Quit
            var left = other.WaitFor(0x150);
            Assert.AreEqual(player.Slot, left.Origin);
            Assert.AreEqual(0u, BinaryPrimitives.ReadUInt32LittleEndian(left.Payload));
            var own = player.WaitFor(0x150);                                       // the leaver gets its own copy too (mode 1)
            Assert.AreEqual(player.Slot, own.Origin);
            other.WaitFor(0x00D);                                                  // the leaving host hands over
            fake.WaitFor(LobbyProtocol.ServerDataUpload);
            player.Dispose();                                                      // the TCP close that follows
            Thread.Sleep(600);
            Assert.AreEqual(1, server.Host.HeroSaves, "saved once, not again on the close");
            Assert.IsTrue(other.Silent(0x150), "no second departure message");
            Assert.IsFalse(log.ToString().Contains("unhandled opcode"), log.ToString());
        }
        finally { Console.SetOut(previous); }
    }

    [TestMethod]
    public void ClosedNetHeroIsSavedFromTheServersOwnView()
    {
        var save = HeroWriterTests.Saves().FirstOrDefault();
        if (save is null) Assert.Inconclusive("no hero saves (set SACRED_HERO_SAVES)");
        var file = File.ReadAllBytes(save);
        var baseline = new byte[file.Length + 4096];
        var baselineLength = Sacred.GameServer.Auth.Hero.HeroFile.WriteBlock(file, new Sacred.GameServer.Wire.ZlibDeflater(), baseline);
        Assert.IsTrue(baselineLength > 0);
        using var fake = new LobbyTests.FakeLobby();
        using var server = new Server(c => { c.Auth.SaveDebounceMilliseconds = 20; c.Auth.SaveIntervalSeconds = 0; }, fake);
        var player = RawClient.Admit(server.Port, "Hero", 500, block: 2);
        fake.WaitFor(LobbyProtocol.UserJoinedServer);
        var push = new byte[18 + baselineLength];
        BinaryPrimitives.WriteInt32LittleEndian(push, 500);
        BinaryPrimitives.WriteUInt16LittleEndian(push.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(push.AsSpan(6), baselineLength);
        BinaryPrimitives.WriteInt32LittleEndian(push.AsSpan(14), baselineLength);
        baseline.AsSpan(0, baselineLength).CopyTo(push.AsSpan(18));
        fake.SendLobby(LobbyProtocol.PublicData, push);
        player.WaitFor(0x132);
        player.Send(0x016, default); player.Send(0x018, Word(0x1964)); player.WaitFor(0x018);

        // The upload, as the client sends it: items, hero, inventories.
        var decoded = HeroWriterTests.Decode(file);
        var (live, bodies) = HeroWriterTests.Upload(decoded, player.Slot);
        byte[] State(uint reference, byte[] body, int prefix)
        {
            var payload = new byte[prefix + body.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(payload, reference);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8)));
            body.CopyTo(payload, prefix);
            return payload;
        }
        foreach (var (reference, body) in bodies) player.Send(0x16E, State(reference, body, 8));
        var hero = live.HeroBody.ToArray();
        player.Send(0x16E, State(player.Slot, hero, 8));
        player.Send(0x122, [.. Word(player.Slot), .. live.InventoryStream.ToArray()]);
        if (!live.SharedInventoryStream.IsEmpty) player.Send(0x122, [.. Word(17), .. live.SharedInventoryStream.ToArray()]);
        // Later: more gold (compact values in a 0x169), and an attempt to put an object the player does not own into the
        // inventory (0x13F [ref][cell][inventory]).
        BinaryPrimitives.WriteUInt32LittleEndian(hero.AsSpan(Sacred.Network.Gameplay.GoldCreatureStateLayout.PrefixSize +
            Sacred.Network.Gameplay.GoldCreatureValuesLayout.CompactWord46Offset), 777_777);
        player.Send(0x169, State(player.Slot, hero, 24));
        var place = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(place, 0x1F000);
        BinaryPrimitives.WriteUInt32LittleEndian(place.AsSpan(4), 255);
        BinaryPrimitives.WriteUInt32LittleEndian(place.AsSpan(8), player.Slot);
        player.Send(0x13F, place);
        player.Send(0x14F, new byte[0x2670 + 100]);
        Thread.Sleep(100);
        player.Dispose();                                                     // a hard disconnect saves

        var upload = fake.WaitFor(LobbyProtocol.ServerDataUpload).Body;
        Assert.AreEqual(500, BinaryPrimitives.ReadInt32LittleEndian(upload));
        Assert.AreEqual(2, BinaryPrimitives.ReadUInt16LittleEndian(upload.AsSpan(4)));
        var block = upload[18..];
        var saved = new byte[Sacred.GameServer.Auth.Hero.HeroWriter.MaxFileBytes];
        var savedLength = Sacred.GameServer.Auth.Hero.HeroFile.ReadBlock(block, new Sacred.GameServer.Wire.ZlibInflater(), saved);
        Assert.IsTrue(savedLength > 0);
        var after = HeroWriterTests.Decode(saved[..savedLength]);
        var values = Sacred.Core.Pax.Hero.Objects.SacredSavedHero.FromPayload(after.Hero, after.Version).Creature.Values;
        Assert.AreEqual(777_777u, values.Gold);
        Assert.AreEqual(decoded.Records.Count, after.Records.Count, "the foreign object is not saved, every own item is");
        var cell = 1036 + 255 * 12;
        Assert.AreEqual(0u, BinaryPrimitives.ReadUInt32LittleEndian(after.Inventory.AsSpan(cell)));
        var profile = Sacred.GameServer.Auth.Hero.HeroFile.FindChunk(saved.AsSpan(0, savedLength), 0xC3, out var profileAt, out _);
        CollectionAssert.AreEqual(saved.AsSpan(profileAt, 556).ToArray(), block[..556], "the preview is the profile chunk");
    }
}
