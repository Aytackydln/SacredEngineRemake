using System.Text.Json;
using System.Text.Json.Nodes;
using Sacred.GameServer.Auth;
using Sacred.GameServer.Config;
using Sacred.GameServer.Wire;

namespace Sacred.GameServer.Tests;

/// <summary>The auction house (the real script) on a live authoritative server with a fake lobby, loopback only, with
/// <c>--auth-strict-items</c> in log mode and then on: a mailbox item that is collected gets a token, so the client's creation of exactly that
/// item passes the gate; an item no token covers is counted (log) or refused (on).</summary>
public partial class AuthoritativeTests
{
    private static string AuctionSource()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Sacred.GameServer", "scripts-examples", "auction_house.lua");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException("auction_house.lua");
    }

    private static ushort _auctionMessageId;

    private static void AuctionSend(RawClient c, object message)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(message);
        var payload = new byte[NetOpsWire.DataHeaderBytes + data.Length];
        NetOpsWire.WriteData(payload, new(NetOpsWire.ChannelId("auction"), (byte)(NetOpsWire.FlagFirst | NetOpsWire.FlagLast | NetOpsWire.FlagJson), NetOpsWire.TargetServer, ++_auctionMessageId), data);
        c.Send(NetOpsWire.DefaultData, payload);
    }

    private static JsonElement AuctionNext(RawClient c, string op, int timeoutMs = 3000)
    {
        while (true)
        {
            var m = c.WaitFor(NetOpsWire.DefaultData, timeoutMs);
            Assert.IsTrue(NetOpsWire.TryReadData(m.Payload, out _, out var d));
            var message = JsonDocument.Parse(d.ToArray()).RootElement.Clone();
            if (message.GetProperty("op").GetString() == op) return message;
        }
    }

    private static void AuctionStrictRun(StrictItemsMode mode)
    {
        StrictRun(mode, (host, player) =>
        {
            NetOpsTests.Handshake(player);      // the chat handshake, then HELLO on the agreed types

            AuctionSend(player, new { op = "hello" });
            var info = AuctionNext(player, "info");
            Assert.IsTrue(info.GetProperty("closed").GetBoolean(), "the server holds this hero");

            // a mailbox entry of this player (as an expired listing would leave): written to the mod's storage, picked up by a reload
            string ModStore(string? key, string? body) => host.ModStoreRequest("auction_house", key, body, CancellationToken.None).GetAwaiter().GetResult().Body;
            var market = JsonNode.Parse(ModStore("market", null))!.AsObject();
            var uid = market["players"]!.AsObject().Single().Key;
            var data = "2a010000" + "0000" + ItemRecords(0x12A, 0x42) + "00";
            var entry = new JsonObject { ["mid"] = 1, ["owner"] = uid, ["kind"] = "item", ["t"] = 1, ["reason"] = "expired", ["data"] = data, ["type"] = 0x12A,
                ["name"] = "Tested Sword", ["quality"] = "rare", ["category"] = "weapon", ["w"] = 1, ["h"] = 2, ["price"] = 5 };
            market["_chunks"]!.AsObject()["mail"] = 1;
            ModStore(null, new JsonObject { ["mod"] = "auction_house", ["set"] = new JsonObject { ["mail.1"] = new JsonArray(entry), ["market"] = market } }.ToJsonString());
            host.Scripts!.RequestReload("auction_house");
            JsonElement mail = default;
            var until = Environment.TickCount64 + 8000;
            while (Environment.TickCount64 < until)
            {
                AuctionSend(player, new { op = "mail" });
                mail = AuctionNext(player, "mail");
                if (mail.GetProperty("entries").GetArrayLength() == 1) break;
                Thread.Sleep(200);
            }
            Assert.AreEqual(1, mail.GetProperty("entries").GetArrayLength(), "the reloaded market has the entry");

            // before the delivery nothing covers the item
            player.Send(0x16F, ItemState(0x6001, 0x12A, 0x42));
            Assert.AreEqual(mode == StrictItemsMode.On ? "nil" : "world", ItemWhere(host, player, 0x6001), "no token yet");
            Assert.AreEqual(1, host.StrictItemsRefused);

            AuctionSend(player, new { op = "collect", rid = "c1", mid = 1 });
            Assert.AreEqual(data, AuctionNext(player, "deliver").GetProperty("data").GetString());
            // the token exists now: the client's creation of exactly this item passes the gate in either mode
            player.Send(0x16F, ItemState(0x6002, 0x12A, 0x42));
            Assert.AreEqual("world", ItemWhere(host, player, 0x6002, not: "nil"));
            Assert.AreEqual(1, host.StrictItemsRefused, "the delivered item was covered by its token");
            // another item is not covered by it
            player.Send(0x16F, ItemState(0x6003, 0x12A, 0x43));
            Assert.AreEqual(mode == StrictItemsMode.On ? "nil" : "world", ItemWhere(host, player, 0x6003, not: mode == StrictItemsMode.On ? "world" : null));
            for (var i = 0; i < 200 && host.StrictItemsRefused < 2; i++) Thread.Sleep(10);
            Assert.AreEqual(2, host.StrictItemsRefused);
        }, new() { ["auction_house.lua"] = AuctionSource() });
    }


    /// <summary>With the gate on, an item the client made itself is refused (the server never holds it); the market must not list it however
    /// the client then "sells" it: not as the refused item (even destroyed afterwards) and not as an id the server never saw.</summary>
    [TestMethod]
    public void AuctionRefusesASaleOfAnItemTheServerNeverHeld()
    {
        StrictRun(StrictItemsMode.On, (host, player) =>
        {
            NetOpsTests.Handshake(player);
            AuctionSend(player, new { op = "hello" });
            Assert.IsTrue(AuctionNext(player, "info").GetProperty("closed").GetBoolean());
            string ModStore(string? key, string? body) => host.ModStoreRequest("auction_house", key, body, CancellationToken.None).GetAwaiter().GetResult().Body;
            var market = JsonNode.Parse(ModStore("market", null))!.AsObject();
            var uid = market["players"]!.AsObject().Single().Key;
            market["wallets"] = new JsonObject { [uid] = 1000 };                      // enough for the listing fee
            var rules = JsonNode.Parse(ModStore("config", null))!.AsObject();
            rules["verify_seconds"] = 5;                                      // (the connection of a raw test client times out after about 30 s)
            ModStore(null, new JsonObject { ["mod"] = "auction_house", ["set"] = new JsonObject { ["market"] = market, ["config"] = rules } }.ToJsonString());
            host.Scripts!.RequestReload("auction_house");
            Thread.Sleep(1500);

            // the client makes an item itself: the gate refuses it, then the client destroys it and sells it
            player.Send(0x16F, ItemState(0x6101, 0x12A, 0x42));
            Assert.AreEqual("nil", ItemWhere(host, player, 0x6101));
            Assert.AreEqual(1, host.StrictItemsRefused);
            player.Send(0x167, Word(0x6101));
            var data = "2a010000" + "0000" + ItemRecords(0x12A, 0x42) + "00";
            AuctionSend(player, new { op = "sell", rid = "s1", @ref = 0x6101, data, price = 10, days = 1, meta = new { name = "Faked", type = 0x12A, category = "weapon" } });
            // and an item id the server never saw at all
            AuctionSend(player, new { op = "sell", rid = "s2", @ref = 0x6102, data, price = 10, days = 1, meta = new { name = "Faked", type = 0x12A, category = "weapon" } });
            var refused = new HashSet<string>();
            var until = Environment.TickCount64 + 25_000;
            while (refused.Count < 2 && Environment.TickCount64 < until)
            {
                var result = AuctionNext(player, "result", 20_000);
                Assert.IsFalse(result.GetProperty("ok").GetBoolean(), "the market must refuse this sale: " + result);
                refused.Add(result.GetProperty("rid").GetString()!);
            }
            CollectionAssert.AreEquivalent(new[] { "s1", "s2" }, refused.ToArray());
            var after = JsonNode.Parse(ModStore("market", null))!.AsObject();
            Assert.AreEqual(1000, (int)after["wallets"]!.AsObject()[uid]!, "no listing fee was taken");
        }, new() { ["auction_house.lua"] = AuctionSource() });
    }

    [TestMethod]
    public void AuctionDeliveryTokensPassTheStrictItemGateInLogMode() => AuctionStrictRun(StrictItemsMode.Log);

    [TestMethod]
    public void AuctionDeliveryTokensPassTheStrictItemGateInOnMode() => AuctionStrictRun(StrictItemsMode.On);
}
