using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sacred.GameServer.Web;

/// <summary>
/// The management UI on Kestrel with minimal API endpoints (<c>MapGet</c>/<c>MapPost</c>, no controllers):
/// <list type="bullet">
/// <item><c>GET /</c> server rendered page (<see cref="Pages"/>), forms post to <c>/actions/*</c> and redirect back,</item>
/// <item><c>GET /fragment</c> the server rendered state block the page swaps in every few seconds,</item>
/// <item><c>GET /api/chat/stream</c> chat as Server-Sent Events, <c>/api/*</c> JSON for scripts.</item>
/// </list>
/// Endpoints are plain <see cref="RequestDelegate"/>s (no reflection or model binding), JSON is source generated: trim and AOT clean.
/// </summary>
public sealed class WebUiServer : IAsyncDisposable
{
    private const int MaxText = 200;
    private readonly WebUiOptions _options;
    private readonly IServerAdmin _admin;
    private readonly Action<string> _log;
    private readonly byte[] _passwordHash;
    private WebApplication? _app;
    private CancellationToken _stopping;

    public WebUiServer(WebUiOptions options, IServerAdmin admin, Action<string> log)
    {
        _options = options; _admin = admin; _log = log;
        _passwordHash = options.Password is null ? [] : SHA256.HashData(Encoding.UTF8.GetBytes(options.Password));
    }

    /// <summary>The ports Kestrel really bound (useful with Port 0 in tests).</summary>
    public IReadOnlyList<int> Ports { get; private set; } = [];

    /// <summary>Why the UI must not start with these options, or null.</summary>
    public static string? Validate(WebUiOptions options)
    {
        if (options.Password is null && !IsLoopbackOnly(options.Hostname))
            return $"WebUi:Password is required when WebUi:Hostname ('{options.Hostname}') is not a loopback address.";
        return null;
    }

    private static bool IsLoopbackOnly(string hostname) =>
        hostname.Equals("localhost", StringComparison.OrdinalIgnoreCase) || (IPAddress.TryParse(hostname, out var ip) && IPAddress.IsLoopback(ip));

