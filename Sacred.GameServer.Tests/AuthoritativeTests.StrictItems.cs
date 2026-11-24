using System.Buffers.Binary;
using Sacred.GameServer.Auth;
using Sacred.GameServer.Config;
using Sacred.GameServer.Lobby;
using Sacred.GameServer.Session;
using Sacred.Network.Gameplay;

namespace Sacred.GameServer.Tests;

/// <summary>--auth-strict-items on a live authoritative server with a fake lobby: an item the client creates that no script token covers is
/// refused (no state in the server's world), one a script minted a token for is accepted, a token is bound to its type and records, and
/// in log mode the same creation is accepted (and counted).</summary>
public partial class AuthoritativeTests
{
    private const string StrictScript = """
        server.on("chat", function(p, text)
            local t2, fa, fb = text:match("^!mint2 (%d+) (%x%x) (%x%x)")
            if t2 then
                local ty = tonumber(t2)
                local own = string.format("%02x%02x%02x%02x", ty % 256, math.floor(ty / 256) % 256, math.floor(ty / 65536) % 256, math.floor(ty / 16777216))
                    .. string.rep(fb, 8) .. string.rep("00", 8) .. string.rep(fb, 110)
                server.save("mint", tostring(server.item_token(p, { type = ty, records = string.rep(fa, 130) .. own }, 60)))
                return true
            end
            local t, fill = text:match("^!mint (%d+) (%x%x)")
            if t then
                local ty = tonumber(t)
                local rec = string.format("%02x%02x%02x%02x", ty % 256, math.floor(ty / 256) % 256, math.floor(ty / 65536) % 256, math.floor(ty / 16777216))
                    .. string.rep(fill, 8) .. string.rep("00", 8) .. string.rep(fill, 110)
                server.save("mint", tostring(server.item_token(p, { type = ty, records = rec }, 60)))
                return true
            end
            local ref = tonumber(text:match("^!item (%d+)"))
            if ref then local it = server.hero_item(p, ref) server.save("item", it and it.where or "nil") return true end
        end)
        """;

    private static string ItemRecords(uint type, byte fill)
    {
        var record = new byte[130];
        Array.Fill(record, fill);
        BinaryPrimitives.WriteUInt32LittleEndian(record, type);
        record.AsSpan(12, 8).Clear();
        return Convert.ToHexStringLower(record);
    }

