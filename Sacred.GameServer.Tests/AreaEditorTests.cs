using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Sacred.GameServer.Data;
using Sacred.GameServer.Scripting;
using Sacred.GameServer.Web;

namespace Sacred.GameServer.Tests;

/// <summary>The admin area editor: the sector table and map geometry, the PNG, the page and its endpoints (auth, content types, limits),
/// the admin's write into a mod's storage and the world_events script picking the saved areas up.</summary>
[TestClass]
public class AreaEditorTests
{
    // ---- the geometry and the files -------------------------------------------------------------------------------------------

    [TestMethod]
    public void TheProjectionIsTheGamesOwnWorldMapProjection()
    {
        // Sacred.World's WorldMapProjection (Gold 0x6CB3B0) for the Ancaria map: map = (combined - (0, 768)) * width / 2048
        var p = AreaMap.ProjectionFor(2048);
        var (x0, y0) = AreaMap.ToMap(p, 0, 0);
        Assert.AreEqual(1018.7499389648438, x0, 1e-9);
        Assert.AreEqual(-186.9473876953125 - 768, y0, 1e-9);
        var (x1, y1) = AreaMap.ToMap(p, 100, 40);
        Assert.AreEqual(1018.7499389648438 + 60 * 48 * 0.007446, x1, 1e-6, "x grows with tx - ty");
        Assert.AreEqual(-186.9473876953125 - 768 + 140 * 24 * 0.014914, y1, 1e-6, "y grows with tx + ty");
        // a bigger atlas scales it
        var big = AreaMap.ProjectionFor(4096);
        Assert.AreEqual(p.Kx * 2, big.Kx, 1e-12);
        Assert.AreEqual(p.Oy * 2, big.Oy, 1e-9);
    }

    private static byte[] Keyx(params (uint Id, int RawX, int RawY, string Name)[] sectors)
    {
        var bytes = new byte[0x100 + sectors.Length * 0x300];
        BitConverter.GetBytes((uint)sectors.Length).CopyTo(bytes, 4);
        for (var i = 0; i < sectors.Length; i++)
        {
            var at = 0x100 + i * 0x300;
            Encoding.Latin1.GetBytes(sectors[i].Name).CopyTo(bytes, at);
            BitConverter.GetBytes(sectors[i].Id).CopyTo(bytes, at + 0x24);
            BitConverter.GetBytes(sectors[i].RawX).CopyTo(bytes, at + 0x3C);
            BitConverter.GetBytes(sectors[i].RawY).CopyTo(bytes, at + 0x40);
        }
        return bytes;
    }

