using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Sacred.GameServer.Config;
using Sacred.GameServer.Session;
using Sacred.GameServer.Web;

namespace Sacred.GameServer.Tests;

/// <summary>Host side of the web UI (snapshot, chat ring, command queue, bans) and the web layer itself (options, auth, CORS, API).</summary>
[TestClass]
public class WebAdminTests
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
        public int Port => Host.TcpPort;
        public void Dispose() { Host.Stop(); _thread.Join(); Host.Dispose(); }
    }

    private static byte[] Text(string text, uint word = 0)
    {
        var b = new byte[4 + text.Length * 2 + 2];
        BinaryPrimitives.WriteUInt32LittleEndian(b, word);
        Encoding.Unicode.GetBytes(text, b.AsSpan(4));
        return b;
    }

    private static T Eventually<T>(Func<T> read, Func<T, bool> done)
    {
        T value = read();
        for (var i = 0; i < 100 && !done(value); i++) { Thread.Sleep(20); value = read(); }
        return value;
    }

    // ---- host -------------------------------------------------------------------------------------------------------

    [TestMethod]
    public void PlayersChatAndSystemLinesAreVisibleToTheAdmin()
    {
        using var server = new Server();
        using var a = RawClient.Admit(server.Port, "Alice", 1);
        using var b = RawClient.Admit(server.Port, "Bob", 2);
        a.Send(0x005, Text("hello all"));
        a.Send(0x003, Text("psst", b.Slot));
        var chat = Eventually(() => server.Host.ChatSince(0, 100), c => c.Count(e => e.Kind != ChatKind.System) >= 2);
        Assert.IsTrue(chat.Any(e => e.Kind == ChatKind.System && e.FromName == "Alice" && e.Text == "joined"));
        var pub = chat.Single(e => e.Kind == ChatKind.Public);
        Assert.AreEqual("hello all", pub.Text); Assert.AreEqual("Alice", pub.FromName); Assert.AreEqual(-1, pub.ToSlot);
        var priv = chat.Single(e => e.Kind == ChatKind.Private);
        Assert.AreEqual("psst", priv.Text); Assert.AreEqual(b.Slot, priv.ToSlot);
        Assert.AreEqual(server.Host.ChatHead, chat[^1].Id);
        Assert.AreEqual(0, server.Host.ChatSince(server.Host.ChatHead, 10).Count);

        var players = Eventually(() => server.Host.Players(), p => p.Count == 2);
        Assert.AreEqual(2, players.Count);
        Assert.AreEqual("Alice", players[0].Name);
        Assert.AreEqual("127.0.0.1", players[0].Address);
        Assert.AreEqual(2, server.Host.Overview().Players);
    }

    [TestMethod]
    public void BroadcastAndPrivateMessageReachThePlayers()
    {
        using var server = new Server();
        using var a = RawClient.Admit(server.Port, "Alice", 1);
        using var b = RawClient.Admit(server.Port, "Bob", 2);
        Assert.IsTrue(server.Host.Submit(new AdminCommand(AdminCommandKind.Broadcast, Text: "server says hi")));
        foreach (var client in new[] { a, b })
        {
            var message = client.WaitFor(0x005);
            Assert.AreEqual((byte)0, message.Origin);
            StringAssert.Contains(Encoding.Unicode.GetString(message.Payload, 4, message.Payload.Length - 4), "server says hi");
        }
        server.Host.Submit(new AdminCommand(AdminCommandKind.Message, b.Slot, Text: "only you"));
        var direct = b.WaitFor(0x003);
        Assert.AreEqual((uint)b.Slot, BinaryPrimitives.ReadUInt32LittleEndian(direct.Payload));
        Assert.IsTrue(a.Silent(0x003));
        var log = Eventually(() => server.Host.ChatSince(0, 100), c => c.Any(e => e.Kind == ChatKind.ServerMessage));
        Assert.IsTrue(log.Any(e => e.Kind == ChatKind.Broadcast && e.Text == "server says hi"));
    }

    [TestMethod]
    public void KickRemovesThePlayerAndBanRefusesItsAddress()
    {
        using var server = new Server();
        using var a = RawClient.Admit(server.Port, "Alice", 1);
        server.Host.Submit(new AdminCommand(AdminCommandKind.Kick, a.Slot));
        Assert.AreEqual(0x157, a.WaitFor(0x157).Opcode);
        Eventually(() => server.Host.Players(), p => p.Count == 0);
        // Not banned: it may come back.
        using (var again = RawClient.Admit(server.Port, "Alice", 1)) Assert.AreNotEqual(0, again.Slot);

        using var b = RawClient.Admit(server.Port, "Bob", 2);
        server.Host.Submit(new AdminCommand(AdminCommandKind.Ban, b.Slot, Text: "spam"));
        b.WaitFor(0x157);
        var bans = Eventually(() => server.Host.Bans(), x => x.Count == 1);
        Assert.AreEqual("127.0.0.1", bans[0].Address); Assert.AreEqual("spam", bans[0].Reason);
        Assert.AreEqual(3u, RawClient.AdmitExpectingRejection(server.Port, "Bob", 2));

        server.Host.Submit(new AdminCommand(AdminCommandKind.Unban, Text: "127.0.0.1"));
        Eventually(() => server.Host.Bans(), x => x.Count == 0);
        using var back = RawClient.Admit(server.Port, "Bob", 2);
        Assert.AreNotEqual(0, back.Slot);
    }

    [TestMethod]
    public void SessionSettingsAreAppliedByTheLoop()
    {
        using var server = new Server();
        using var a = RawClient.Admit(server.Port, "Alice", 1);
        server.Host.Submit(new AdminCommand(AdminCommandKind.SetMaxPlayers, Value: 3));
        Assert.AreEqual(3u, BinaryPrimitives.ReadUInt32LittleEndian(a.WaitFor(0x023).Payload));
        server.Host.Submit(new AdminCommand(AdminCommandKind.Lock, Value: 1));
        a.WaitFor(0x01A);
        Assert.IsTrue(Eventually(() => server.Host.Overview(), o => o.Locked).Locked);
        Assert.AreEqual(3, server.Host.Overview().MaxPlayers);
        server.Host.Submit(new AdminCommand(AdminCommandKind.SetMode, 2, 2));          // player killer, level 2
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(a.WaitFor(0x01E).Payload);
        Assert.AreEqual(0x40u | 0x200u, flags & (0x40u | 0x200u | 0x20u | 0x10u));
        server.Host.Submit(new AdminCommand(AdminCommandKind.EndSession));
        a.WaitFor(0x157);
        Eventually(() => server.Host.Players(), p => p.Count == 0);
    }

    [TestMethod]
    public void CommandQueueIsBounded()
    {
        using var server = new Server();
        // Do not let the loop drain: stop it first.
        server.Host.Stop();
        Thread.Sleep(100);
        var accepted = 0;
        for (var i = 0; i < 1000; i++) if (server.Host.Submit(new AdminCommand(AdminCommandKind.Lock, Value: 1))) accepted++;
        Assert.IsTrue(accepted is > 100 and <= 300, accepted.ToString());
        Assert.IsFalse(server.Host.Submit(new AdminCommand(AdminCommandKind.None)));
    }

    // ---- options ------------------------------------------------------------------------------------------------------

    [TestMethod]
    public void CommandLineBeatsFileBeatsEnvironment()
    {
        var file = Path.Combine(Path.GetTempPath(), $"webui-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, """{ "WebUi": { "Enabled": true, "Port": 9100, "Hostname": "localhost", "CorsOrigins": ["https://a.example", "https://b.example"] } }""");
        Environment.SetEnvironmentVariable("SACRED_GS_WebUi__Port", "9000");
        Environment.SetEnvironmentVariable("SACRED_GS_WebUi__PathBase", "sacred/");
        try
        {
            var viaFile = WebUiOptions.From(WebUiOptions.BuildConfiguration([$"--webui-config={file}"]));
            Assert.IsTrue(viaFile.Enabled);
            Assert.AreEqual(9100, viaFile.Port);                                  // file beats environment
            Assert.AreEqual("/sacred", viaFile.PathBase);                         // environment fills what the file lacks
            CollectionAssert.AreEqual(new[] { "https://a.example", "https://b.example" }, viaFile.CorsOrigins);
            var viaLine = WebUiOptions.From(WebUiOptions.BuildConfiguration([$"--webui-config={file}", "--WebUi:Port=9200", "--webui-hostname", "0.0.0.0", "--tick-ms=20", "-x"]));
            Assert.AreEqual(9200, viaLine.Port);                                  // command line beats file
            Assert.AreEqual("0.0.0.0", viaLine.Hostname);
            var bare = WebUiOptions.From(WebUiOptions.BuildConfiguration(["--webserver", "--webui-port=9300", "--trace"]));
            Assert.IsTrue(bare.Enabled);                                          // a bare switch must not swallow the next argument
            Assert.AreEqual(9300, bare.Port);
            Assert.IsFalse(WebUiOptions.From(WebUiOptions.BuildConfiguration([])).Enabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SACRED_GS_WebUi__Port", null);
            Environment.SetEnvironmentVariable("SACRED_GS_WebUi__PathBase", null);
            File.Delete(file);
        }
    }

    [TestMethod]
    public void ExposedBindWithoutPasswordIsRefused()
    {
        Assert.IsNotNull(WebUiServer.Validate(new WebUiOptions { Hostname = "0.0.0.0" }));
        Assert.IsNull(WebUiServer.Validate(new WebUiOptions { Hostname = "0.0.0.0", Password = "x" }));
        Assert.IsNull(WebUiServer.Validate(new WebUiOptions { Hostname = "127.0.0.1" }));
        Assert.IsNull(WebUiServer.Validate(new WebUiOptions { Hostname = "localhost" }));
    }

    // ---- web layer ----------------------------------------------------------------------------------------------------

    private sealed class FakeAdmin : IServerAdmin
    {
        public readonly List<AdminCommand> Commands = new();
        public readonly List<ChatEntry> Chat = new();
        public ServerOverview Overview() => new("Realm", 5, 1.5, 2006, 2105, 1, 16, false, false, false, false, 0x20, 1, 0, false, false, null, "test");
        public IReadOnlyList<PlayerInfo> Players() => [new PlayerInfo(1, "Alice", "10.0.0.1", "tcp", true, true, true, true, 12, 3.5)];
        public long ChatHead => Chat.Count;
        public IReadOnlyList<ChatEntry> ChatSince(long afterId, int max) => Chat.Where(c => c.Id > afterId).Take(max).ToList();
        public IReadOnlyList<BanEntry> Bans() => [];
        public IReadOnlyList<HeroInfo> Heroes() => [new HeroInfo(1, "Alice", "acct.Alice", 2, 12, 3400, 990, 1, [new SkillInfo(14, 3, 0)], [1, 0, 2, 0, 0, 0], null,
            [new ArtInfo(2, 1097, 6)], 4, [new EquippedItem(12, new ItemInfo(40, 7452, 11, 2, 1, 500))], [new StoredItem(0, 1, 1, new ItemInfo(41, 5000, 3, 0, 0, 20))], [], true, 75)];
        public IReadOnlyList<ModInfo> Mods() => [new ModInfo("ladders", "Ladders <and> boards", null, ModEndpoints().ToArray()), new ModInfo("quiet", "Does things in the background", null, []),
            new ModInfo("broken", null, "line 1: unexpected symbol", [])];
        public IReadOnlyList<ModEndpointInfo> ModEndpoints() => [new ModEndpointInfo("ladders", "by-creature", true, "csv"), new ModEndpointInfo("ladders", "admin", false, null), new ModEndpointInfo("ladders", "page", true, "dashboard", Page: true)];
        public Task<ModResponse> ModRequest(string mod, string path, IReadOnlyList<KeyValuePair<string, string>> query, bool publicOnly, CancellationToken cancellationToken) =>
            Task.FromResult(mod == "ladders" && path == "by-creature" ? new ModResponse(200, "text/csv; charset=utf-8", "creature,kills\r\n551," + (query.FirstOrDefault(q => q.Key == "n").Value ?? "0"))
                : mod == "ladders" && path == "page" ? new ModResponse(200, "text/html; charset=utf-8", "<h1>dash</h1>", Page: true)
                : mod == "ladders" && path == "admin" && !publicOnly ? new ModResponse(200, "text/plain; charset=utf-8", "admin only")
                : new ModResponse(404, "text/plain; charset=utf-8", "no such endpoint"));
        public FloodSettings Flood() => new(true, false, 600, 3000, 2 * 1024 * 1024, 8 * 1024 * 1024, 3, 8, 500, 10, 60, 8, 42, 1, 2);
        public bool Submit(in AdminCommand command) { Commands.Add(command); return true; }
        public CancellationToken StopRequested => default;
    }

    private static async Task<(WebUiServer Server, HttpClient Client, FakeAdmin Admin)> StartWeb(WebUiOptions options)
    {
        var admin = new FakeAdmin();
        var server = new WebUiServer(options, admin, _ => { });
        await server.StartAsync();
        var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.Ports[0]}") };
        return (server, client, admin);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task TheBountyEditorPageIsAdminOnlyAndOffline()
    {
        var (server, client, _) = await StartWeb(new WebUiOptions { Enabled = true, Port = 0, Password = "s3cret" });
        await using (server) using (client)
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/bounties")).StatusCode);
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String("admin:s3cret"u8.ToArray()));
            var page = await client.GetAsync("/bounties");
            Assert.AreEqual(HttpStatusCode.OK, page.StatusCode);
            var html = await page.Content.ReadAsStringAsync();
            StringAssert.Contains(html, "src=\"assets/bounty.js\"");
            var script = await client.GetStringAsync("/assets/bounty.js");
            StringAssert.Contains(script, "mods/'+MOD+'/rules", "reads the mod's rules endpoint");
            StringAssert.Contains(script, "api/mod-store", "writes the mod's storage");
            StringAssert.Contains(html, "href=\"bounties\" aria-current=\"page\"", "the tab");
            StringAssert.Contains(script, "Add a creature");
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(html, @"(src|href)=""https?://|url\(https?://|@import"), "no outside resources: works offline");
            StringAssert.Contains(await client.GetStringAsync("/mods"), "href=\"bounties\"");
        }
    }

    [TestMethod]
    public async Task ApiServesStateAndAcceptsCommands()
    {
        var (server, client, admin) = await StartWeb(new WebUiOptions { Enabled = true, Port = 0 });
        await using (server) using (client)
        {
            using var overview = JsonDocument.Parse(await client.GetStringAsync("/api/overview"));
            Assert.AreEqual("Realm", overview.RootElement.GetProperty("name").GetString());
            using var players = JsonDocument.Parse(await client.GetStringAsync("/api/players"));
            Assert.AreEqual("Alice", players.RootElement[0].GetProperty("name").GetString());
            StringAssert.Contains(await client.GetStringAsync("/"), "<html");

            Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsync("/api/broadcast", Json("""{"text":"  hi\u0007 there "}"""))).StatusCode);
            Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsync("/api/kick", Json("""{"slot":3,"value":8}"""))).StatusCode);
            Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsync("/api/ban", Json("""{"address":"203.0.113.9","reason":"x"}"""))).StatusCode);
            Assert.AreEqual(new AdminCommand(AdminCommandKind.Broadcast, Text: "hi there"), admin.Commands[0]);
            Assert.AreEqual(new AdminCommand(AdminCommandKind.Kick, 3, 8), admin.Commands[1]);
            Assert.AreEqual(new AdminCommand(AdminCommandKind.BanAddress, Text: "203.0.113.9|x"), admin.Commands[2]);

            Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsync("/api/kick", Json("""{"slot":99}"""))).StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsync("/api/ban", Json("""{"address":"not an ip"}"""))).StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsync("/api/broadcast", Json("not json"))).StatusCode);
            Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, (await client.PostAsync("/api/broadcast", new StringContent("text=x", Encoding.UTF8, "application/x-www-form-urlencoded"))).StatusCode);
            Assert.AreEqual(HttpStatusCode.MethodNotAllowed, (await client.GetAsync("/api/nothing")).StatusCode);
            Assert.AreEqual(3, admin.Commands.Count);
        }
    }

    [TestMethod]
    public async Task ChatStreamsAsServerSentEvents()
    {
        var (server, client, admin) = await StartWeb(new WebUiOptions { Enabled = true, Port = 0 });
        await using (server) using (client)
        {
            admin.Chat.Add(new ChatEntry(1, 1000, ChatKind.Public, 1, "Alice", -1, "first"));
            using var response = await client.GetAsync("/api/chat/stream?after=0", HttpCompletionOption.ResponseHeadersRead);
            Assert.AreEqual("text/event-stream", response.Content.Headers.ContentType!.MediaType);
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            string? Next(string prefix) { for (;;) { var line = reader.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult(); if (line is null || line.StartsWith(prefix)) return line; } }
            StringAssert.Contains(Next("data: ")!, "\"text\":\"first\"");
            admin.Chat.Add(new ChatEntry(2, 2000, ChatKind.Broadcast, 0, "", -1, "<b>second</b>"));   // arrives while the stream is open
            Assert.AreEqual("id: 2", Next("id: 2"));
            StringAssert.Contains(Next("data: ")!, "second");
        }
    }

    [TestMethod]
    public async Task PageIsRenderedOnTheServerAndEscapesNetworkText()
    {
        var (server, client, admin) = await StartWeb(new WebUiOptions { Enabled = true, Port = 0 });
        await using (server) using (client)
        {
            admin.Chat.Add(new ChatEntry(1, 1000, ChatKind.Public, 1, "Al<script>", -1, "<img src=x onerror=alert(1)>"));
            var page = await client.GetStringAsync("/");
            StringAssert.Contains(page, "Realm");
            StringAssert.Contains(page, "Al&lt;script&gt;");
            Assert.IsFalse(page.Contains("<img src=x"));
            StringAssert.Contains(page, "action=\"actions/kick\"");
            StringAssert.Contains(await client.GetStringAsync("/fragment"), "Alice");
        }
    }

    [TestMethod]
    public async Task FormsPostAndRedirectBack()
    {
        var (server, _, admin) = await StartWeb(new WebUiOptions { Enabled = true, Port = 0 });
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri($"http://127.0.0.1:{server.Ports[0]}") };
        await using (server)
        {
            var say = await client.PostAsync("/actions/say", new FormUrlEncodedContent(new Dictionary<string, string> { ["to"] = "0", ["text"] = "hello" }));
            Assert.AreEqual(HttpStatusCode.SeeOther, say.StatusCode);
            StringAssert.Contains(say.Headers.Location!.ToString(), "notice=");
            var pm = await client.PostAsync("/actions/say", new FormUrlEncodedContent(new Dictionary<string, string> { ["to"] = "2", ["text"] = "psst" }));
            Assert.AreEqual(HttpStatusCode.SeeOther, pm.StatusCode);
            await client.PostAsync("/actions/mode", new FormUrlEncodedContent(new Dictionary<string, string> { ["mode"] = "2", ["level"] = "3" }));
            Assert.AreEqual(new AdminCommand(AdminCommandKind.Broadcast, Text: "hello"), admin.Commands[0]);
            Assert.AreEqual(new AdminCommand(AdminCommandKind.Message, 2, Text: "psst"), admin.Commands[1]);
            Assert.AreEqual(new AdminCommand(AdminCommandKind.SetMode, 3, 2), admin.Commands[2]);
            // A page on another site must not be able to drive the forms with the browser's credentials.
            var cross = new HttpRequestMessage(HttpMethod.Post, "/actions/shutdown") { Content = new FormUrlEncodedContent(new Dictionary<string, string>()) };
            cross.Headers.Add("Sec-Fetch-Site", "cross-site");
            Assert.AreEqual(HttpStatusCode.Forbidden, (await client.SendAsync(cross)).StatusCode);
            var foreign = new HttpRequestMessage(HttpMethod.Post, "/actions/shutdown") { Content = new FormUrlEncodedContent(new Dictionary<string, string>()) };
            foreign.Headers.Add("Origin", "https://evil.example");
            Assert.AreEqual(HttpStatusCode.Forbidden, (await client.SendAsync(foreign)).StatusCode);
            Assert.AreEqual(3, admin.Commands.Count);
        }
    }

    [TestMethod]
    public async Task SameOriginPostsPassBehindProxiesAndRefusalsExplainThemselves()
    {
        var (server, _, admin) = await StartWeb(new WebUiOptions { Enabled = true, Port = 0 });
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri($"http://127.0.0.1:{server.Ports[0]}") };
        await using (server)
        {
            HttpRequestMessage Post(string origin, params (string, string)[] headers)
            {
                var m = new HttpRequestMessage(HttpMethod.Post, "/actions/lock") { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["value"] = "1" }) };
                m.Headers.Add("Origin", origin);
                foreach (var (k, v) in headers) m.Headers.Add(k, v);
                return m;
            }
            // The browser says same-origin (page opened through a proxy or alias whose Host differs from the Origin).
            Assert.AreEqual(HttpStatusCode.SeeOther, (await client.SendAsync(Post("https://gs.example", ("Sec-Fetch-Site", "same-origin")))).StatusCode);
            // Reverse proxy without Sec-Fetch-Site: the original host arrives in X-Forwarded-Host.
            Assert.AreEqual(HttpStatusCode.SeeOther, (await client.SendAsync(Post("https://gs.example", ("X-Forwarded-Host", "gs.example")))).StatusCode);
            // Chromium sends "Origin: null" on form posts when the referrer policy is no-referrer; the browser's verdict still says same-origin.
            Assert.AreEqual(HttpStatusCode.SeeOther, (await client.SendAsync(Post("null", ("Sec-Fetch-Site", "same-origin")))).StatusCode);
            Assert.AreEqual(3, admin.Commands.Count);
            Assert.AreEqual("same-origin", (await client.GetAsync("/")).Headers.GetValues("Referrer-Policy").Single());
            var refused = await client.SendAsync(Post("https://evil.example"));
            Assert.AreEqual(HttpStatusCode.Forbidden, refused.StatusCode);
            StringAssert.Contains(await refused.Content.ReadAsStringAsync(), "evil.example");
            Assert.AreEqual(HttpStatusCode.Forbidden, (await client.SendAsync(Post("https://gs.example", ("Sec-Fetch-Site", "cross-site")))).StatusCode);
        }
    }

    [TestMethod]
    public async Task HeroesForAdminsAndThePublicApi()
    {
        var (server, client, _) = await StartWeb(new WebUiOptions { Enabled = true, Port = 0 });
        await using (server) using (client)
        {
            using var heroes = JsonDocument.Parse(await client.GetStringAsync("/api/heroes"));
            Assert.AreEqual("acct.Alice", heroes.RootElement[0].GetProperty("account").GetString());
            Assert.AreEqual(1097, heroes.RootElement[0].GetProperty("combatArts")[0].GetProperty("id").GetInt32());
            Assert.AreEqual(7452u, JsonDocument.Parse(await client.GetStringAsync("/api/heroes/1")).RootElement.GetProperty("equipment")[0].GetProperty("item").GetProperty("type").GetUInt32());
            Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync("/api/heroes/9")).StatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync("/public/heroes")).StatusCode, "the public API is off by default");
            // The Mods tab: one card per mod, pages and endpoints apart; the server page no longer lists them.
            var mods = await client.GetStringAsync("/mods");
            StringAssert.Contains(mods, "ladders.lua");
            StringAssert.Contains(mods, "quiet.lua", "mods without pages or endpoints are listed too");
            StringAssert.Contains(mods, "class=\"help\"");
            StringAssert.Contains(mods, "Ladders &lt;and&gt; boards", "descriptions are escaped");
            StringAssert.Contains(mods, "Failed to load: line 1: unexpected symbol");
            var card = mods.IndexOf("ladders.lua", StringComparison.Ordinal);
            var pagesAt = mods.IndexOf("Pages", card, StringComparison.Ordinal);
            var endpointsAt = mods.IndexOf("Endpoints", card, StringComparison.Ordinal);
            Assert.IsTrue(pagesAt > 0 && endpointsAt > pagesAt);
            Assert.IsTrue(mods.IndexOf("by-creature", StringComparison.Ordinal) > endpointsAt, "the endpoint is listed under Endpoints");
            var page = mods.IndexOf("/page", StringComparison.Ordinal);
            Assert.IsTrue(page > pagesAt && page < endpointsAt, "the page is listed under Pages");
            var main = await client.GetStringAsync("/");
            StringAssert.Contains(main, "href=\"mods\"");
            Assert.IsFalse(main.Contains("by-creature"));
        }
        var (open, anonymous, _) = await StartWeb(new WebUiOptions { Enabled = true, Port = 0, PublicApi = true, Password = "secret" });
        await using (open) using (anonymous)
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/heroes")).StatusCode, "the admin API still needs the password");
            var response = await anonymous.GetAsync("/public/heroes/alice");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("*", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
            using var hero = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.IsFalse(hero.RootElement.TryGetProperty("account", out _), "no account names in public");
            Assert.AreEqual(0, hero.RootElement.GetProperty("slot").GetInt32());
            Assert.AreEqual(12, hero.RootElement.GetProperty("level").GetInt32());
            Assert.AreEqual(HttpStatusCode.MethodNotAllowed, (await anonymous.PostAsync("/public/heroes", Json("{}"))).StatusCode);
            // Script endpoints: public ones for everybody, the rest behind the password.
            var csv = await anonymous.GetAsync("/public/mods/ladders/by-creature?n=7");
            Assert.AreEqual(HttpStatusCode.OK, csv.StatusCode);
            Assert.AreEqual("text/csv", csv.Content.Headers.ContentType!.MediaType);
            Assert.AreEqual("creature,kills\r\n551,7", await csv.Content.ReadAsStringAsync());
            StringAssert.Contains(string.Join(";", csv.Headers.GetValues("Content-Security-Policy")), "default-src 'none'");
            Assert.AreEqual(HttpStatusCode.NotFound, (await anonymous.GetAsync("/public/mods/ladders/admin")).StatusCode);
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/mods/ladders/admin")).StatusCode);
            // Pages: sandboxed (opaque origin, cannot use the admin's credentials), no forms, no base, inline script only.
            var page = await anonymous.GetAsync("/public/mods/ladders/page");
            Assert.AreEqual("text/html", page.Content.Headers.ContentType!.MediaType);
            var policy = string.Join(";", page.Headers.GetValues("Content-Security-Policy"));
            foreach (var part in new[] { "sandbox allow-scripts", "default-src 'none'", "form-action 'none'", "base-uri 'none'", "frame-ancestors *" })
                StringAssert.Contains(policy, part);
            Assert.IsFalse(policy.Contains("allow-same-origin"));
            Assert.AreEqual("nosniff", page.Headers.GetValues("X-Content-Type-Options").Single());
            using var list = JsonDocument.Parse(await anonymous.GetStringAsync("/public/mods"));
            Assert.AreEqual(2, list.RootElement.GetArrayLength());                 // the public page and the public endpoint
        }
    }

    [TestMethod]
    public async Task FloodLimitsAreShownAndChangedLive()
    {
        var (server, _, admin) = await StartWeb(new WebUiOptions { Enabled = true, Port = 0 });
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri($"http://127.0.0.1:{server.Ports[0]}") };
        await using (server)
        {
            using var flood = JsonDocument.Parse(await client.GetStringAsync("/api/flood"));
            Assert.AreEqual(600, flood.RootElement.GetProperty("messagesPerSecond").GetInt32());
            Assert.AreEqual(42, flood.RootElement.GetProperty("drops").GetInt64());
            var page = await client.GetStringAsync("/");
            StringAssert.Contains(page, "id=\"flood\"");
            StringAssert.Contains(page, "42 dropped");
            static FormUrlEncodedContent Form(params (string, string)[] pairs) => new(pairs.Select(p => KeyValuePair.Create(p.Item1, p.Item2)));
            // Unchanged values: no command.
            var same = await client.PostAsync("/actions/flood-form", Form(("enabled", "0"), ("enabled", "1"), ("msg", "600"), ("kbps", "2048"), ("chat", "3")));
            StringAssert.Contains(Uri.UnescapeDataString(same.Headers.Location!.ToString()), "Nothing to change");
            Assert.AreEqual(0, admin.Commands.Count);
            // Harden: fewer messages, less data, less chat; only those keys are sent, in one command.
            var hard = await client.PostAsync("/actions/flood-form", Form(("enabled", "0"), ("enabled", "1"), ("msg", "100"), ("kbps", "512"), ("chat", "1"), ("kick", "500")));
            Assert.AreEqual(HttpStatusCode.SeeOther, hard.StatusCode);
            StringAssert.Contains(Uri.UnescapeDataString(hard.Headers.Location!.ToString()), "applied live");
            Assert.AreEqual(new AdminCommand(AdminCommandKind.SetFlood, Text: "FLOOD_MESSAGES_PER_SECOND=100;FLOOD_BYTES_PER_SECOND=524288;FLOOD_CHAT_PER_SECOND=1"), admin.Commands[0]);
            // Unchecked box (only the hidden 0): turn it off.
            await client.PostAsync("/actions/flood-form", Form(("enabled", "0")));
            Assert.AreEqual("FLOOD_PROTECTION=0", admin.Commands[1].Text);
            // JSON API: validated keys and values.
            Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsync("/api/flood", Json("""{"text":"FLOOD_CHAT_BURST=2;max-connections-per-address=3"}"""))).StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsync("/api/flood", Json("""{"text":"PASSWORD=1"}"""))).StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsync("/api/flood", Json("""{"text":"FLOOD_CHAT_BURST=-1"}"""))).StatusCode);
            Assert.AreEqual(3, admin.Commands.Count);
        }
    }

    [TestMethod]
    public void FloodChangesApplyToTheRunningServerWithoutKicking()
    {
        using var server = new Server();
        server.Host.FloodClock = () => 1000;                                  // no refill while the test sends
        using var a = RawClient.Admit(server.Port, "Alice", 1);
        using var b = RawClient.Admit(server.Port, "Bob", 2);
        Assert.IsFalse(server.Host.Flood().Enabled);
        Assert.IsTrue(server.Host.Submit(new AdminCommand(AdminCommandKind.SetFlood, Text: "FLOOD_PROTECTION=1;FLOOD_CHAT_PER_SECOND=1;FLOOD_CHAT_BURST=2;FLOOD_KICK_AFTER_DROPS=0")));
        var flood = Eventually(() => server.Host.Flood(), f => f.Enabled);
        Assert.IsTrue(flood.Enabled);
        Assert.AreEqual(2, flood.ChatBurst);
        Assert.AreEqual(2, Eventually(() => server.Host.Players(), p => p.Count == 2).Count, "nobody is kicked by a change");
        for (var i = 0; i < 10; i++) a.Send(0x005, Text("spam"));
        var received = 0;
        var deadline = Environment.TickCount64 + 600;
        while (Environment.TickCount64 < deadline)
            if (b.TryReceive((int)Math.Max(1, deadline - Environment.TickCount64)) is { Opcode: 0x005 }) received++;
        Assert.AreEqual(2, received, "the new chat limit applies at once");
        Assert.IsTrue(server.Host.ChatSince(0, 100).Any(e => e.Kind == ChatKind.System && e.Text.StartsWith("Flood protection on", StringComparison.Ordinal)));
        Assert.IsTrue(server.Host.Submit(new AdminCommand(AdminCommandKind.SetFlood, Text: "FLOOD_PROTECTION=0")));
        Assert.IsFalse(Eventually(() => server.Host.Flood(), f => !f.Enabled).Enabled);
    }

    [TestMethod]
    public async Task SettingsFormSavesOnlyWhatChanged()
    {
        var (server, _, admin) = await StartWeb(new WebUiOptions { Enabled = true, Port = 0 });
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri($"http://127.0.0.1:{server.Ports[0]}") };
        await using (server)
        {
            static FormUrlEncodedContent Form(params (string, string)[] pairs) => new(pairs.Select(p => KeyValuePair.Create(p.Item1, p.Item2)));
            // Same values as the fake server's state (Realm, 16 players, mode 1, level 0, no password): nothing to do.
            var same = await client.PostAsync("/actions/settings", Form(("text", "Realm"), ("max", "16"), ("password", ""), ("mode", "1"), ("level", "0")));
            StringAssert.Contains(Uri.UnescapeDataString(same.Headers.Location!.ToString()), "Nothing to change");
            Assert.AreEqual(0, admin.Commands.Count);
            var saved = await client.PostAsync("/actions/settings", Form(("text", "New realm"), ("max", "8"), ("password", "pw"), ("mode", "2"), ("level", "3")));
            Assert.AreEqual(HttpStatusCode.SeeOther, saved.StatusCode);
            CollectionAssert.AreEqual(new[] { AdminCommandKind.SetName, AdminCommandKind.SetMaxPlayers, AdminCommandKind.SetPassword, AdminCommandKind.SetMode }, admin.Commands.Select(c => c.Kind).ToArray());
            Assert.AreEqual(new AdminCommand(AdminCommandKind.SetMode, 3, 2), admin.Commands[3]);
            var bad = await client.PostAsync("/actions/settings", Form(("text", "Realm"), ("max", "99"), ("mode", "1"), ("level", "0")));
            StringAssert.Contains(Uri.UnescapeDataString(bad.Headers.Location!.ToString()), "1..16");
        }
    }

    [TestMethod]
    public async Task PasswordCorsFramingAndHostChecks()
    {
        var options = new WebUiOptions { Enabled = true, Port = 0, Password = "s3cret", CorsOrigins = ["https://panel.example"], FrameAncestors = ["https://panel.example"] };
        var (server, client, _) = await StartWeb(options);
        await using (server) using (client)
        {
            var denied = await client.GetAsync("/api/overview");
            Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
            Assert.IsTrue(denied.Headers.WwwAuthenticate.Any());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String("admin:wrong"u8.ToArray()));
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/overview")).StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String("admin:s3cret"u8.ToArray()));
            Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/api/overview")).StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cret");
            var ok = await client.GetAsync("/");
            Assert.AreEqual(HttpStatusCode.OK, ok.StatusCode);
            StringAssert.Contains(ok.Headers.GetValues("Content-Security-Policy").Single(), "frame-ancestors https://panel.example");
            Assert.IsFalse(ok.Headers.Contains("X-Frame-Options"));

            var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/kick");
            preflight.Headers.Add("Origin", "https://panel.example");
            var allowed = await client.SendAsync(preflight);
            Assert.AreEqual(HttpStatusCode.NoContent, allowed.StatusCode);
            Assert.AreEqual("https://panel.example", allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());
            var foreign = new HttpRequestMessage(HttpMethod.Get, "/api/overview");
            foreign.Headers.Add("Origin", "https://evil.example");
            Assert.AreEqual(HttpStatusCode.Forbidden, (await client.SendAsync(foreign)).StatusCode);
        }

        var (loopback, plain, _) = await StartWeb(new WebUiOptions { Enabled = true, Port = 0 });
        await using (loopback) using (plain)
        {
            var response = await plain.GetAsync("/");
            Assert.AreEqual("DENY", response.Headers.GetValues("X-Frame-Options").Single());
            var rebinding = new HttpRequestMessage(HttpMethod.Get, "/api/overview");
            rebinding.Headers.Host = "attacker.example";
            Assert.AreEqual(HttpStatusCode.BadRequest, (await plain.SendAsync(rebinding)).StatusCode);
        }
    }

    [TestMethod]
    public async Task StyleSheetsAndScriptsAreServedAsFilesAndTemplatesStayInside()
    {
        var (server, client, _) = await StartWeb(new WebUiOptions { Enabled = true, Port = 0 });
        await using (server) using (client)
        {
            var page = await client.GetStringAsync("/");
            StringAssert.Contains(page, "href=\"assets/app.css\"");
            StringAssert.Contains(page, "src=\"assets/app.js\"");
            Assert.IsFalse(page.Contains("<style>", StringComparison.Ordinal), "no inline style");
            var css = await client.GetAsync("/assets/app.css");
            Assert.AreEqual(HttpStatusCode.OK, css.StatusCode);
            StringAssert.StartsWith(css.Content.Headers.ContentType!.ToString(), "text/css");
            StringAssert.Contains(await css.Content.ReadAsStringAsync(), "--bg");
            var js = await client.GetAsync("/assets/app.js");
            StringAssert.StartsWith(js.Content.Headers.ContentType!.ToString(), "text/javascript");
            // The validator: a repeat request with it is answered without a body.
            var again = new HttpRequestMessage(HttpMethod.Get, "/assets/app.css");
            again.Headers.TryAddWithoutValidation("If-None-Match", css.Headers.ETag!.ToString());
            Assert.AreEqual(HttpStatusCode.NotModified, (await client.SendAsync(again)).StatusCode);
            // Only style sheets and scripts, by plain name: templates, data and anything else are 404.
            foreach (var other in new[] { "page.html", "icons.txt", "..%2Fapp.css", "app.css%2F..%2Fpage.html", "nope.js" })
                Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync("/assets/" + other)).StatusCode, other);
        }
    }
}
