using System.Net;
using System.Text.Json;
using Sacred.GameServer.Config;
using Sacred.GameServer.Session;
using Sacred.GameServer.Web;

namespace Sacred.GameServer.Tests;

/// <summary>The admin sector ownership map: the server's snapshot of owners and players, and the page, JSON and SSE stream of the web UI
/// (127.0.0.1, ports chosen by the OS).</summary>
[TestClass]
public class SectorOwnershipTests
{
    private sealed class Server : IDisposable
    {
        public readonly GameServerHost Host;
        private readonly Thread _thread;
        public Server()
        {
            Host = new GameServerHost(new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5 }, new AdmissionRules(null), null);
            _thread = new Thread(Host.Run) { IsBackground = true };
            _thread.Start();
        }
        public void Dispose() { Host.Stop(); _thread.Join(); Host.Dispose(); }
    }

    private static SectorOwnership WaitFor(GameServerHost host, Func<SectorOwnership, bool> done)
    {
        var until = Environment.TickCount64 + 5000;
        while (Environment.TickCount64 < until)
        {
            var s = host.SectorOwnership();
            if (done(s)) return s;
            Thread.Sleep(20);
        }
        Assert.Fail("the snapshot never reached the expected state: " + JsonSerializer.Serialize(host.SectorOwnership()));
        return default!;
    }

    // run 8 request: current 0x0B5D, nine entered sectors (0x0B1F, 0x0B20, 0x0B21, ...), 0x87C refs left
    private static readonly byte[] Entry = Convert.FromHexString("5d0b00001f0b200b210b5e0b9d0b9c0b9b0b5c0b5d0b" + new string('0', 36 * 2) + "7c080000");

    [TestMethod]
    public void TheSnapshotShowsOwnerPlayersAndPositionsAndOnlyChangesWithTheContent()
    {
        using var server = new Server();
        Assert.AreEqual(0, server.Host.SectorOwnership().Sectors.Length);
        using var first = RawClient.Admit(((GameServerHost)server.Host).TcpPort, "Host", 1);
        first.WaitFor(0x153);
        first.Send(0x148, Entry);
        first.WaitFor(0x020);
        var one = WaitFor(server.Host, s => s.Sectors.Any(x => x.Id == 0x0B5D && x.Owner == 1));
        var sector = one.Sectors.Single(x => x.Id == 0x0B5D);
        CollectionAssert.AreEqual(new[] { 1 }, sector.Players);
        Assert.IsTrue(sector.Initialized);
        Assert.AreEqual(1, one.Players.Length);
        Assert.AreEqual(0x0B5D, one.Players[0].Sector);
        Assert.AreEqual(1, one.Players[0].Owned, "owns the sector it stands in");
        Assert.IsFalse(one.Players[0].HasPosition, "no hero state sent: only the sector is known");

        // nothing changes: the version stays
        Thread.Sleep(600);
        Assert.AreEqual(one.Version, server.Host.SectorOwnership().Version);

        // a second player enters the same sectors: held by both, the owner stays
        using var second = RawClient.Admit(((GameServerHost)server.Host).TcpPort, "Joiner", 2);
        second.Send(0x148, Entry);
        second.WaitFor(0x148);
        var two = WaitFor(server.Host, s => s.Players.Length == 2 && s.Sectors.Any(x => x.Id == 0x0B5D && x.Players.Length == 2));
        Assert.IsTrue(two.Version > one.Version);
        Assert.AreEqual(1, two.Sectors.Single(x => x.Id == 0x0B5D).Owner);
        CollectionAssert.AreEqual(new[] { 1, 2 }, two.Sectors.Single(x => x.Id == 0x0B5D).Players);
    }

    // ---- the web layer ---------------------------------------------------------------------------------------------------------

    private sealed class Admin : IServerAdmin
    {
        public SectorOwnership Current = new(1, 1000, [new SectorOwnerInfo(3451, 1, [1, 2], true)], [new SectorPlayerInfo(1, "Seraphim", true, 3451, 100, 200, true, 1)]);
        public ServerOverview Overview() => new("Realm", 5, 1.5, 2006, 2105, 0, 16, false, false, false, false, 0x20, 1, 0, false, false, null, "test");
        public IReadOnlyList<PlayerInfo> Players() => [];
        public long ChatHead => 0;
        public IReadOnlyList<ChatEntry> ChatSince(long afterId, int max) => [];
        public IReadOnlyList<BanEntry> Bans() => [];
        public IReadOnlyList<HeroInfo> Heroes() => [];
        public IReadOnlyList<ModEndpointInfo> ModEndpoints() => [];
        public Task<ModResponse> ModRequest(string mod, string path, IReadOnlyList<KeyValuePair<string, string>> query, bool publicOnly, CancellationToken cancellationToken) =>
            Task.FromResult(new ModResponse(404, "text/plain", "no"));
        public FloodSettings Flood() => new(true, false, 600, 3000, 2 * 1024 * 1024, 8 * 1024 * 1024, 3, 8, 500, 10, 60, 8, 42, 1, 2);
        public bool Submit(in AdminCommand command) => true;
        public CancellationToken StopRequested => default;
        public SectorOwnership SectorOwnership() => Current;
    }

    [TestMethod]
    public async Task ThePageAndTheApiAreAdminOnlyAndOffline()
    {
        var admin = new Admin();
        await using var server = new WebUiServer(new WebUiOptions { Enabled = true, Port = 0, Password = "s3cret" }, admin, _ => { });
        await server.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.Ports[0]}") };
        foreach (var url in new[] { "/sectors", "/api/sectors", "/api/sectors/stream" })
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)).StatusCode, url);
        client.DefaultRequestHeaders.Authorization = new("Basic", Convert.ToBase64String("admin:s3cret"u8.ToArray()));
        var html = await client.GetStringAsync("/sectors");
        StringAssert.Contains(html, "<canvas id=\"cv\"");
        StringAssert.Contains(html, "width=device-width");
        StringAssert.Contains(html, "new EventSource('api/sectors/stream')");
        StringAssert.Contains(html, "href=\"sectors\" aria-current=\"page\"");
        StringAssert.Contains(html, "pointerdown");
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(html, @"(src|href)=""https?://|url\(https?://|@import"), "no outside resources: works offline");
        StringAssert.Contains(await client.GetStringAsync("/areas"), "href=\"sectors\"");

        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/sectors"));
        Assert.AreEqual(1, json.RootElement.GetProperty("version").GetInt64());
        var sector = json.RootElement.GetProperty("sectors")[0];
        Assert.AreEqual(3451, sector.GetProperty("id").GetInt32());
        Assert.AreEqual(1, sector.GetProperty("owner").GetInt32());
        Assert.AreEqual(2, sector.GetProperty("players").GetArrayLength());
        Assert.AreEqual("Seraphim", json.RootElement.GetProperty("players")[0].GetProperty("name").GetString());
    }

    [TestMethod]
    public async Task TheStreamSendsTheSnapshotOnConnectAndAgainOnlyWhenItChanges()
    {
        var admin = new Admin();
        await using var server = new WebUiServer(new WebUiOptions { Enabled = true, Port = 0 }, admin, _ => { });
        await server.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.Ports[0]}"), Timeout = Timeout.InfiniteTimeSpan };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await client.GetAsync("/api/sectors/stream", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.AreEqual("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cts.Token));

        async Task<(string Id, JsonDocument Data)> Event()
        {
            string? id = null, name = null, data = null;
            while (true)
            {
                var line = await reader.ReadLineAsync(cts.Token) ?? throw new EndOfStreamException();
                if (line.StartsWith("id: ")) id = line[4..];
                else if (line.StartsWith("event: ")) name = line[7..];
                else if (line.StartsWith("data: ")) data = line[6..];
                else if (line.Length == 0 && data is not null) { Assert.AreEqual("sectors", name); return (id!, JsonDocument.Parse(data)); }
            }
        }

        var (id1, d1) = await Event();
        Assert.AreEqual("1", id1);
        Assert.AreEqual(3451, d1.RootElement.GetProperty("sectors")[0].GetProperty("id").GetInt32());
        admin.Current = new SectorOwnership(2, 2000, [new SectorOwnerInfo(3451, 2, [2], true), new SectorOwnerInfo(3452, 0, [], false)], []);
        var (id2, d2) = await Event();
        Assert.AreEqual("2", id2, "no event in between: the unchanged snapshot is not repeated");
        Assert.AreEqual(2, d2.RootElement.GetProperty("sectors")[0].GetProperty("owner").GetInt32());
        Assert.AreEqual(2, d2.RootElement.GetProperty("sectors").GetArrayLength());
    }
}