    [TestMethod]
    public void SectorsComeFromTheKeyxWithTheirGridCell()
    {
        var path = Path.Combine(Path.GetTempPath(), "sacred-keyx-" + Guid.NewGuid().ToString("N") + ".keyx");
        try
        {
            const double sector = AreaMap.WorldUnitsPerTile * AreaMap.TilesPerSector;
            File.WriteAllBytes(path, Keyx(
                (3451, (int)(37 * sector) + 10, (int)(52 * sector) + 10, "Ancaria\u0000junk"),
                (3452, (int)(38 * sector) + 10, (int)(52 * sector) + 10, "Silver\tCreek"),
                (0, 0, 0, "ignored"),                                   // id 0 is no sector
                (9, (int)(0 * sector), (int)(127 * sector) + 10, "")));
            var sectors = AreaMap.ReadSectors(path);
            Assert.AreEqual(3, sectors.Count);
            Assert.AreEqual((3451u, 37, 52), (sectors[0].Id, sectors[0].CellX, sectors[0].CellY));
            Assert.AreEqual((3452u, 38, 52), (sectors[1].Id, sectors[1].CellX, sectors[1].CellY));
            Assert.AreEqual((9u, 0, 127), (sectors[2].Id, sectors[2].CellX, sectors[2].CellY));
            Assert.AreEqual("Ancaria", sectors[0].Name);
            Assert.AreEqual("Silver Creek", sectors[1].Name, "control characters become spaces");
            // a truncated file is refused, not read past its end
            File.WriteAllBytes(path, Keyx((1, 0, 0, "x"))[..0x200]);
            Assert.ThrowsException<InvalidDataException>(() => AreaMap.ReadSectors(path));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void ThePngIsAValidTruecolourImage()
    {
        byte[] rgba = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 10, 20, 30, 255, 40, 50, 60, 255, 70, 80, 90, 255];   // 3 x 2
        var png = AreaMap.EncodePng(3, 2, rgba);
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        var chunks = new List<(string Type, byte[] Data, uint Crc)>();
        for (var at = 8; at < png.Length;)
        {
            var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var type = Encoding.ASCII.GetString(png, at + 4, 4);
            var data = png[(at + 8)..(at + 8 + length)];
            var crc = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length));
            Assert.AreEqual(AreaMap.Crc32(png.AsSpan(at + 4, 4 + length)), crc, type + " CRC");
            chunks.Add((type, data, crc));
            at += 12 + length;
        }
        CollectionAssert.AreEqual(new[] { "IHDR", "IDAT", "IEND" }, chunks.Select(c => c.Type).ToArray());
        Assert.AreEqual(3, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(chunks[0].Data));
        Assert.AreEqual(2, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(chunks[0].Data.AsSpan(4)));
        Assert.AreEqual(8, chunks[0].Data[8]); Assert.AreEqual(2, chunks[0].Data[9]);
        using var inflated = new ZLibStream(new MemoryStream(chunks[1].Data), CompressionMode.Decompress);
        var raw = new MemoryStream(); inflated.CopyTo(raw);
        CollectionAssert.AreEqual(new byte[] { 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 10, 20, 30, 40, 50, 60, 70, 80, 90 }, raw.ToArray()[..10].Concat(raw.ToArray()[10..]).ToArray()[..20]);
        Assert.AreEqual(0xAE426082u, chunks[2].Crc, "the CRC of an empty IEND is the well-known one");
    }

    // ---- the web layer -------------------------------------------------------------------------------------------------------

    private sealed class Admin : IServerAdmin
    {
        public readonly List<(string Mod, string? Key, string? Body)> Stores = [];
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
        public readonly List<AdminCommand> Commands = new();
        public bool Submit(in AdminCommand command) { Commands.Add(command); return true; }
        public CancellationToken StopRequested => default;
        public bool HasMap = true;
        public Task<BinaryResponse> AreaMapRequest(bool image, CancellationToken cancellationToken) =>
            Task.FromResult(!HasMap ? new BinaryResponse(404, "text/plain", "the area map needs --game-dir"u8.ToArray())
                : image ? new BinaryResponse(200, "image/png", AreaMap.EncodePng(1, 1, [1, 2, 3, 255]))
                : new BinaryResponse(200, "application/json; charset=utf-8", """{"available":true,"width":2048,"height":2048,"sectors":[{"id":3451,"x":37,"y":52}]}"""u8.ToArray()));
        public Task<ModResponse> ModStoreRequest(string mod, string? key, string? body, CancellationToken cancellationToken)
        {
            Stores.Add((mod, key, body));
            return Task.FromResult(mod == "nope" ? new ModResponse(404, "text/plain", "no such mod") : new ModResponse(200, "application/json", key is null ? """{"ok":true}""" : """{"name":"Silver Creek"}"""));
        }
    }

    private static async Task<(WebUiServer Server, HttpClient Client, Admin Admin)> Start(WebUiOptions options)
    {
        var admin = new Admin();
        var server = new WebUiServer(options, admin, _ => { });
        await server.StartAsync();
        return (server, new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.Ports[0]}") }, admin);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task ThePageIsAdminOnlyOfflineAndFollowsTheTheme()
    {
        var (server, client, _) = await Start(new WebUiOptions { Enabled = true, Port = 0, Password = "s3cret" });
        await using (server) using (client)
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/areas")).StatusCode);
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/areas/map")).StatusCode);
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/areas/map.png")).StatusCode);
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/mod-store", Json("{}"))).StatusCode);
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/mod-store?mod=world_events&key=areas")).StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String("admin:s3cret"u8.ToArray()));
            var page = await client.GetAsync("/areas");
            Assert.AreEqual(HttpStatusCode.OK, page.StatusCode);
            var html = await page.Content.ReadAsStringAsync();
            StringAssert.Contains(html, "<canvas id=\"cv\"");
            StringAssert.Contains(html, "width=device-width");
            // style and script are files the page links to (served from /assets/), not inline
            StringAssert.Contains(html, "href=\"assets/areas.css\"");
            StringAssert.Contains(html, "src=\"assets/areas.js\"");
            StringAssert.Contains(await client.GetStringAsync("/assets/app.css"), "prefers-color-scheme:light", "the admin UI's theme variables");
            var areaScript = await client.GetStringAsync("/assets/areas.js");
            StringAssert.Contains(areaScript, "pointerdown", "pointer events: mouse and touch");
            StringAssert.Contains(areaScript, "api/mod-store");
            StringAssert.Contains(html, "href=\"areas\" aria-current=\"page\"", "the tab");
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(html, @"(src|href)=""https?://|url\(https?://|@import"), "no outside resources: works offline");
            // the other pages link to it
            StringAssert.Contains(await client.GetStringAsync("/mods"), "href=\"areas\"");
        }
    }

    [TestMethod]
    public async Task TheEventsPageAndTheReloadButtonsAreAdminOnlyAndOffline()
    {
        var (server, client, admin) = await Start(new WebUiOptions { Enabled = true, Port = 0, Password = "s3cret" });
        await using (server) using (client)
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/events")).StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String("admin:s3cret"u8.ToArray()));
            var html = await client.GetStringAsync("/events");
            StringAssert.Contains(html, "href=\"events\" aria-current=\"page\"");
            StringAssert.Contains(html, "src=\"assets/events.js\"");
            var eventsScript = await client.GetStringAsync("/assets/events.js");
            StringAssert.Contains(eventsScript, "mods/'+MOD+'/events");
            StringAssert.Contains(eventsScript, "api/mod-store");
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(html, @"(src|href)=""https?://|url\(https?://|@import"), "no outside resources: works offline");
            StringAssert.Contains(await client.GetStringAsync("/mods"), "href=\"events\"");
            // reload commands: all mods (empty) or one; a bad name is refused
            var all = await client.PostAsync("/api/reload-mods", Json("{\"text\":\"\"}"));
            Assert.AreEqual(HttpStatusCode.OK, all.StatusCode);
            var one = await client.PostAsync("/api/reload-mods", Json("{\"text\":\"world_events\"}"));
            Assert.AreEqual(HttpStatusCode.OK, one.StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsync("/api/reload-mods", Json("{\"text\":\"../x\"}"))).StatusCode);
            CollectionAssert.AreEqual(new[] { AdminCommandKind.ReloadMods, AdminCommandKind.ReloadMods }, admin.Commands.Select(c => c.Kind).ToArray());
            Assert.AreEqual("", admin.Commands[0].Text);
            Assert.AreEqual("world_events", admin.Commands[1].Text);
        }
    }

    [TestMethod]
    public async Task TheMapEndpointsServeTheDataOrSayWhyNot()
    {
        var (server, client, admin) = await Start(new WebUiOptions { Enabled = true, Port = 0 });
        await using (server) using (client)
        {
            var json = await client.GetAsync("/api/areas/map");
            Assert.AreEqual(HttpStatusCode.OK, json.StatusCode);
            StringAssert.Contains(await json.Content.ReadAsStringAsync(), "\"sectors\"");
            var png = await client.GetAsync("/api/areas/map.png");
            Assert.AreEqual("image/png", png.Content.Headers.ContentType?.MediaType);
            StringAssert.Contains(string.Join(",", png.Headers.CacheControl?.ToString()), "max-age=3600");
            CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, (await png.Content.ReadAsByteArrayAsync())[..4]);
            admin.HasMap = false;
            var missing = await client.GetAsync("/api/areas/map");
            Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
            StringAssert.Contains(await missing.Content.ReadAsStringAsync(), "--game-dir");
        }
    }

    [TestMethod]
    public async Task WritingModStorageIsJsonOnlyLimitedAndNotFromAnotherSite()
    {
        var (server, client, admin) = await Start(new WebUiOptions { Enabled = true, Port = 0 });
        await using (server) using (client)
        {
            var body = """{"mod":"world_events","set":{"areas":{"silver_creek":{"name":"Silver Creek","sectors":[3451,[3453,3456]]}}}}""";
            var ok = await client.PostAsync("/api/mod-store", Json(body));
            Assert.AreEqual(HttpStatusCode.OK, ok.StatusCode);
            Assert.AreEqual("world_events", admin.Stores.Last().Mod);
            Assert.IsNull(admin.Stores.Last().Key);
            Assert.AreEqual(body, admin.Stores.Last().Body);
            // only JSON: a cross-site form post cannot send it without a preflight
            Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, (await client.PostAsync("/api/mod-store", new StringContent("mod=x", Encoding.UTF8, "application/x-www-form-urlencoded"))).StatusCode);
            var cross = new HttpRequestMessage(HttpMethod.Post, "/api/mod-store") { Content = Json(body) };
            cross.Headers.Add("Sec-Fetch-Site", "cross-site");
            Assert.AreEqual(HttpStatusCode.Forbidden, (await client.SendAsync(cross)).StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsync("/api/mod-store", Json("not json"))).StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsync("/api/mod-store", Json("""{"set":{}}"""))).StatusCode, "no mod");
            Assert.AreEqual(HttpStatusCode.NotFound, (await client.PostAsync("/api/mod-store", Json("""{"mod":"nope","set":{}}"""))).StatusCode);
            var calls = admin.Stores.Count;
            // too large: refused before it reaches the game loop
            var big = Json("{\"mod\":\"world_events\",\"set\":{\"areas\":\"" + new string('x', 600 * 1024) + "\"}}");
            try { Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync("/api/mod-store", big)).StatusCode); }
            catch (HttpRequestException) { }                      // Kestrel may close the connection while the client is still sending
            // a body above the web UI's usual 16 KB but within this endpoint's limit passes
            var medium = Json("{\"mod\":\"world_events\",\"set\":{\"areas\":\"" + new string('x', 100 * 1024) + "\"}}");
            using var fresh = new HttpClient { BaseAddress = client.BaseAddress };            // the other connection was closed by the refusal
            Assert.AreEqual(HttpStatusCode.OK, (await fresh.PostAsync("/api/mod-store", medium)).StatusCode);
            calls++;
            Assert.AreEqual(calls, admin.Stores.Count);
            // reading
            var read = await fresh.GetAsync("/api/mod-store?mod=world_events&key=areas");
            Assert.AreEqual(HttpStatusCode.OK, read.StatusCode);
            Assert.AreEqual(("world_events", "areas"), (admin.Stores.Last().Mod, admin.Stores.Last().Key));
            Assert.AreEqual(HttpStatusCode.BadRequest, (await fresh.GetAsync("/api/mod-store?mod=world_events")).StatusCode);
        }
    }

    // ---- the storage on the game loop, and the script ----------------------------------------------------------------------

    private sealed class Folder : IDisposable
    {
        public readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sacred-ae-" + Guid.NewGuid().ToString("N"));
        public string Scripts => System.IO.Path.Combine(Path, "scripts");
        public string Data => System.IO.Path.Combine(Path, "data");
        public Folder() { Directory.CreateDirectory(Scripts); }
        public void Write(string name, string source) { File.WriteAllText(System.IO.Path.Combine(Scripts, name), source); ExampleAssets.CopyTo(Scripts); }
        public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
    }

    private sealed class Fake : IScriptServer
    {
        public long Now;
        public readonly List<(int Slot, string Text)> Told = [];
        public readonly List<ScriptPlayer> All = [new ScriptPlayer(1, "Alice", "name:Alice", 2, 10, true, true, 0, Sector: 2911)];
        public long NowMs => Now;
        public IReadOnlyList<ScriptPlayer> Players() => All;
        public ScriptPlayer? Player(int slot) => All.FirstOrDefault(p => p.Slot == slot);
        public void Broadcast(string text) { }
        public void Message(int slot, string text) => Told.Add((slot, text));
        public void Kick(int slot, string reason) { }
        public ScriptDrop? DropItem(int slot, in ScriptLootRequest request, out string? error) { error = "no"; return null; }
        public ScriptDrop? DropGold(int slot, uint amount, out string? error) { error = "no"; return null; }
    }

    private static (ScriptHost Host, Fake Server) Start(Folder folder, string script, long maxChars = 8 * 1024 * 1024)
    {
        folder.Write("world_events.lua", script);
        var server = new Fake();
        var host = new ScriptHost(new ScriptOptions { Directory = folder.Scripts, DataDirectory = folder.Data, MaxModStoreChars = maxChars }, server);
        host.Load();
        return (host, server);
    }

    [TestMethod]
    public void TheAdminWritesAndReadsAModsStorage()
    {
        using var folder = new Folder();
        var (host, _) = Start(folder, "server.on('start', function() end)");
        string Write(string body) { var r = host.StoreRequest("World_Events", null, body); return $"{r.Status} {r.Body}"; }
        Assert.AreEqual("200 {\"ok\":true,\"revisions\":{\"areas\":1}}", Write("""{"mod":"x","set":{"areas":{"a":{"name":"A","sectors":[1,[3,5]]}}}}"""));
        var read = host.StoreRequest("world_events", "areas", null);
        Assert.AreEqual(200, read.Status);
        Assert.AreEqual("""{"a":{"name":"A","sectors":[1,[3,5]]}}""", read.Body);
        StringAssert.Contains(Write("""{"mod":"x","set":{"areas":{}}}"""), "\"areas\":2", "the revision goes up with every write");
        Assert.IsTrue(host.StoreOf("world_events").TryGet("areas_rev", out var rev)); Assert.AreEqual(2.0, rev);
        // deleting
        Assert.AreEqual(200, host.StoreRequest("world_events", null, """{"mod":"x","delete":["areas"]}""").Status);
        Assert.AreEqual(404, host.StoreRequest("world_events", "areas", null).Status);
        // what is refused
        Assert.AreEqual(404, host.StoreRequest("other", "areas", null).Status, "no such mod");
        Assert.AreEqual(400, host.StoreRequest("world_events", "../x", null).Status, "keys are plain");
        Assert.AreEqual(400, host.StoreRequest("world_events", null, """{"set":{"@hero":{}}}""").Status, "hero data is out of reach");
        Assert.AreEqual(400, host.StoreRequest("world_events", null, """{"set":{"areas_rev":5}}""").Status, "revisions are the server's");
        Assert.AreEqual(400, host.StoreRequest("world_events", null, "[1,2]").Status);
        Assert.AreEqual(400, host.StoreRequest("world_events", null, "{oops").Status);
        Assert.AreEqual(404, host.StoreRequest("world_events", "never", null).Status);
    }

    [TestMethod]
    public void TheStorageLimitStopsAWriteAndChangesNothing()
    {
        using var folder = new Folder();
        var (host, _) = Start(folder, "server.on('start', function() end)", maxChars: 4096);
        Assert.AreEqual(200, host.StoreRequest("world_events", null, """{"set":{"small":{"a":1}}}""").Status);
        var big = "{\"set\":{\"areas\":\"" + new string('x', 8000) + "\"}}";
        Assert.AreEqual(413, host.StoreRequest("world_events", null, big).Status);
        Assert.AreEqual(404, host.StoreRequest("world_events", "areas", null).Status, "nothing was stored");
        Assert.AreEqual(200, host.StoreRequest("world_events", "small", null).Status);
    }

    private static string WorldEvents()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Sacred.GameServer", "scripts-examples", "world_events.lua");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException("world_events.lua");
    }

    [TestMethod]
    public void AreasSavedFromTheEditorAreUsedByTheScriptWithinSeconds()
    {
        using var folder = new Folder();
        var (host, server) = Start(folder, WorldEvents().Replace("first_delay = 60,", "first_delay = 0,"));
        // before: the script file's silver_creek has no sector; !here says no area
        var alice = server.All[0];
        Assert.IsTrue(host.FireChat(alice, "!here"));
        Assert.IsFalse(server.Told.Last().Text.Contains("area "), server.Told.Last().Text);
        // the editor saves an area through the admin endpoint
        var saved = host.StoreRequest("world_events", null, """{"mod":"world_events","set":{"areas":{"silver_creek":{"name":"Silver Creek","sectors":[2909,[2910,2912]],"circle":{"x":5000,"y":6000,"r":800}}}}}""");
        Assert.AreEqual(200, saved.Status, saved.Body);
        for (var i = 0; i < 6; i++) { server.Now += 2000; host.Pump(server.Now); }    // the script looks for new areas every few ticks
        Assert.IsTrue(host.FireChat(alice, "!here"));
        StringAssert.Contains(server.Told.Last().Text, "area silver_creek");
        // the endpoints for the page
        var areas = host.HandleRequest("world_events", "areas", [], publicOnly: false);
        Assert.AreEqual(200, areas.Status);
        using var doc = JsonDocument.Parse(areas.Body);
        var silver = doc.RootElement.GetProperty("areas").GetProperty("silver_creek");
        Assert.AreEqual("saved", silver.GetProperty("source").GetString());
        Assert.AreEqual("Silver Creek", silver.GetProperty("name").GetString());
        Assert.AreEqual(2, silver.GetProperty("sectors").GetArrayLength() - 0);
        Assert.AreEqual(404, host.HandleRequest("world_events", "areas", [], publicOnly: true).Status, "admin only");
        var where = JsonDocument.Parse(host.HandleRequest("world_events", "whereabouts", [], publicOnly: false).Body).RootElement.GetProperty("players");
        Assert.AreEqual("Alice", where[0].GetProperty("name").GetString());
        Assert.AreEqual(2911, where[0].GetProperty("sector").GetInt32());
        Assert.AreEqual(404, host.HandleRequest("world_events", "whereabouts", [], publicOnly: true).Status, "admin only");
        // and the event offered is now the one tied to the area: it is no longer skipped
        Assert.AreEqual(0, host.Errors);
    }

    [TestMethod]
    public void ThePagesCodecWritesTheSameAreasTheScriptAndTheModWrite()
    {
        var node = new[] { "node", "node.exe" }.FirstOrDefault(n => { try { using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(n, "--version") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true }); p!.WaitForExit(5000); return p.ExitCode == 0; } catch (Exception) { return false; } });
        if (node is null) { Assert.Inconclusive("node is not installed"); return; }
        var script = AreaEditorAssets.Script;
        var from = script.IndexOf("/* ---- areas:", StringComparison.Ordinal);
        var to = script.IndexOf("function color(", StringComparison.Ordinal);
        Assert.IsTrue(from > 0 && to > from);
        var code = "var window={}; var cellId={}; var TILE=53.66563034057617, ST=64; " + script[from..to] + """
            var AreaCodec = window.AreaCodec;
            var ids = {}; [2910, 2911, 2912, 2920, 2915, 2911].forEach(function(i){ ids[i] = true; });
            var area = { id: 'silver_creek', name: 'Silver "Creek"', ids: ids, near: 0, circle: { x: 5000.4, y: 6000.6, r: 800 } };
            var back = AreaCodec.fromDefIds(AreaCodec.toDef(area));
            console.log(JSON.stringify({
              snippet: AreaCodec.snippet(area),
              def: AreaCodec.toDef(area),
              compact: AreaCodec.compact([1, 3, 4, 5, 9, 10]),
              slug: [AreaCodec.slug('Silver Creek'), AreaCodec.slug('  Fort  Dun-Mor! '), AreaCodec.slug('123 Gate'), AreaCodec.slug('!!!')],
              roundTrip: Object.keys(back).map(Number).sort(function(a, b){ return a - b; })
            }));
            """;
        var path = Path.Combine(Path.GetTempPath(), "sacred-codec-" + Guid.NewGuid().ToString("N") + ".js");
        File.WriteAllText(path, code);
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(node, $"\"{path}\"") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true })!;
            var output = process.StandardOutput.ReadToEnd(); var error = process.StandardError.ReadToEnd();
            process.WaitForExit(10000);
            Assert.AreEqual(0, process.ExitCode, error);
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            Assert.AreEqual("silver_creek = { name = \"Silver \\\"Creek\\\"\", sectors = {{2910, 2912}, 2915, 2920}, circle = { x = 5000, y = 6001, r = 800 } },", root.GetProperty("snippet").GetString(),
                "the same line the server's !area show and the sector-tools mod write");
            Assert.AreEqual("[1,[3,5],9,10]", root.GetProperty("compact").GetRawText(), "runs of three or more become ranges");
            CollectionAssert.AreEqual(new[] { "silver_creek", "fort_dun_mor", "area_123_gate", "area" }, root.GetProperty("slug").EnumerateArray().Select(e => e.GetString()).ToArray());
            CollectionAssert.AreEqual(new[] { 2910, 2911, 2912, 2915, 2920 }, root.GetProperty("roundTrip").EnumerateArray().Select(e => e.GetInt32()).ToArray());
            Assert.AreEqual("[[2910,2912],2915,2920]", root.GetProperty("def").GetProperty("sectors").GetRawText());
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void FormControlsAreThemedScopedToTheirPageAndNeverTouchTheSharedStylesheet()
    {
        var css = FormControls.Css(".ev", ".ae");
        // every rule (outside the media block) starts with one of the scopes: nothing leaks into other pages
        var main = css[..css.IndexOf("@media", StringComparison.Ordinal)];
        foreach (var rule in main.Split((char)10, StringSplitOptions.RemoveEmptyEntries))
            foreach (var selector in rule[..rule.IndexOf('{')].Split(','))
                Assert.IsTrue(selector.StartsWith(".ev ") || selector.StartsWith(".ae "), selector);
        StringAssert.Contains(css, ".ev input[type=checkbox],.ev input[type=radio],.ae input[type=checkbox],.ae input[type=radio]{appearance:none");
        StringAssert.Contains(css, "input[type=checkbox]:checked,.ev input[type=radio]:checked", "the checked state");
        StringAssert.Contains(css, ":focus-visible{outline:2px solid var(--gold2)", "a visible focus ring");
        StringAssert.Contains(css, "label.check,.ae label.chk,.ae label.check{display:inline-flex;align-items:center;gap:", "the label is a centred flex row with a fixed gap");
        StringAssert.Contains(css, "@media (pointer:coarse){", "bigger targets on touch screens");
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(css, "#[0-9a-fA-F]{3,8}\b"), "colours come from the theme variables, light and dark follow");
        Assert.AreEqual(css, FormControls.Css(".ev", ".ae"), "pure");
        // the pages use it, scoped to their own wrapper
        Assert.IsFalse(css.Contains((char)10 + " input[type"), "no unscoped selector");
    }

    [TestMethod]
    public async Task TheEditingPagesCarryTheFormControlsAndTheOthersDoNot()
    {
        var (server, client, _) = await Start(new WebUiOptions { Enabled = true, Port = 0 });
        await using (server) using (client)
        {
            // The rules are one file, scoped to the page's own wrapper class by a query: each editing page links its own scope.
            StringAssert.Contains(await client.GetStringAsync("/areas"), "href=\"assets/form-controls.css?scope=ae\"");
            StringAssert.Contains(await client.GetStringAsync("/events"), "href=\"assets/form-controls.css?scope=ev\"");
            var areas = await client.GetStringAsync("/assets/form-controls.css?scope=ae");
            StringAssert.Contains(areas, ".ae input[type=checkbox]");
            Assert.IsFalse(areas.Contains(".ev input[type=checkbox]"));
            var events = await client.GetStringAsync("/assets/form-controls.css?scope=ev");
            StringAssert.Contains(events, ".ev input[type=checkbox]");
            Assert.IsFalse(events.Contains(".ae input[type=checkbox]"));
            Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync("/assets/form-controls.css?scope=x")).StatusCode, "only the known scopes");
            Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync("/assets/form-controls.css")).StatusCode);
            var home = await client.GetStringAsync("/mods");
            Assert.IsFalse(home.Contains("form-controls.css"), "the other pages do not link the form controls");
        }
    }
}