    public async Task StartAsync()
    {
        if (Validate(_options) is { } problem) throw new InvalidOperationException(problem);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = ModStoreBodyLimit;   // the commands and forms lower it to 16 KB (LimitBody); only the mod storage endpoint takes more
            kestrel.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
            kestrel.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            foreach (var address in ResolveBindAddresses(_options.Hostname))
                kestrel.Listen(address, _options.Port, listen => listen.Protocols = HttpProtocols.Http1AndHttp2);
        });
        builder.Services.Configure<Microsoft.Extensions.Hosting.HostOptions>(o => o.ShutdownTimeout = TimeSpan.Zero);   // never wait for browsers
        var app = builder.Build();
        if (_options.PathBase.Length > 0) app.UsePathBase(_options.PathBase);
        app.Use(Gate);
        MapEndpoints(app);
        await app.StartAsync().ConfigureAwait(false);
        _app = app;
        _stopping = app.Lifetime.ApplicationStopping;
        _admin.StopRequested.Register(() => { try { app.Lifetime.StopApplication(); } catch (ObjectDisposedException) { } });
        var ports = new List<int>();
        foreach (var address in app.Urls) if (Uri.TryCreate(address, UriKind.Absolute, out var uri)) ports.Add(uri.Port);
        Ports = ports;
        _log($"Web UI on http://{_options.Hostname}:{(ports.Count > 0 ? ports[0] : _options.Port)}{_options.PathBase}/ ({(_options.Password is null ? "no password, loopback only" : "password protected")})");
    }

    private static IEnumerable<IPAddress> ResolveBindAddresses(string hostname)
    {
        if (hostname is "*" or "+" or "0.0.0.0" or "::") return [hostname == "0.0.0.0" ? IPAddress.Any : IPAddress.IPv6Any];
        if (hostname.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return [IPAddress.Loopback, IPAddress.IPv6Loopback];
        if (IPAddress.TryParse(hostname, out var ip)) return [ip];
        return Dns.GetHostAddresses(hostname);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is null) return;
        using var limit = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        try { await _app.StopAsync(limit.Token).ConfigureAwait(false); } catch (OperationCanceledException) { }
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    // ---- endpoints ----------------------------------------------------------------------------------------------------

    private void MapEndpoints(WebApplication app)
    {
        app.MapGet("/", (RequestDelegate)PageEndpoint);
        app.MapGet("/fragment", (RequestDelegate)FragmentEndpoint);
        app.MapGet("/assets/{name}", (RequestDelegate)AssetEndpoint);
        app.MapGet("/api/overview", (RequestDelegate)(c => Json(c.Response, _admin.Overview(), WebUiJson.Default.ServerOverview)));
        app.MapGet("/api/players", (RequestDelegate)(c => Json(c.Response, _admin.Players().ToArray(), WebUiJson.Default.PlayerInfoArray)));
        app.MapGet("/api/bans", (RequestDelegate)(c => Json(c.Response, _admin.Bans().ToArray(), WebUiJson.Default.BanEntryArray)));
        app.MapGet("/api/flood", (RequestDelegate)(c => Json(c.Response, _admin.Flood(), WebUiJson.Default.FloodSettings)));
        app.MapGet("/api/heroes", (RequestDelegate)(c => Json(c.Response, _admin.Heroes().ToArray(), WebUiJson.Default.HeroInfoArray)));
        app.MapGet("/api/heroes/{slot:int}", (RequestDelegate)(c => HeroEndpoint(c, publicView: false)));
        app.MapGet("/mods", (RequestDelegate)(c => Html(c.Response, Pages.ModsPage(_admin, _options.PublicApi, c.Request.Query["notice"].ToString() is { Length: > 0 and <= 200 } n ? n : null))));
        app.MapGet("/areas", (RequestDelegate)(c => Html(c.Response, Pages.AreaEditorPage(_admin))));
        app.MapGet("/events", (RequestDelegate)(c => Html(c.Response, Pages.EventsPage(_admin))));
        app.MapGet("/auction", (RequestDelegate)(c => Html(c.Response, Pages.AuctionPage(_admin))));
        app.MapGet("/api/areas/map", (RequestDelegate)(c => AreaMapEndpoint(c, image: false)));
        app.MapGet("/api/areas/map.png", (RequestDelegate)(c => AreaMapEndpoint(c, image: true)));
        app.MapGet("/api/mod-store", (RequestDelegate)ModStoreRead);
        app.MapPost("/api/mod-store", (RequestDelegate)ModStoreWrite);
        app.MapGet("/api/mods", (RequestDelegate)(c => Json(c.Response, _admin.ModEndpoints().ToArray(), WebUiJson.Default.ModEndpointInfoArray)));
        app.MapGet("/api/mods/installed", (RequestDelegate)(c => Json(c.Response, _admin.Mods().ToArray(), WebUiJson.Default.ModInfoArray)));
        app.MapGet("/mods/{mod}/{**path}", (RequestDelegate)(c => ModEndpoint(c, publicOnly: false)));
        if (_options.PublicApi)
        {
            app.MapGet("/public/heroes", (RequestDelegate)(c => Json(c.Response, _admin.Heroes().Select(Public).ToArray(), WebUiJson.Default.HeroInfoArray)));
            app.MapGet("/public/heroes/{name}", (RequestDelegate)(c => HeroEndpoint(c, publicView: true)));
            app.MapGet("/public/mods", (RequestDelegate)(c => Json(c.Response, _admin.ModEndpoints().Where(e => e.Public).ToArray(), WebUiJson.Default.ModEndpointInfoArray)));
            app.MapGet("/public/mods/{mod}/{**path}", (RequestDelegate)(c => ModEndpoint(c, publicOnly: true)));
        }
        app.MapGet("/api/chat", (RequestDelegate)ChatPageEndpoint);
        app.MapGet("/api/chat/stream", (RequestDelegate)ChatStreamEndpoint);
        app.MapPost("/api/{command}", (RequestDelegate)ApiCommandEndpoint);
        app.MapPost("/actions/{command}", (RequestDelegate)FormActionEndpoint);
    }

    private async Task PageEndpoint(HttpContext context)
    {
        var notice = context.Request.Query["notice"].ToString();
        var html = Pages.Index(_admin, string.IsNullOrEmpty(notice) ? null : notice);
        await Html(context.Response, html).ConfigureAwait(false);
    }

    private Task FragmentEndpoint(HttpContext context) => Html(context.Response, Pages.State(_admin));

    /// <summary>The public view never shows the account part of a lobby name nor the slot.</summary>
    private static HeroInfo Public(HeroInfo hero) => hero with { Account = null, Slot = 0 };

    /// <summary><c>/api/heroes/{slot}</c> (admin) or <c>/public/heroes/{character name}</c>.</summary>
    private Task HeroEndpoint(HttpContext context, bool publicView)
    {
        var heroes = _admin.Heroes();
        HeroInfo? hero;
        if (publicView)
        {
            var name = (string?)context.GetRouteValue("name") ?? "";
            hero = heroes.FirstOrDefault(h => h.Character.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (hero is not null) hero = Public(hero);
        }
        else
        {
            var slot = Convert.ToInt32(context.GetRouteValue("slot"), System.Globalization.CultureInfo.InvariantCulture);
            hero = heroes.FirstOrDefault(h => h.Slot == slot);
        }
        return hero is null ? Fail(context.Response, 404, "no such hero online") : Json(context.Response, hero, WebUiJson.Default.HeroInfo);
    }

    /// <summary><c>/api/areas/map</c> (sectors and projection as JSON) and <c>/api/areas/map.png</c>: the area editor's data from the game files.</summary>
    private async Task AreaMapEndpoint(HttpContext context, bool image)
    {
        BinaryResponse answer;
        try { answer = await _admin.AreaMapRequest(image, context.RequestAborted).ConfigureAwait(false); }
        catch (TimeoutException) { await Fail(context.Response, 504, "the map is still being read from the game files, try again").ConfigureAwait(false); return; }
        catch (OperationCanceledException) { return; }
        if (answer.Status != 200) { await Fail(context.Response, answer.Status, System.Text.Encoding.UTF8.GetString(answer.Body)).ConfigureAwait(false); return; }
        context.Response.StatusCode = 200;
        context.Response.ContentType = answer.ContentType;
        context.Response.Headers.CacheControl = image ? "private, max-age=3600" : "no-store";
        await context.Response.Body.WriteAsync(answer.Body, context.RequestAborted).ConfigureAwait(false);
    }

    private const int ModStoreBodyLimit = 512 * 1024;

    /// <summary><c>GET /api/mod-store?mod=world_events&amp;key=areas</c>: one key of a mod's storage as JSON (admin).</summary>
    private async Task ModStoreRead(HttpContext context)
    {
        var mod = context.Request.Query["mod"].ToString(); var key = context.Request.Query["key"].ToString();
        if (mod.Length == 0 || key.Length == 0) { await Fail(context.Response, 400, "mod and key are required").ConfigureAwait(false); return; }
        await ModStoreAnswer(context, mod, key, null).ConfigureAwait(false);
    }

    /// <summary><c>POST /api/mod-store</c> with <c>{"mod":"world_events","set":{"areas":{...}},"delete":["old"]}</c>: writes keys of a mod's
    /// storage (admin; JSON bodies only, like the other commands; at most 512 KB). Each key written also bumps <c>{key}_rev</c> so a script
    /// can notice cheaply.</summary>
    private async Task ModStoreWrite(HttpContext context)
    {
        var request = context.Request;
        if (request.Headers["Sec-Fetch-Site"].ToString() == "cross-site") { await Deny(context.Response, "Request refused: posted from another site."); return; }
        if (request.ContentType is null || !request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        { context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType; return; }
        if (request.ContentLength is > ModStoreBodyLimit) { await Fail(context.Response, 413, "too large").ConfigureAwait(false); return; }
        var limit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = ModStoreBodyLimit;
        string text;
        try
        {
            using var reader = new StreamReader(request.Body, System.Text.Encoding.UTF8);
            var buffer = new char[ModStoreBodyLimit + 1];
            var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            if (read > ModStoreBodyLimit) { await Fail(context.Response, 413, "too large").ConfigureAwait(false); return; }
            text = new string(buffer, 0, read);
        }
        catch (Microsoft.AspNetCore.Http.BadHttpRequestException) { await Fail(context.Response, 413, "too large").ConfigureAwait(false); return; }
        string mod;
        try { using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 }); mod = doc.RootElement.TryGetProperty("mod", out var m) ? m.GetString() ?? "" : ""; }
        catch (JsonException) { await Fail(context.Response, 400, "invalid JSON").ConfigureAwait(false); return; }
        if (mod.Length == 0) { await Fail(context.Response, 400, "mod is required").ConfigureAwait(false); return; }
        await ModStoreAnswer(context, mod, null, text).ConfigureAwait(false);
    }

    private async Task ModStoreAnswer(HttpContext context, string mod, string? key, string? body)
    {
        ModResponse answer;
        try { answer = await _admin.ModStoreRequest(mod, key, body, context.RequestAborted).ConfigureAwait(false); }
        catch (TimeoutException) { await Fail(context.Response, 504, "the game loop did not answer in time").ConfigureAwait(false); return; }
        catch (OperationCanceledException) { return; }
        if (answer.Status != 200) { await Fail(context.Response, answer.Status, answer.Body).ConfigureAwait(false); return; }
        context.Response.StatusCode = 200;
        context.Response.ContentType = answer.ContentType;
        await context.Response.WriteAsync(answer.Body, context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary><c>/mods/{mod}/{path}</c>: a script endpoint, answered by its Lua handler on the game loop.</summary>
    private async Task ModEndpoint(HttpContext context, bool publicOnly)
    {
        var mod = (string?)context.GetRouteValue("mod") ?? "";
        var path = (string?)context.GetRouteValue("path") ?? "";
        var query = context.Request.Query.Select(q => KeyValuePair.Create(q.Key, q.Value.ToString())).Take(64).ToArray();
        ModResponse answer;
        try { answer = await _admin.ModRequest(mod, path, query, publicOnly, context.RequestAborted).ConfigureAwait(false); }
        catch (TimeoutException) { answer = new ModResponse(504, "text/plain; charset=utf-8", "the script did not answer in time"); }
        catch (OperationCanceledException) { return; }
        context.Response.StatusCode = answer.Status;
        context.Response.ContentType = answer.ContentType;
        // Script output is data, not part of this site. Endpoints: nothing runs. Pages: their own inline scripts and styles run, but in
        // a sandbox (opaque origin), so they cannot use the admin's credentials for this site's API; no forms, no base, no plugins;
        // they may fetch (connect-src 'self', public endpoints answer CORS *) and show images.
        context.Response.Headers.ContentSecurityPolicy = (answer.Page
                ? "sandbox allow-scripts; default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data: https:; connect-src 'self'; form-action 'none'; base-uri 'none'"
                : "default-src 'none'; style-src 'unsafe-inline'; img-src data: https:")
            + "; frame-ancestors " + (publicOnly ? "*" : FrameAncestors());
        if (publicOnly) context.Response.Headers.Remove("X-Frame-Options");          // public data may be embedded anywhere
        await context.Response.WriteAsync(answer.Body, context.RequestAborted).ConfigureAwait(false);
    }

    private async Task ChatPageEndpoint(HttpContext context)
    {
        var head = _admin.ChatHead;
        var after = long.TryParse(context.Request.Query["after"], out var a) && a >= 0 ? a : Math.Max(0, head - 100);
        var entries = _admin.ChatSince(after, 200);
        await Json(context.Response, new ChatPage(entries.Count > 0 ? entries[^1].Id : Math.Max(after, head), entries.ToArray()), WebUiJson.Default.ChatPage).ConfigureAwait(false);
    }

    /// <summary>Server-Sent Events: one <c>chat</c> event per line, <c>id</c> = chat id so EventSource resumes with Last-Event-ID.</summary>
    private async Task ChatStreamEndpoint(HttpContext context)
    {
        var response = context.Response;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";
        var last = long.TryParse(context.Request.Headers["Last-Event-ID"], out var resume) ? resume
            : long.TryParse(context.Request.Query["after"], out var after) && after >= 0 ? after : Math.Max(0, _admin.ChatHead - 50);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, _stopping, _admin.StopRequested);
        var cancel = linked.Token;
        var idle = 0;
        try
        {
            await response.WriteAsync("retry: 3000\n\n", cancel).ConfigureAwait(false);
            await response.Body.FlushAsync(cancel).ConfigureAwait(false);
            while (!cancel.IsCancellationRequested)
            {
                var entries = _admin.ChatSince(last, 100);
                if (entries.Count == 0)
                {
                    await Task.Delay(200, cancel).ConfigureAwait(false);
                    if (++idle < 75) continue;               // ~15 s: keep proxies from closing an idle stream
                    idle = 0;
                    await response.WriteAsync(": keep-alive\n\n", cancel).ConfigureAwait(false);
                    await response.Body.FlushAsync(cancel).ConfigureAwait(false);
                    continue;
                }
                idle = 0;
                foreach (var entry in entries)
                {
                    var data = JsonSerializer.Serialize(entry, WebUiJson.Default.ChatEntry);
                    await response.WriteAsync($"id: {entry.Id}\nevent: chat\ndata: {data}\n\n", cancel).ConfigureAwait(false);
                    last = entry.Id;
                }
                await response.Body.FlushAsync(cancel).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>The usual limit of a command or a form: 16 KB (Kestrel's own limit is higher for the mod storage endpoint).</summary>
    private static void LimitBody(HttpContext context)
    {
        var limit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = 16 * 1024;
    }

    private async Task ApiCommandEndpoint(HttpContext context)
    {
        LimitBody(context);
        var response = context.Response;
        // Only JSON bodies: a cross-site form post cannot send this content type without a CORS preflight.
        if (context.Request.ContentType is null || !context.Request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        { response.StatusCode = StatusCodes.Status415UnsupportedMediaType; return; }
        CommandRequest body;
        try { body = await JsonSerializer.DeserializeAsync(context.Request.Body, WebUiJson.Default.CommandRequest, context.RequestAborted).ConfigureAwait(false) ?? new CommandRequest(); }
        catch (JsonException) { await Fail(response, 400, "invalid JSON").ConfigureAwait(false); return; }
        var (status, error) = Run((string)context.GetRouteValue("command")!, body);
        if (error is not null) await Fail(response, status, error).ConfigureAwait(false);
        else await Json(response, new CommandResult(true, null), WebUiJson.Default.CommandResult).ConfigureAwait(false);
    }

    /// <summary>The HTML forms: urlencoded body, answered with a redirect back to the page (Post/Redirect/Get).</summary>
    private async Task FormActionEndpoint(HttpContext context)
    {
        LimitBody(context);
        var request = context.Request; var response = context.Response;
        // Cookie-less Basic auth is attached by the browser to cross-site posts too: refuse anything a foreign page started.
        if (request.Headers["Sec-Fetch-Site"].ToString() == "cross-site") { await Deny(response, "Request refused: the form was posted from another site."); return; }
        if (request.ContentType is null || !request.ContentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        { response.StatusCode = StatusCodes.Status415UnsupportedMediaType; return; }
        var form = await request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
        var name = (string)context.GetRouteValue("command")!;
        if (name == "settings") { var (saved, problem) = Settings(form); await Respond(context, saved, problem).ConfigureAwait(false); return; }
        if (name == "flood-form") { var (saved, problem) = FloodForm(form); await Respond(context, saved, problem).ConfigureAwait(false); return; }
        var body = new CommandRequest(Int(form["slot"]), Int(form["value"]), form["text"], form["reason"], form["address"]);
        // The chat box posts "to" (0 = everybody) and the mode form "mode"/"level".
        if (name == "say") { name = Int(form["to"]) is var to and > 0 ? "message" : "broadcast"; body = body with { Slot = Int(form["to"]) }; }
        if (name == "mode") body = body with { Value = Int(form["mode"]), Slot = Int(form["level"]) };
        var (_, error) = Run(name, body);
        await Respond(context, error is null ? "Done." : null, error, form["back"] == "mods" ? "/mods" : "/").ConfigureAwait(false);
    }

    /// <summary>
    /// The one settings form (name, max players, password, mode and level): only values that differ from the server's current
    /// state become commands, so saving never re-sends (or resets) what the admin did not touch.
    /// </summary>
    private (string? Notice, string? Error) Settings(IFormCollection form)
    {
        var current = _admin.Overview();
        var commands = new List<string>();
        void Send(string name, CommandRequest body, string what, out string? error)
        {
            var (_, e) = Run(name, body);
            error = e;
            if (e is null) commands.Add(what);
        }
        string? failure = null;
        var text = Clean(form["text"]);
        if (text is not null && text != current.Name) { Send("name", new CommandRequest(Text: text), "name", out var e); failure ??= e; }
        var max = Int(form["max"]);
        if (max != 0 && max != current.MaxPlayers) { Send("max-players", new CommandRequest(Value: max), "max players", out var e); failure ??= e; }
        var password = form["password"].ToString();
        if (password.Length > 0) { Send("password", new CommandRequest(Text: password), "password", out var e); failure ??= e; }
        else if (form["clearPassword"] == "1" && current.PasswordSet) { Send("password", new CommandRequest(Text: ""), "password removed", out var e); failure ??= e; }
        var mode = Int(form["mode"]); var level = Int(form["level"]);
        if ((mode != current.Mode || level != current.Level) && !current.WorldAvailable) { Send("mode", new CommandRequest(Value: mode, Slot: level), "game mode", out var e); failure ??= e; }
        if (failure is not null) return (null, failure);
        return (commands.Count == 0 ? "Nothing to change." : "Saved: " + string.Join(", ", commands) + ".", null);
    }

    /// <summary>The flood protection form: only changed limits are sent (one SetFlood command), applied live by the game loop.</summary>
    private (string? Notice, string? Error) FloodForm(IFormCollection form)
    {
        var current = _admin.Flood();
        var pairs = new List<string>();
        var changed = new List<string>();
        void Field(string field, string key, int value, string label, int scale = 1)
        {
            var text = form[field].ToString();
            if (text.Length == 0 || !int.TryParse(text, out var n) || n < 0 || n > int.MaxValue / scale) return;
            n *= scale;
            if (n == value) return;
            pairs.Add($"{key}={n}"); changed.Add(label);
        }
        var enabled = form["enabled"].Contains("1");
        if (enabled != current.Enabled) { pairs.Add($"FLOOD_PROTECTION={(enabled ? 1 : 0)}"); changed.Add(enabled ? "turned on" : "turned off"); }
        Field("msg", "FLOOD_MESSAGES_PER_SECOND", current.MessagesPerSecond, "messages/s");
        Field("msgBurst", "FLOOD_MESSAGE_BURST", current.MessageBurst, "message burst");
        Field("kbps", "FLOOD_BYTES_PER_SECOND", current.BytesPerSecond, "data rate", 1024);
        Field("kbBurst", "FLOOD_BYTE_BURST", current.ByteBurst, "data burst", 1024);
        Field("chat", "FLOOD_CHAT_PER_SECOND", current.ChatPerSecond, "chat/s");
        Field("chatBurst", "FLOOD_CHAT_BURST", current.ChatBurst, "chat burst");
        Field("kick", "FLOOD_KICK_AFTER_DROPS", current.KickAfterDrops, "kick threshold");
        Field("window", "FLOOD_STRIKE_WINDOW", current.StrikeWindowSeconds, "window");
        Field("grace", "FLOOD_UPLOAD_GRACE", current.UploadGraceSeconds, "upload grace");
        Field("perAddress", "MAX_CONNECTIONS_PER_ADDRESS", current.MaxConnectionsPerAddress, "connections per address");
        if (pairs.Count == 0) return ("Nothing to change.", null);
        var (_, error) = Run("flood", new CommandRequest(Text: string.Join(';', pairs)));
        return error is null ? ("Flood protection: " + string.Join(", ", changed) + " (applied live).", null) : (null, error);
    }

    private async Task Respond(HttpContext context, string? notice, string? error = null, string page = "/")
    {
        var request = context.Request; var response = context.Response;
        notice = error ?? notice ?? "Done.";
        var wantsJson = request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
        if (wantsJson) { response.StatusCode = error is null ? 200 : 400; await Json(response, new CommandResult(error is null, error), WebUiJson.Default.CommandResult).ConfigureAwait(false); return; }
        response.StatusCode = StatusCodes.Status303SeeOther;
        response.Headers.Location = $"{_options.PathBase}{page}?notice={Uri.EscapeDataString(notice)}";
    }

    private static int Int(string? value) => int.TryParse(value, out var n) ? n : 0;

    private (int Status, string? Error) Run(string name, CommandRequest body)
    {
        var (command, error) = Build(name, body);
        if (error is not null) return (name is "" ? 404 : 400, error);
        if (!_admin.Submit(command)) return (503, "command queue full");
        return (200, null);
    }

    // ---- gate: headers, host, CORS, auth --------------------------------------------------------------------------------

    private async Task Gate(HttpContext context, Func<Task> next)
    {
        var request = context.Request; var response = context.Response;
        SetSecurityHeaders(response);
        if (_options.PathBase.Length > 0 && !request.PathBase.HasValue) { response.StatusCode = StatusCodes.Status404NotFound; return; }
        // The public read-only API: anybody may read it, from any page (embeds, overlays); GET only, no credentials.
        if (_options.PublicApi && request.Path.StartsWithSegments("/public"))
        {
            response.Headers.AccessControlAllowOrigin = "*";
            response.Headers.CacheControl = "public, max-age=2";
            if (HttpMethods.IsOptions(request.Method)) { response.Headers.AccessControlAllowMethods = "GET"; response.StatusCode = StatusCodes.Status204NoContent; return; }
            if (!HttpMethods.IsGet(request.Method)) { response.StatusCode = StatusCodes.Status405MethodNotAllowed; return; }
            await next().ConfigureAwait(false);
            return;
        }
        if (!HostAllowed(request.Host.Host)) { response.StatusCode = StatusCodes.Status400BadRequest; return; }
        var origin = request.Headers.Origin.ToString();
        var corsAllowed = origin.Length > 0 && OriginAllowed(origin);
        if (corsAllowed)
        {
            response.Headers.AccessControlAllowOrigin = origin;
            response.Headers.Vary = "Origin";
            response.Headers.AccessControlAllowHeaders = "Authorization, Content-Type";
            response.Headers.AccessControlAllowMethods = "GET, POST, OPTIONS";
            response.Headers.AccessControlMaxAge = "600";
        }
        if (HttpMethods.IsOptions(request.Method)) { response.StatusCode = corsAllowed ? StatusCodes.Status204NoContent : StatusCodes.Status403Forbidden; return; }
        // A foreign origin that is not allowed must not drive the API with the browser's cached credentials.
        if (origin.Length > 0 && !corsAllowed && !SameOrigin(origin, request))
        { await Deny(response, $"Request refused: it comes from the origin '{origin}', but this page is served as '{request.Host}'. Open the UI under one address (or list the origin in WebUi:CorsOrigins)."); return; }
        if (!Authorized(request))
        {
            response.StatusCode = StatusCodes.Status401Unauthorized;
            response.Headers.WWWAuthenticate = "Basic realm=\"Sacred GameServer\", charset=\"UTF-8\"";
            return;
        }
        await next().ConfigureAwait(false);
    }

    private void SetSecurityHeaders(HttpResponse response)
    {
        var headers = response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "same-origin";   // not no-referrer: Chromium then sends "Origin: null" on same-origin form posts
        headers.CacheControl = "no-store";
        var ancestors = FrameAncestors();
        headers.ContentSecurityPolicy = $"default-src 'self'; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; connect-src 'self'; img-src 'self' data:; frame-ancestors {ancestors}; base-uri 'none'; form-action 'self'";
        if (ancestors == "'none'") headers.XFrameOptions = "DENY";
        else if (ancestors == "'self'") headers.XFrameOptions = "SAMEORIGIN";
    }

    private string FrameAncestors()
    {
        if (_options.FrameAncestors.Length == 0 || _options.FrameAncestors.Any(a => a.Equals("none", StringComparison.OrdinalIgnoreCase))) return "'none'";
        return string.Join(' ', _options.FrameAncestors.Select(a => a.Equals("self", StringComparison.OrdinalIgnoreCase) ? "'self'" : a));
    }

    private bool HostAllowed(string host)
    {
        if (_options.AllowedHosts.Length > 0)
            return _options.AllowedHosts.Any(a => a == "*" || a.Equals(host, StringComparison.OrdinalIgnoreCase));
        if (_options.Password is not null || !IsLoopbackOnly(_options.Hostname)) return true;
        // Loopback without a password: refuse foreign Host headers (DNS rebinding from a web page).
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host == "127.0.0.1" || host == "[::1]" || host == "::1" ||
               host.Equals(_options.Hostname, StringComparison.OrdinalIgnoreCase);
    }

    private bool OriginAllowed(string origin) =>
        _options.CorsOrigins.Any(o => o == "*" || o.TrimEnd('/').Equals(origin, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The browser's own verdict (<c>Sec-Fetch-Site</c>) wins when it is there; otherwise the Origin must name this server. Behind a
    /// reverse proxy the original host is accepted from <c>X-Forwarded-Host</c> (a foreign page cannot set that header).
    /// </summary>
    private static bool SameOrigin(string origin, HttpRequest request)
    {
        var site = request.Headers["Sec-Fetch-Site"].ToString();
        if (site is "same-origin" or "none") return true;
        if (site is "cross-site" or "same-site") return false;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        var forwarded = request.Headers["X-Forwarded-Host"].ToString().Split(',')[0].Trim();
        return uri.Authority.Equals(request.Host.Value, StringComparison.OrdinalIgnoreCase) ||
               (forwarded.Length > 0 && uri.Authority.Equals(forwarded, StringComparison.OrdinalIgnoreCase));
    }

    private static Task Deny(HttpResponse response, string message)
    {
        response.StatusCode = StatusCodes.Status403Forbidden;
        response.ContentType = "text/plain; charset=utf-8";
        return response.WriteAsync(message);
    }

    private bool Authorized(HttpRequest request)
    {
        if (_options.Password is null) return true;
        var header = request.Headers.Authorization.ToString();
        string? candidate = null;
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) candidate = header[7..].Trim();
        else if (header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..].Trim()));
                var colon = decoded.IndexOf(':');
                candidate = colon < 0 ? decoded : decoded[(colon + 1)..];
            }
            catch (FormatException) { return false; }
        }
        if (candidate is null) return false;
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(candidate)), _passwordHash);
    }

    // ---- commands -----------------------------------------------------------------------------------------------------

    private static (AdminCommand Command, string? Error) Build(string name, CommandRequest b)
    {
        string? text = Clean(b.Text);
        switch (name)
        {
            case "broadcast": return text is null ? (default, "text required") : (new(AdminCommandKind.Broadcast, Text: text), null);
            case "message":
                if (!Slot(b.Slot)) return (default, "slot 1..16 required");
                return text is null ? (default, "text required") : (new(AdminCommandKind.Message, b.Slot, Text: text), null);
            case "kick": return Slot(b.Slot) ? (new(AdminCommandKind.Kick, b.Slot, b.Value), null) : (default, "slot 1..16 required");
            case "ban":
                if (Slot(b.Slot)) return (new(AdminCommandKind.Ban, b.Slot, Text: Clean(b.Reason) ?? ""), null);
                if (b.Address is { Length: > 0 and <= 64 } address && IPAddress.TryParse(address.Trim(), out var ip))
                    return (new(AdminCommandKind.BanAddress, Text: ip.ToString() + "|" + (Clean(b.Reason) ?? "")), null);
                return (default, "slot or a valid address required");
            case "unban":
                return b.Address is { Length: > 0 and <= 64 } lifted && IPAddress.TryParse(lifted.Trim(), out var parsed)
                    ? (new(AdminCommandKind.Unban, Text: parsed.ToString()), null) : (default, "valid address required");
            case "lock": return (new(AdminCommandKind.Lock, Value: b.Value != 0 ? 1 : 0), null);
            case "pause": return (new(AdminCommandKind.Pause, Value: b.Value != 0 ? 1 : 0), null);
            case "max-players": return b.Value is >= 1 and <= 16 ? (new(AdminCommandKind.SetMaxPlayers, Value: b.Value), null) : (default, "value 1..16 required");
            case "password": return b.Text is { Length: > 32 } ? (default, "password too long") : (new(AdminCommandKind.SetPassword, Text: b.Text), null);
            case "name": return text is null ? (default, "text required") : (new(AdminCommandKind.SetName, Text: text), null);
            case "mode":
                return b.Value is >= 0 and <= 4 && b.Slot is >= 0 and <= 5 ? (new(AdminCommandKind.SetMode, b.Slot, b.Value), null) : (default, "value 0..4 and slot (level) 0..5 required");
            case "end-session": return (new(AdminCommandKind.EndSession), null);
            case "shutdown": return (new(AdminCommandKind.Shutdown), null);
            case "flood": return FloodCommand(b.Text);
            case "reload-mods":
                return b.Text is null or "" || ModNameOk.IsMatch(b.Text) ? (new(AdminCommandKind.ReloadMods, Text: b.Text ?? ""), null) : (default, "a mod name (a-z 0-9 _ . -) or nothing for all mods");
        }
        return (default, "unknown command");
    }

    private static bool Slot(int slot) => slot is >= 1 and <= 16;

    private static readonly System.Text.RegularExpressions.Regex ModNameOk = new("^[A-Za-z0-9_.-]{1,64}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Validates <c>KEY=VALUE</c> pairs for SetFlood: known keys only, whole numbers &gt;= 0.</summary>
    private static (AdminCommand, string?) FloodCommand(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 1024) return (default, "text with KEY=VALUE pairs required");
        foreach (var pair in text.Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var equals = pair.IndexOf('=');
            var key = equals > 0 ? pair[..equals].Trim().Replace('-', '_').ToUpperInvariant() : "";
            if (!FloodKeys.Contains(key)) return (default, $"unknown flood setting {(equals > 0 ? pair[..equals] : pair)}");
            if (!int.TryParse(pair[(equals + 1)..].Trim(), out var value) || value < 0) return (default, $"{key} needs a whole number >= 0");
        }
        return (new(AdminCommandKind.SetFlood, Text: text), null);
    }

    private static readonly HashSet<string> FloodKeys = ["FLOOD_PROTECTION", "FLOOD_MESSAGES_PER_SECOND", "FLOOD_MESSAGE_BURST", "FLOOD_BYTES_PER_SECOND",
        "FLOOD_BYTE_BURST", "FLOOD_CHAT_PER_SECOND", "FLOOD_CHAT_BURST", "FLOOD_KICK_AFTER_DROPS", "FLOOD_STRIKE_WINDOW", "FLOOD_UPLOAD_GRACE",
        "MAX_CONNECTIONS_PER_ADDRESS"];

    /// <summary>Trims, drops control characters and limits the length; null when nothing is left.</summary>
    private static string? Clean(string? text)
    {
        if (text is null) return null;
        var builder = new StringBuilder(Math.Min(text.Length, MaxText));
        foreach (var c in text)
        {
            if (builder.Length >= MaxText) break;
            if (!char.IsControl(c)) builder.Append(c);
        }
        var result = builder.ToString().Trim();
        return result.Length == 0 ? null : result;
    }

    private static async Task Json<T>(HttpResponse response, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
    {
        response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(response.Body, value, info, response.HttpContext.RequestAborted).ConfigureAwait(false);
    }

    /// <summary><c>GET /assets/{name}</c>: the style sheets and scripts of the pages (embedded files, see <see cref="Assets"/>). Only those, by plain name;
    /// answered with an ETag so the browser revalidates instead of downloading again.</summary>
    private static async Task AssetEndpoint(HttpContext context)
    {
        var name = context.Request.RouteValues["name"] as string ?? "";
        if (!Assets.TryGetFile(name, out var file)) { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
        await ServeAsset(context, file).ConfigureAwait(false);
    }

    private static async Task ServeAsset(HttpContext context, Assets.File file)
    {
        var response = context.Response;
        response.Headers.CacheControl = "no-cache";
        response.Headers.ETag = file.ETag;
        if (context.Request.Headers.IfNoneMatch.ToString() == file.ETag) { response.StatusCode = StatusCodes.Status304NotModified; return; }
        response.ContentType = file.ContentType;
        response.ContentLength = file.Content.Length;
        await response.Body.WriteAsync(file.Content, context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task Html(HttpResponse response, string html)
    {
        response.ContentType = "text/html; charset=utf-8";
        await response.WriteAsync(html, response.HttpContext.RequestAborted).ConfigureAwait(false);
    }

    private static async Task Fail(HttpResponse response, int status, string message)
    {
        response.StatusCode = status;
        await Json(response, new CommandResult(false, message), WebUiJson.Default.CommandResult).ConfigureAwait(false);
    }
}