    private static string? StoredOf(GameServerHost host, string mod, string key, string? not = null, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (host.Scripts!.StoreOf(mod).TryGet(key, out var v) && v is string s && s != not) return s;
            Thread.Sleep(10);
        }
        return null;
    }

    private static void StrictRun(StrictItemsMode mode, Action<GameServerHost, RawClient> test, Dictionary<string, string>? scripts = null, Action<GameServerConfig>? configure = null)
    {
        using var fake = new LobbyTests.FakeLobby();
        var folder = Directory.CreateTempSubdirectory("strict-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "scripts"));
            File.WriteAllText(Path.Combine(folder, "scripts", "strict.lua"), StrictScript);
            foreach (var (name, source) in scripts ?? []) File.WriteAllText(Path.Combine(folder, "scripts", name), source);
            var block = new byte[0x300];
            new Random(5).NextBytes(block);
            var config = new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5, LobbyChannel = 1 };
            config.Auth.Enabled = true; config.Auth.SaveIntervalSeconds = 0; config.Auth.SaveDebounceMilliseconds = 60_000; config.Flood.Enabled = false;
            config.Auth.StrictItems = mode;
            config.Auth.Witness.Sources = Sacred.GameServer.Simulation.WitnessOptions.AllSources & ~Sacred.GameServer.Simulation.ItemSourceSet.Stock;   // these tests create positionless items and expect refusals
            config.Auth.Witness.WorldStartGraceMs = 0;                      // the creations of these tests come right after the world start
            configure?.Invoke(config);
            config.Scripts.Directory = Path.Combine(folder, "scripts"); config.Scripts.DataDirectory = Path.Combine(folder, "data");
            var host = new GameServerHost(config, new AdmissionRules(null), null) { HeroSerializer = new FixedSerializer(block) };
            host.Start();
            var record = host.LobbyRecord(0x1234, 0);
            using var lobby = new LobbyClient("127.0.0.1", fake.Port, record);
            host.AttachLobby(lobby, record);
            lobby.Start();
            var deadline = Environment.TickCount64 + 5000;
            while (!lobby.IsOnline && Environment.TickCount64 < deadline) Thread.Sleep(10);
            Assert.IsTrue(lobby.IsOnline);
            host.StartScripts();
            var thread = new Thread(host.Run) { IsBackground = true };
            thread.Start();
            try
            {
                using var player = RawClient.Admit(host.TcpPort, "Hero", 500, block: 2);
                fake.WaitFor(LobbyProtocol.UserJoinedServer);
                var push = new byte[18 + 0x300];
                BinaryPrimitives.WriteInt32LittleEndian(push, 500);
                BinaryPrimitives.WriteUInt16LittleEndian(push.AsSpan(4), 2);
                BinaryPrimitives.WriteInt32LittleEndian(push.AsSpan(6), 0x300);
                BinaryPrimitives.WriteInt32LittleEndian(push.AsSpan(14), 0x300);
                fake.SendLobby(LobbyProtocol.PublicData, push);
                player.WaitFor(0x132);
                player.Send(0x016, default); player.Send(0x018, Word(0x1964)); player.WaitFor(0x018);
                test(host, player);
            }
            finally { host.Stop(); thread.Join(); host.Dispose(); }
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    private static string? ItemWhere(GameServerHost host, RawClient player, uint reference, string? not = null)
    {
        player.Send(0x005, ChatLine("!item " + reference));
        return StoredOf(host, "strict", "item", not);
    }

    [TestMethod]
    public void StrictItemsRefuseCreationsNoTokenCovers()
    {
        StrictRun(StrictItemsMode.On, (host, player) =>
        {
            // no token: the item the client creates never gets a state in the server's world
            player.Send(0x16F, ItemState(0x5001, 0x12A, 0x77));
            Assert.AreEqual("nil", ItemWhere(host, player, 0x5001));
            Assert.AreEqual(1, host.StrictItemsRefused);

            // a token for exactly that type and records: accepted
            player.Send(0x005, ChatLine("!mint " + 0x12A + " 77"));
            var minted = StoredOf(host, "strict", "mint");
            Assert.IsTrue(minted is not (null or "nil"), "a token id came back: " + minted);
            player.Send(0x16F, ItemState(0x5002, 0x12A, 0x77));
            Assert.AreEqual("world", ItemWhere(host, player, 0x5002, not: "nil"));
            Assert.AreEqual(1, host.StrictItemsRefused, "nothing more was refused");

            // other records: the token does not cover them
            player.Send(0x16F, ItemState(0x5003, 0x12A, 0x66));
            Assert.AreEqual("nil", ItemWhere(host, player, 0x5003, not: "world"));
            Assert.AreEqual(2, host.StrictItemsRefused);
        });
    }

    [TestMethod]
    public void StrictItemsLogModeOnlyCounts()
    {
        StrictRun(StrictItemsMode.Log, (host, player) =>
        {
            player.Send(0x16F, ItemState(0x5011, 0x12A, 0x77));
            Assert.AreEqual("world", ItemWhere(host, player, 0x5011));
            Assert.AreEqual(1, host.StrictItemsRefused, "counted");
        });
    }

    [TestMethod]
    public void StrictItemsOffByDefault()
    {
        StrictRun(StrictItemsMode.Off, (host, player) =>
        {
            player.Send(0x16F, ItemState(0x5021, 0x12A, 0x77));
            Assert.AreEqual("world", ItemWhere(host, player, 0x5021));
            Assert.AreEqual(0, host.StrictItemsRefused);
        });
    }

    private static byte[] SocketedBody(uint type, byte own, byte entry)
    {
        var plain = ItemState(0, type, own)[24..];
        var body = new byte[plain.Length + 130];
        plain.AsSpan(0, GoldItemStateLayout.RecordsOffset).CopyTo(body);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(GoldItemStateLayout.CountOffset), 1);
        body.AsSpan(GoldItemStateLayout.RecordsOffset, 130).Fill(entry);
        plain.AsSpan(GoldItemStateLayout.RecordsOffset).CopyTo(body.AsSpan(GoldItemStateLayout.RecordsOffset + 130));
        return body;
    }

    private static byte[] Update16C(uint reference, uint type, byte[] body)
    {
        var payload = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, reference);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), type);
        body.CopyTo(payload, 8);
        return payload;
    }

    /// <summary>An item with sockets is made in two steps (found with a real Closed Net auction run: the creation 0x16F has no socket entries, a
    /// later update adds them): the token of the finished item (n entries + the own record) covers the creation provisionally and the finished
    /// item must match the token within AUTH_TOKEN_COMPLETE_MS, else it is removed.</summary>
    [TestMethod]
    public void ATokenOfAnItemWithSocketsCoversItsTwoStepCreation()
    {
        StrictRun(StrictItemsMode.On, (host, player) =>
        {
            const uint type = 0x872;
            player.Send(0x005, ChatLine("!mint2 " + type + " 33 44"));
            Assert.IsTrue(StoredOf(host, "strict", "mint") is not (null or "nil"));
            // 1) the creation without entries, then the update with the token's entries: kept
            player.Send(0x16F, ItemState(0x6301, type, 0x44));
            Assert.AreEqual("world", ItemWhere(host, player, 0x6301), "the creation is covered provisionally");
            player.Send(0x16C, Update16C(0x6301, type, SocketedBody(type, 0x44, 0x33)));
            Thread.Sleep(1200);
            Assert.AreEqual("world", ItemWhere(host, player, 0x6301, not: "x"), "the finished item is the token's item");
            Assert.AreEqual(0, host.StrictItemsRefused);
            // 2) a creation that is never completed: removed after the window (a second token of the same item)
            player.Send(0x005, ChatLine("!mint2 " + type + " 33 44"));
            player.Send(0x16F, ItemState(0x6302, type, 0x44));
            Assert.AreEqual("world", ItemWhere(host, player, 0x6302, not: "nil"));
            var until = Environment.TickCount64 + 6000;
            while (Environment.TickCount64 < until && host.StrictItemsRefused < 1) Thread.Sleep(50);
            Assert.AreEqual("nil", ItemWhere(host, player, 0x6302, not: "world"), "never became the token's item");
            Assert.AreEqual(1, host.StrictItemsRefused);
            // 3) completed with other socket entries than the token has: removed
            player.Send(0x005, ChatLine("!mint2 " + type + " 33 44"));
            player.Send(0x16F, ItemState(0x6303, type, 0x44));
            player.Send(0x16C, Update16C(0x6303, type, SocketedBody(type, 0x44, 0x55)));
            until = Environment.TickCount64 + 6000;
            while (Environment.TickCount64 < until && host.StrictItemsRefused < 2) Thread.Sleep(50);
            Assert.AreEqual("nil", ItemWhere(host, player, 0x6303, not: "world"), "other entries than the token");
            Assert.AreEqual(2, host.StrictItemsRefused);
        }, configure: config => { config.Auth.Witness.TokenCompleteMs = 600; config.Auth.CheckItems = false; });
    }
}
