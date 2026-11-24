using System.Net;
using System.Text.Json;
using Sacred.GameServer.Config;
using Sacred.GameServer.Demo;
using Sacred.GameServer.Session;
using Sacred.GameServer.Web;

namespace Sacred.GameServer.Tests;

/// <summary><c>--demo</c>: the stub state file, the options, and the web UI served from stub data (127.0.0.1, OS-chosen ports).</summary>
[TestClass]
public class DemoModeTests
{
    [TestMethod]
    public void TheOptionsAreKnownAndTheConfigSeesThem()
    {
        Assert.IsNull(CommandLine.Validate(["--demo"]));
        Assert.IsNull(CommandLine.Validate(["--demo", "--demo-file=x.json", "--webui-port=18080"]));
        var config = GameServerConfig.Load(["--demo-file=x.json"]);
        Assert.IsTrue(config.Demo, "a state file implies demo mode");
        Assert.AreEqual("x.json", config.DemoFile);
        Assert.IsFalse(GameServerConfig.Load([]).Demo);
        StringAssert.Contains(CommandLine.HelpText(), "--demo");
    }

    [TestMethod]
    public void TheBuiltInStateParsesAndACustomFileReplacesIt()
    {
        var d = DemoState.Default();
        Assert.AreEqual("Demo Realm", d.Name);
        Assert.AreEqual(4, d.Players.Count);
        Assert.AreEqual(1, d.Players[0].Slot);
        Assert.IsNotNull(d.Players[0].AtX, "a position inside the grid");
        Assert.AreEqual(43, d.Players[2].SectorX, "a plain sector cell");
        Assert.AreEqual(4, d.Owners.Count);
        Assert.AreEqual(1, d.Unowned.Count);
        CollectionAssert.AreEqual(new[] { 3 }, d.Owners[0].Also);
        Assert.AreEqual(ChatKind.Private, d.Chat.Single(c => c.Kind == ChatKind.Private).Kind);

        var custom = DemoState.Parse("""
            { // comments and trailing commas are fine
              "name": "Mine", "players": [ { "name": "Solo", "level": 7, }, ],
              "chat": [ { "text": "hi", "from": 1 } ], }
            """);
        Assert.AreEqual("Mine", custom.Name);
        Assert.AreEqual(1, custom.Players.Count);
        Assert.AreEqual(7, custom.Players[0].Level);
        Assert.AreEqual(0, custom.Owners.Count);
        Assert.AreEqual(ChatKind.Public, custom.Chat[0].Kind);
        try { DemoState.Parse("{ nope"); Assert.Fail("bad JSON must be refused"); }
        catch (JsonException) { }
    }

    [TestMethod]
    public async Task TheWebUiShowsTheStubPlayersChatAndSectorOwners()
    {
        using var host = new GameServerHost(new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5, BindAddress = IPAddress.Loopback },
            new AdmissionRules(null), null);
        var admin = new DemoAdmin(host, DemoState.Default(), realMap: false);
        await using var server = new WebUiServer(new WebUiOptions { Enabled = true, Port = 0 }, admin, _ => { });
        await server.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.Ports[0]}") };

        Assert.AreEqual(4, admin.Players().Count);
        Assert.AreEqual("Demo Realm", admin.Overview().Name);
        var page = await client.GetStringAsync("/");
        StringAssert.Contains(page, "Seraphim");
        using var chat = JsonDocument.Parse(await client.GetStringAsync("/api/chat?after=0"));
        Assert.AreEqual(5, chat.RootElement.GetProperty("entries").GetArrayLength());

        // the made-up map resolves the owner cells to sector ids (done on a worker: wait for it)
        var until = Environment.TickCount64 + 10000;
        while (admin.SectorOwnership().Version == 0 && Environment.TickCount64 < until) await Task.Delay(50);
        var sectors = admin.SectorOwnership();
        Assert.AreEqual(1, sectors.Version);
        Assert.AreEqual(12, sectors.Sectors.Count(s => s.Owner == 1), "4 x 3 cells of player 1");
        Assert.AreEqual(1, sectors.Sectors.Count(s => s.Owner == 0), "the unowned held sector");
        Assert.IsTrue(sectors.Players[0].HasPosition);
        Assert.AreEqual(12, sectors.Players[0].Owned);
        Assert.IsTrue(sectors.Players[2].Sector > 0 && !sectors.Players[2].HasPosition, "cell only: the sector centre");
        using var map = JsonDocument.Parse(await client.GetStringAsync("/api/areas/map"));
        Assert.IsTrue(map.RootElement.GetProperty("sectors").GetArrayLength() > 1000);
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/api/areas/map.png")).StatusCode);

        // commands do nothing in demo mode
        Assert.IsTrue(admin.Submit(new AdminCommand(AdminCommandKind.Kick, 1)));
        Assert.AreEqual(4, admin.Players().Count);
    }
}
