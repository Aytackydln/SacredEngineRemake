using System.Text;
using System.Text.Json;
using Sacred.GameServer.Wire;

namespace Sacred.GameServer.Tests;

/// <summary>scripts-examples/auction_house.lua on a live server (ephemeral loopback port, never the game's ports): two raw clients
/// say HELLO and trade over the server_only channel "auction" (custom net opcodes 0x1E0/0x1E1).</summary>
public partial class NetOpsTests
{
    private static string AuctionScript()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Sacred.GameServer", "scripts-examples", "auction_house.lua");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException("auction_house.lua");
    }

    private static ushort _auctionMessage;

    private static void Auction(RawClient c, object message) =>
        SendData(c, "auction", NetOpsWire.TargetServer, JsonSerializer.SerializeToUtf8Bytes(message), ++_auctionMessage,
            (byte)(NetOpsWire.FlagFirst | NetOpsWire.FlagLast | NetOpsWire.FlagJson));

    /// <summary>The next message of the market with <paramref name="op"/> (others are skipped).</summary>
    private static JsonElement AuctionAnswer(RawClient c, string op)
    {
        while (true)
        {
            var (channel, json, data) = NextMessage(c);
            Assert.AreEqual(NetOpsWire.ChannelId("auction"), channel);
            Assert.IsTrue(json);
            var message = JsonDocument.Parse(data).RootElement.Clone();
            if (message.GetProperty("op").GetString() == op) return message;
        }
    }

    [TestMethod]
    public void AuctionHouseTradesOverTheLiveChannel()
    {
        using var server = new NetServer(AuctionScript());
        using var a = RawClient.Admit(server.Port, "Alice", 1);
        using var b = RawClient.Admit(server.Port, "Bob", 2);
        Handshake(a); Handshake(b);
        var item = "2a010000" + "0100" + string.Concat(Enumerable.Repeat("ab", 130)) + "00";

        Auction(a, new { op = "hello" });
        var info = AuctionAnswer(a, "info");
        Assert.AreEqual("server", info.GetProperty("where").GetString());
        Auction(a, new { op = "deposit", rid = "a1", gold = 100 });
        Assert.AreEqual(100, AuctionAnswer(a, "result").GetProperty("wallet").GetDouble());
        Auction(a, new { op = "sell", rid = "a2", data = item, price = 1000, days = 1, meta = new { name = "Live Sword", quality = "unique", level = 30 } });
        var sold = AuctionAnswer(a, "result");
        Assert.IsTrue(sold.GetProperty("ok").GetBoolean(), sold.ToString());

        Auction(b, new { op = "browse", q = new { text = "live" } });
        var page = AuctionAnswer(b, "page");
        Assert.AreEqual(1, page.GetProperty("total").GetDouble());
        Assert.AreEqual("Alice", page.GetProperty("rows")[0].GetProperty("seller").GetString());
        Auction(b, new { op = "deposit", rid = "b1", gold = 2000 });
        AuctionAnswer(b, "result");
        Auction(b, new { op = "buy", rid = "b2", id = sold.GetProperty("id").GetDouble(), price = 1000 });
        Assert.IsTrue(AuctionAnswer(b, "result").GetProperty("ok").GetBoolean());
        Assert.AreEqual("sold", AuctionAnswer(a, "notify").GetProperty("kind").GetString(), "the seller hears of it at once");

        Auction(b, new { op = "mail" });
        var mid = AuctionAnswer(b, "mail").GetProperty("entries")[0].GetProperty("mid").GetDouble();
        Auction(b, new { op = "collect", rid = "b3", mid });
        Assert.AreEqual(item, AuctionAnswer(b, "deliver").GetProperty("data").GetString());
        Auction(b, new { op = "collected", rid = "b3" });
        Assert.IsTrue(AuctionAnswer(b, "collected").GetProperty("removed").GetBoolean());

        // the page and the JSON over the script host, as the web UI calls them
        var listings = server.Host.Scripts!.Endpoints.Any(e => e.Mod == "net" && e.Path == "listings");
        Assert.IsTrue(listings, "the listings endpoint is registered");
        Assert.AreEqual(0, server.Host.Scripts.Errors, "no script errors");
    }
}
