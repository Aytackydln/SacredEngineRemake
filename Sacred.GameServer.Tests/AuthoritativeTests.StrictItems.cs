using System.Buffers.Binary;
using Sacred.GameServer.Auth;
using Sacred.GameServer.Config;
using Sacred.GameServer.Lobby;
using Sacred.GameServer.Session;

namespace Sacred.GameServer.Tests;

/// <summary>--auth-strict-items on a live authoritative server with a fake lobby: an item the client creates that no script token covers is
/// refused (no state in the server's world), one a script minted a token for is accepted, a token is bound to its type and records, and
/// in log mode the same creation is accepted (and counted).</summary>
public partial class AuthoritativeTests
{
    private const string StrictScript = """
        server.on("chat", function(p, text)
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

    private static void StrictRun(StrictItemsMode mode, Action<GameServerHost, RawClient> test, Dictionary<string, string>? scripts = null)
    {
        using var fake = new LobbyTests.FakeLobby();
        var folder = Directory.CreateTempSubdirectory("strict-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "scripts"));
            File.WriteAllText(Path.Combine(folder, "scripts", "strict.lua"), StrictScript);
            foreach (var (name, source) in scripts ?? []) File.WriteAllText(Path.Combine(folder, "scripts", name), source);
            ExampleAssets.CopyTo(Path.Combine(folder, "scripts"));
            var block = new byte[0x300];
            new Random(5).NextBytes(block);
            var config = new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5, LobbyChannel = 1 };
            config.Auth.Enabled = true; config.Auth.SaveIntervalSeconds = 0; config.Auth.SaveDebounceMilliseconds = 60_000; config.Flood.Enabled = false;
            config.Auth.StrictItems = mode;
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
}
