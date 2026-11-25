using System.Buffers.Binary;
using System.Text;
using Sacred.GameServer.Config;
using Sacred.GameServer.Lobby;
using Sacred.GameServer.Session;
using Sacred.Network.Gameplay;

namespace Sacred.GameServer.Tests;

/// <summary>Hero custody for scripts (Session/GameServerHost.ScriptHeroes.cs, Scripting/ScriptHost.Heroes.cs, Lobby/LobbyClient.Uploads.cs)
/// on a live authoritative server with a fake lobby: server.save_hero waits for the lobby's answer to the upload, an item the player
/// destroys is "gone" with its records and can be claimed once, server.hero_upload_time follows the uploads.</summary>
public partial class AuthoritativeTests
{
    private const string CustodyScript = """
        server.on("chat", function(p, text)
            if text == "!auth" then server.save("auth", tostring(server.authoritative(p))) return true end
            if text == "!save" then
                server.save_hero(p, function(ok, err) server.save("saved", tostring(ok) .. ":" .. tostring(err)) end)
                return true
            end
            local ref = tonumber(text:match("^!item (%d+)"))
            if ref then
                local it = server.hero_item(p, ref)
                server.save("item", it and (it.where .. ":" .. it.type .. ":" .. it.records:sub(1, 8) .. ":" .. #it.records) or "nil")
                return true
            end
            ref = tonumber(text:match("^!claim (%d+)"))
            if ref then server.save("claim", tostring(server.claim_item(p, ref))) return true end
            if text == "!uploaded" then server.save("uploaded", server.hero_upload_time(p) and "yes" or "no") return true end
        end)
        """;

    private static byte[] ChatLine(string text) { var b = new byte[4 + text.Length * 2 + 2]; Encoding.Unicode.GetBytes(text, b.AsSpan(4)); return b; }

    /// <summary>A 0x16F item state: 24-byte prefix [ref][type]..., then a body with one 130-byte value record (no sockets).</summary>
    private static byte[] ItemState(uint reference, uint type, byte fill)
    {
        var body = new byte[GoldItemStateLayout.MinimumSize];
        BinaryPrimitives.WriteUInt32LittleEndian(body, GoldObjectStateLayout.Marker);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(GoldObjectStateLayout.TypeOffset), type);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(GoldItemStateLayout.MarkerOffset), 0xBAADC0DE);
        body.AsSpan(GoldItemStateLayout.RecordsOffset, 130).Fill(fill);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(GoldItemStateLayout.RecordsOffset), type);
        body.AsSpan(GoldItemStateLayout.RecordsOffset + 12, 8).Clear();               // no open sockets
        var payload = new byte[24 + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, reference);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), type);
        body.CopyTo(payload.AsSpan(24));
        return payload;
    }

    private static string? WaitStored(GameServerHost host, string key, string? not = null, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (host.Scripts!.StoreOf("custody").TryGet(key, out var v) && v is string s && s != not) return s;
            Thread.Sleep(10);
        }
        return null;
    }

    [TestMethod]
    public void ScriptsSaveTheHeroWithTheLobbysAnswerAndSeeItemsLeave()
    {
        using var fake = new LobbyTests.FakeLobby();
        var folder = Directory.CreateTempSubdirectory("custody-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "scripts"));
            File.WriteAllText(Path.Combine(folder, "scripts", "custody.lua"), CustodyScript);
            var block = new byte[0x300];
            new Random(5).NextBytes(block);
            var config = new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5, LobbyChannel = 1 };
            config.Auth.Enabled = true; config.Auth.SaveIntervalSeconds = 0; config.Auth.SaveDebounceMilliseconds = 60_000; config.Flood.Enabled = false;
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

                player.Send(0x005, ChatLine("!auth"));
                Assert.AreEqual("true", WaitStored(host, "auth"), "the server writes this Closed Net hero");
                player.Send(0x005, ChatLine("!uploaded"));
                Assert.AreEqual("no", WaitStored(host, "uploaded"));

                // a save the lobby stores (result 12 for message 18)
                player.Send(0x005, ChatLine("!save"));
                var upload = fake.WaitFor(LobbyProtocol.ServerDataUpload).Body;
                CollectionAssert.AreEqual(block, upload[18..]);
                Thread.Sleep(100);
                Assert.IsNull(WaitStored(host, "saved", timeoutMs: 200), "no answer before the lobby's");
                var result = new byte[8];
                BinaryPrimitives.WriteInt32LittleEndian(result, LobbyClient.UploadStored);
                BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), LobbyProtocol.ServerDataUpload);
                fake.SendLobby(LobbyProtocol.Result, result);
                Assert.AreEqual("true:nil", WaitStored(host, "saved"));
                // one the lobby refuses
                player.Send(0x005, ChatLine("!save"));
                fake.WaitFor(LobbyProtocol.ServerDataUpload);
                BinaryPrimitives.WriteInt32LittleEndian(result, 11);
                fake.SendLobby(LobbyProtocol.Result, result);
                StringAssert.Contains(WaitStored(host, "saved", not: "true:nil"), "false:the lobby refused the hero (result 11)");
                player.Send(0x005, ChatLine("!uploaded"));
                Assert.AreEqual("yes", WaitStored(host, "uploaded", not: "no"));

                // an item of the player: in the world, then destroyed by its owner (0x167): gone, with its records, claimed once
                player.Send(0x16F, ItemState(0x5001, 0x12A, 0x77));
                player.Send(0x005, ChatLine("!item " + 0x5001));
                Assert.AreEqual("world:298:2a010000:260", WaitStored(host, "item"));
                player.Send(0x167, Word(0x5001));
                player.Send(0x005, ChatLine("!item " + 0x5001));
                Assert.AreEqual("gone:298:2a010000:260", WaitStored(host, "item", not: "world:298:2a010000:260"));
                player.Send(0x005, ChatLine("!claim " + 0x5001));
                Assert.AreEqual("true", WaitStored(host, "claim"));
                player.Send(0x005, ChatLine("!claim " + 0x5001));
                Assert.AreEqual("false", WaitStored(host, "claim", not: "true"));
                player.Send(0x005, ChatLine("!item " + 0x5001));
                Assert.AreEqual("nil", WaitStored(host, "item", not: "gone:298:2a010000:260"));
            }
            finally { host.Stop(); thread.Join(); host.Dispose(); }
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }
}
