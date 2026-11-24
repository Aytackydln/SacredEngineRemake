using System.Net;
using System.Text;

namespace Sacred.GameServer.Web;

/// <summary>
/// Server side rendering of the management page. Everything is self contained (inline CSS, script and SVG icons, system fonts):
/// no external request, so it works on a LAN without Internet. Dynamic values (player names and chat text come from the network)
/// are always HTML encoded. The blocks <c>#head</c>, <c>#players</c>, <c>#bans</c> and <c>#session</c> are what <c>/fragment</c>
/// returns and the page swaps in every few seconds; chat arrives through the SSE stream.
/// </summary>
internal static partial class Pages
{
    private static readonly string[] Modes = ["Default", "Free game", "Player killer", "Campaign", "Underworld campaign"];
    private static readonly string[] Levels = ["Any level", "Bronze", "Silver", "Gold", "Platinum", "Niobium"];

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");

    // 24x24 stroke icons (no icon font, no files).
    private static string Icon(string name) => "<svg class=\"i\" viewBox=\"0 0 24 24\" aria-hidden=\"true\">" + name switch
    {
        "users" => "<path d=\"M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM22 21v-2a4 4 0 0 0-3-3.9M16 3.1a4 4 0 0 1 0 7.8\"/>",
        "clock" => "<path d=\"M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20zM12 6v6l4 2\"/>",
        "sword" => "<path d=\"M14.5 17.5 3 6V3h3l11.5 11.5M13 19l6-6M16 16l4 4M19 21l2-2\"/>",
        "pulse" => "<path d=\"M22 12h-4l-3 9L9 3l-3 9H2\"/>",
        "lock" => "<path d=\"M5 11h14v10H5zM8 11V7a4 4 0 0 1 8 0v4\"/>",
        "unlock" => "<path d=\"M5 11h14v10H5zM8 11V7a4 4 0 0 1 7.5-2\"/>",
        "pause" => "<path d=\"M6 4h4v16H6zM14 4h4v16h-4z\"/>",
        "play" => "<path d=\"M6 4l14 8-14 8z\"/>",
        "chat" => "<path d=\"M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z\"/>",
        "kick" => "<path d=\"M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9\"/>",
        "ban" => "<path d=\"M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20zM4.9 4.9l14.2 14.2\"/>",
        "send" => "<path d=\"M22 2 11 13M22 2l-7 20-4-9-9-4z\"/>",
        "power" => "<path d=\"M18.4 6.6a9 9 0 1 1-12.8 0M12 2v10\"/>",
        "crown" => "<path d=\"M3 8l4 4 5-7 5 7 4-4-2 11H5z\"/>",
        "shield" => "<path d=\"M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z\"/>",
        _ => "",
    } + "</svg>";

    public static string Index(IServerAdmin admin, string? notice)
    {
        var o = admin.Overview();
        var players = admin.Players();
        var sb = new StringBuilder(24 * 1024);
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
          .Append("<meta name=\"color-scheme\" content=\"dark light\"><script>try{var m=localStorage.getItem('gsTheme');if(m==='light'||m==='dark')document.documentElement.dataset.theme=m;}catch(e){}</script><title>").Append(E(o.Name)).Append(" - Sacred GameServer</title><style>").Append(Style)
          .Append("</style></head><body><div class=\"app\">");
        sb.Append("<header class=\"top\"><div class=\"brand\"><span class=\"logo\">").Append(Icon("shield")).Append("</span><div><b>Sacred GameServer</b><small>management</small></div></div>");
        Tabs(sb, "server", admin.Mods().Count);
        Head(sb, o);
        sb.Append("<button id=\"theme\" type=\"button\" class=\"themebtn ghost\" aria-label=\"Change theme\"></button></header><main class=\"grid\"><div class=\"col\">");
        Players(sb, players);
        Session(sb, o);
        Flood(sb, admin.Flood());
        Bans(sb, admin.Bans());
        sb.Append("</div><section class=\"card chat\" id=\"chatCard\"><h2>").Append(Icon("chat")).Append("Live chat<span class=\"live\" id=\"live\" title=\"connection\">connecting</span></h2>")
          .Append("<div id=\"chat\" aria-live=\"polite\">");
        foreach (var entry in admin.ChatSince(Math.Max(0, admin.ChatHead - 50), 50)) ChatLine(sb, entry);
        sb.Append("</div><button id=\"latest\" class=\"ghost\" hidden>Jump to latest</button>")
          .Append("<form id=\"chatForm\" class=\"composer\" method=\"post\" action=\"actions/say\"><select name=\"to\" id=\"chatTo\" aria-label=\"Recipient\"><option value=\"0\">Everybody</option>");
        foreach (var p in players) sb.Append("<option value=\"").Append(p.Slot).Append("\">#").Append(p.Slot).Append(' ').Append(E(p.Name)).Append("</option>");
        sb.Append("</select><input name=\"text\" id=\"chatText\" type=\"text\" maxlength=\"120\" placeholder=\"Say something as the server...\" autocomplete=\"off\" required>")
          .Append("<button class=\"primary\" type=\"submit\">").Append(Icon("send")).Append("<span>Send</span></button></form></section></main></div>");
        sb.Append("<dialog id=\"ask\"><form method=\"dialog\"><h3 id=\"askTitle\"></h3><p id=\"askText\" class=\"dim\"></p><input id=\"askReason\" type=\"text\" maxlength=\"100\" placeholder=\"Reason (optional)\" hidden>")
          .Append("<div class=\"row end\"><button value=\"cancel\" class=\"ghost\">Cancel</button><button value=\"ok\" id=\"askOk\" class=\"primary\">Confirm</button></div></form></dialog>");
        if (notice is not null) sb.Append("<div id=\"toast\" role=\"status\">").Append(E(notice)).Append("</div>");
        sb.Append("<script>").Append(Script).Append("</script></body></html>");
        return sb.ToString();
    }

    /// <summary>The four blocks the page swaps while it stays open.</summary>
    public static string State(IServerAdmin admin)
    {
        var o = admin.Overview();
        var sb = new StringBuilder(8 * 1024);
        sb.Append("<header class=\"top\">"); Head(sb, o); sb.Append("</header>");
        Players(sb, admin.Players()); Session(sb, o); Flood(sb, admin.Flood()); Bans(sb, admin.Bans());
        return sb.ToString();
    }

    private static void Head(StringBuilder sb, ServerOverview o)
    {
        var slots = new StringBuilder();
        for (var i = 0; i < 16; i++) slots.Append("<i class=\"").Append(i < o.Players ? "on" : i < o.MaxPlayers ? "free" : "off").Append("\"></i>");
        sb.Append("<div id=\"head\" class=\"head\"><h1>").Append(E(o.Name)).Append("</h1><div class=\"stats\">")
          .Append("<div class=\"stat\">").Append(Icon("users")).Append("<div><b>").Append(o.Players).Append(" / ").Append(o.MaxPlayers).Append("</b><small>players</small></div><span class=\"slots\" aria-hidden=\"true\">").Append(slots).Append("</span></div>")
          .Append("<div class=\"stat\">").Append(Icon("clock")).Append("<div><b>").Append(Duration(o.UptimeSeconds)).Append("</b><small>uptime</small></div></div>")
          .Append("<div class=\"stat\">").Append(Icon("sword")).Append("<div><b>").Append(E(o.Mode >= 0 && o.Mode < Modes.Length ? Modes[o.Mode] : o.Mode.ToString())).Append("</b><small>")
          .Append(E(o.Level >= 0 && o.Level < Levels.Length ? Levels[o.Level] : "")).Append("</small></div></div></div><div class=\"pills\">");
        Pill(sb, o.Locked ? "Locked" : "Open", o.Locked ? "warn" : "ok"); Pill(sb, o.Paused ? "Paused" : "Running", o.Paused ? "warn" : "ok");
        Pill(sb, o.PasswordSet ? "Password" : "No password", o.PasswordSet ? "info" : "");
        Pill(sb, o.WorldAvailable ? "World running" : "In lobby", o.WorldAvailable ? "ok" : "");
        Pill(sb, !o.LobbyConfigured ? "No lobby" : o.LobbyOnline ? "Lobby online" : "Lobby offline", !o.LobbyConfigured ? "" : o.LobbyOnline ? "ok" : "bad");
        sb.Append("</div></div>");
    }

    private static void Players(StringBuilder sb, IReadOnlyList<PlayerInfo> players)
    {
        sb.Append("<section class=\"card\" id=\"players\"><h2>").Append(Icon("users")).Append("Players <span class=\"count\">").Append(players.Count).Append("</span></h2>");
        if (players.Count == 0) sb.Append("<div class=\"empty\">").Append(Icon("users")).Append("<p>Nobody is connected yet.</p><small>Players appear here as soon as they join.</small></div>");
        foreach (var p in players)
        {
            var ping = p.LatencyMs < 80 ? "ok" : p.LatencyMs < 200 ? "warn" : "bad";
            var state = p.InGame ? "In game" : p.Ready ? "Ready" : "In lobby";
            sb.Append("<div class=\"player\"><span class=\"avatar\" style=\"--h:").Append(p.Slot * 47 % 360).Append("\">").Append(E(Initial(p.Name))).Append("</span><div class=\"who\"><b>")
              .Append(E(p.Name)).Append("</b>");
            if (p.IsHost) sb.Append("<span class=\"badge\" title=\"Session host\">").Append(Icon("crown")).Append("host</span>");
            sb.Append("<small>#").Append(p.Slot).Append(" &middot; ").Append(E(p.Address)).Append(" &middot; ").Append(E(p.Transport)).Append(" &middot; ").Append(E(state)).Append("</small></div>")
              .Append("<span class=\"ping ").Append(ping).Append("\">").Append(p.LatencyMs).Append(" ms</span><div class=\"acts\">")
              .Append("<button type=\"button\" class=\"icon\" data-msg=\"").Append(p.Slot).Append("\" title=\"Message\" aria-label=\"Message ").Append(E(p.Name)).Append("\">").Append(Icon("chat")).Append("</button>");
            Form(sb, "kick", $"<input type=\"hidden\" name=\"slot\" value=\"{p.Slot}\"><button class=\"icon\" title=\"Kick\" aria-label=\"Kick {E(p.Name)}\">{Icon("kick")}</button>", "Kick " + p.Name + "?", "They can rejoin right away.", false, inline: true);
            Form(sb, "ban", $"<input type=\"hidden\" name=\"slot\" value=\"{p.Slot}\"><input type=\"hidden\" name=\"reason\"><button class=\"icon bad\" title=\"Ban\" aria-label=\"Ban {E(p.Name)}\">{Icon("ban")}</button>", "Ban " + p.Name + "?", "The address " + p.Address + " is refused until you lift the ban.", true, inline: true);
            sb.Append("</div></div>");
        }
        sb.Append("</section>");
    }

    private static void Session(StringBuilder sb, ServerOverview o)
    {
        sb.Append("<section class=\"card\" id=\"session\"><h2>").Append(Icon("pulse")).Append("Session</h2><div class=\"toggles\">");
        Tile(sb, "lock", o.Locked, "lock", "unlock", "Locked", "Open", "Click to let players join", "Click to stop new players");
        Tile(sb, "pause", o.Paused, "pause", "play", "Paused", "Running", "Click to continue", "Click to pause the game");
        sb.Append("</div>");
        sb.Append("<form method=\"post\" action=\"actions/settings\" class=\"settings\">");
        sb.Append($"<label class=\"f\"><span>Server name</span><input name=\"text\" type=\"text\" maxlength=\"60\" value=\"{E(o.Name)}\" required></label>");
        sb.Append($"<label class=\"f narrow\"><span>Max players</span><input name=\"max\" type=\"number\" min=\"1\" max=\"16\" value=\"{o.MaxPlayers}\"></label>");
        sb.Append($"<label class=\"f\"><span>Password</span><input name=\"password\" type=\"password\" maxlength=\"32\" placeholder=\"{(o.PasswordSet ? "unchanged - type to replace" : "none")}\" autocomplete=\"new-password\"></label>");
        if (o.PasswordSet) sb.Append("<label class=\"check\"><input type=\"checkbox\" name=\"clearPassword\" value=\"1\"> Remove the password</label>");
        sb.Append("<div class=\"f\"><span>Game mode</span><div class=\"ctl\"><select name=\"mode\" aria-label=\"Game mode\">");
        for (var i = 0; i < Modes.Length; i++) sb.Append($"<option value=\"{i}\"{(i == o.Mode ? " selected" : "")}>{Modes[i]}</option>");
        sb.Append("</select><select name=\"level\" aria-label=\"Level\">");
        for (var i = 0; i < Levels.Length; i++) sb.Append($"<option value=\"{i}\"{(i == o.Level ? " selected" : "")}>{Levels[i]}</option>");
        sb.Append("</select></div>");
        if (o.WorldAvailable) sb.Append("<small class=\"dim\">The host world is running; the mode can only change in the lobby.</small>");
        sb.Append("</div><div class=\"row end\"><button class=\"primary\">Save settings</button></div></form>");
        sb.Append("<details class=\"danger\"><summary>Danger zone</summary><div class=\"row\">");
        Form(sb, "end-session", $"<button class=\"bad\">{Icon("kick")}End session</button>", "End the session?", "Every player is kicked and the session is reset.", false, inline: true);
        Form(sb, "shutdown", $"<button class=\"bad\">{Icon("power")}Shut down server</button>", "Shut the server down?", "The process exits and does not restart by itself.", false, inline: true);
        sb.Append("</div></details></section>");
    }

    /// <summary>Flood protection limits, changed live (no restart, nobody is kicked by a change).</summary>
    private static void Flood(StringBuilder sb, FloodSettings f)
    {
        sb.Append("<section class=\"card\" id=\"flood\"><h2>").Append(Icon("ban")).Append("Flood protection</h2>");
        sb.Append("<p class=\"dim small\">").Append(f.Drops).Append(" dropped &middot; ").Append(f.Removed).Append(" removed &middot; ")
          .Append(f.RefusedConnections).Append(" connections refused. Changes apply at once to everybody's next messages; nobody is kicked by a change.</p>");
        sb.Append("<form method=\"post\" action=\"actions/flood-form\" class=\"settings\">");
        sb.Append("<label class=\"check\"><input type=\"hidden\" name=\"enabled\" value=\"0\"><input type=\"checkbox\" name=\"enabled\" value=\"1\"")
          .Append(f.Enabled ? " checked" : "").Append("> Enabled</label><div class=\"floodgrid\">");
        Number(sb, "msg", "Messages / s", f.MessagesPerSecond, 1); Number(sb, "msgBurst", "Message burst", f.MessageBurst, 1);
        Number(sb, "kbps", "Data KB / s", f.BytesPerSecond / 1024, 1); Number(sb, "kbBurst", "Data burst KB", f.ByteBurst / 1024, 1);
        Number(sb, "chat", "Chat lines / s", f.ChatPerSecond, 1); Number(sb, "chatBurst", "Chat burst", f.ChatBurst, 1);
        Number(sb, "kick", "Kick after drops (0 = never)", f.KickAfterDrops, 0); Number(sb, "window", "Within seconds", f.StrikeWindowSeconds, 1);
        Number(sb, "grace", "Upload grace at join (s)", f.UploadGraceSeconds, 0); Number(sb, "perAddress", "Connections per address (0 = any)", f.MaxConnectionsPerAddress, 0);
        sb.Append("</div><div class=\"row end\"><button class=\"primary\">Apply limits</button></div></form></section>");
    }

    private static void Number(StringBuilder sb, string name, string label, long value, int min) =>
        sb.Append("<label class=\"f\"><span>").Append(label).Append("</span><input name=\"").Append(name).Append("\" type=\"number\" min=\"")
          .Append(min).Append("\" value=\"").Append(value).Append("\"></label>");

    /// <summary>
    /// A toggle tile. The markup of the other state travels along in <c>data-alt</c> so the page can flip it the moment it is clicked,
    /// before the game loop has acted: the server's answer then replaces it.
    /// </summary>
    private static void Tile(StringBuilder sb, string action, bool on, string onIcon, string offIcon, string onTitle, string offTitle, string onHint, string offHint)
    {
        string Inner(bool state) => Icon(state ? onIcon : offIcon) + "<b>" + (state ? onTitle : offTitle) + "</b><small>" + (state ? onHint : offHint) + "</small>";
        Form(sb, action, $"<input type=\"hidden\" name=\"value\" value=\"{(on ? 0 : 1)}\"><button class=\"tile {(on ? "active" : "")}\" data-alt=\"{E(Inner(!on))}\">{Inner(on)}</button>");
    }

    /// <summary>The header tabs: the server page and the mods page.</summary>
    private static void Tabs(StringBuilder sb, string current, int mods)
    {
        sb.Append("<nav class=\"tabs\" aria-label=\"Sections\">")
          .Append("<a href=\"./\"").Append(current == "server" ? " aria-current=\"page\"" : "").Append(">").Append(Icon("pulse")).Append("Server</a>")
          .Append("<a href=\"mods\"").Append(current == "mods" ? " aria-current=\"page\"" : "").Append(">").Append(Icon("users")).Append("Mods <span class=\"count\">")
          .Append(mods).Append("</span></a>")
          .Append("<a href=\"areas\"").Append(current == "areas" ? " aria-current=\"page\"" : "").Append(">").Append(Icon("pulse")).Append("Areas</a>")
          .Append("<a href=\"events\"").Append(current == "events" ? " aria-current=\"page\"" : "").Append(">").Append(Icon("pulse")).Append("Events</a>")
          .Append("<a href=\"sectors\"").Append(current == "sectors" ? " aria-current=\"page\"" : "").Append(">").Append(Icon("pulse")).Append("Sectors</a>")
          .Append("<a href=\"auction\"").Append(current == "auction" ? " aria-current=\"page\"" : "").Append(">").Append(Icon("pulse")).Append("Auction</a>")
          .Append("<a href=\"bounties\"").Append(current == "bounties" ? " aria-current=\"page\"" : "").Append(">").Append(Icon("pulse")).Append("Bounties</a></nav>");
    }

    /// <summary>
    /// <c>GET /areas</c>: the area editor. A canvas with the game's own world map and its sector grid (<c>/api/areas/map</c>, needs
    /// --game-dir), tools to paint, erase and rectangle-select sectors and to draw a circle, a list of named areas and a save button
    /// (through <c>POST /api/mod-store</c> into the world_events mod's storage). Script and style are in <see cref="AreaEditorAssets"/>.
    /// </summary>
    public static string AreaEditorPage(IServerAdmin admin)
    {
        var o = admin.Overview();
        var sb = new StringBuilder(12 * 1024);
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1, viewport-fit=cover\">")
          .Append("<meta name=\"color-scheme\" content=\"dark light\"><script>try{var m=localStorage.getItem('gsTheme');if(m==='light'||m==='dark')document.documentElement.dataset.theme=m;}catch(e){}</script><title>Areas - ")
          .Append(E(o.Name)).Append(" - Sacred GameServer</title><style>").Append(Style).Append(AreaEditorAssets.Css).Append(FormControls.Css(".ae")).Append("</style></head><body><div class=\"app\">");
        sb.Append("<header class=\"top\"><div class=\"brand\"><span class=\"logo\">").Append(Icon("shield")).Append("</span><div><b>Sacred GameServer</b><small>")
          .Append(E(o.Name)).Append("</small></div></div>");
        Tabs(sb, "areas", admin.Mods().Count);
        sb.Append("</header><main class=\"ae\"><div class=\"side\">")
          .Append("<section class=\"card\"><h2>Tools</h2><div class=\"tools\">")
          .Append("<button type=\"button\" data-tool=\"pan\" aria-pressed=\"true\">Move</button><button type=\"button\" data-tool=\"paint\">Paint</button>")
          .Append("<button type=\"button\" data-tool=\"erase\">Erase</button><button type=\"button\" data-tool=\"rectadd\">Rect +</button>")
          .Append("<button type=\"button\" data-tool=\"rectdel\">Rect &minus;</button><button type=\"button\" data-tool=\"circle\">Circle</button><button type=\"button\" data-tool=\"region\" id=\"regionTool\" title=\"Click a sector to add every sector of its game region\">Region</button>")
          .Append("<button type=\"button\" id=\"fit\">Fit</button></div><p class=\"hint\" id=\"toolhint\"></p></section>")
          .Append("<section class=\"card\" id=\"regioncard\" hidden><h2>Game regions</h2><label class=\"chk\"><input type=\"checkbox\" id=\"showRegions\" checked> Show the regions on the map</label>")
          .Append("<ul class=\"regions\" id=\"regions\"></ul><p class=\"hint\">A region is a country of the game (the ones the Region Boss quest counts in), read from the game files. The <b>Region</b> tool (or the buttons here) adds all its sectors to the selected area; the area then follows the region, so a world event can be bound to it.</p></section>")
          .Append("<section class=\"card\"><h2>Areas</h2><ul class=\"areas\" id=\"areas\"></ul>")
          .Append("<div class=\"row\" style=\"margin-top:.6rem\"><input type=\"text\" id=\"newName\" maxlength=\"40\" placeholder=\"New area, e.g. Silver Creek\" autocomplete=\"off\"><button type=\"button\" class=\"primary\" id=\"new\">Create</button></div></section>")
          .Append("<section class=\"card\" id=\"detail\" hidden><h2>Selected area</h2><div class=\"row\"><input type=\"text\" id=\"name\" maxlength=\"40\" aria-label=\"Name\"></div>")
          .Append("<div class=\"row\" style=\"margin-top:.4rem\"><label for=\"near\">Plus neighbours within</label><input type=\"number\" id=\"near\" min=\"0\" max=\"3\" value=\"0\"><span class=\"hint\">cells</span></div>")
          .Append("<p class=\"hint\" id=\"info\"></p><div class=\"row\"><button type=\"button\" id=\"zoomArea\">Zoom to it</button><button type=\"button\" id=\"circleOff\" hidden>Remove circle</button><button type=\"button\" id=\"clear\">Clear</button><button type=\"button\" id=\"delete\">Delete</button></div>")
          .Append("<p class=\"hint\" style=\"margin-top:.6rem\">Config line for <code>world_events.lua</code>:</p><textarea class=\"snip\" id=\"snip\" readonly></textarea>")
          .Append("<div class=\"row\"><button type=\"button\" id=\"copy\">Copy snippet</button></div></section>")
          .Append("<section class=\"card\"><div class=\"row\"><button type=\"button\" class=\"primary\" id=\"save\" disabled>Save to the server</button></div><p class=\"status\" id=\"status\" aria-live=\"polite\"></p>")
          .Append("<p class=\"hint\">Saved areas go into the <code>world_events</code> mod's storage (key <code>areas</code>) and win over an area of the same id in the script file. Events use them by id.</p></section>")
          .Append("</div><div class=\"stage\"><canvas id=\"cv\" aria-label=\"World map with the sector grid\"></canvas><p class=\"status\" id=\"loading\" style=\"position:absolute;top:.6rem;left:.8rem;margin:0;color:#efe6d6\">Loading the map...</p><div class=\"readout\" id=\"readout\"></div></div>")
          .Append("</main></div><script>").Append(AreaEditorAssets.Script).Append("</script></body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// <c>GET /events</c>: manages the world_events mod's events while the server runs: the list (script file and web events), add, edit,
    /// turn off, delete, start now, cancel the running one. Script and style are in <see cref="EventsPageAssets"/>; data and commands go through
    /// the mod's <c>events</c> endpoint and <c>POST /api/mod-store</c>.
    /// </summary>
    public static string EventsPage(IServerAdmin admin)
    {
        var o = admin.Overview();
        var sb = new StringBuilder(8 * 1024);
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
          .Append("<meta name=\"color-scheme\" content=\"dark light\"><script>try{var m=localStorage.getItem('gsTheme');if(m==='light'||m==='dark')document.documentElement.dataset.theme=m;}catch(e){}</script><title>Events - ")
          .Append(E(o.Name)).Append(" - Sacred GameServer</title><style>").Append(Style).Append(EventsPageAssets.Css).Append(FormControls.Css(".ev")).Append("</style></head><body><div class=\"app\">");
        sb.Append("<header class=\"top\"><div class=\"brand\"><span class=\"logo\">").Append(Icon("shield")).Append("</span><div><b>Sacred GameServer</b><small>")
          .Append(E(o.Name)).Append("</small></div></div>");
        Tabs(sb, "events", admin.Mods().Count);
        sb.Append("</header><main class=\"ev\"><section class=\"card\" id=\"missing\" hidden><h2>World events</h2><p>The <code>world_events</code> mod is not loaded (or too old for this page). Copy <code>world_events.lua</code> from <code>scripts-examples</code> into the scripts folder.</p><p class=\"dim small\" id=\"missingwhy\"></p></section>")
          .Append("<div id=\"main\" class=\"ev\" hidden><section class=\"card\"><h2>Right now</h2><div id=\"runbox\"></div></section>")
          .Append("<section class=\"card\"><h2>Events</h2><table><thead><tr><th>Event</th><th>Goal</th><th>State</th><th></th></tr></thead><tbody id=\"events\"></tbody></table>")
          .Append("<div class=\"row\" style=\"margin-top:.8rem\"><button type=\"button\" class=\"primary\" id=\"new\">New event</button><button type=\"button\" class=\"ghost\" id=\"reload\">Refresh</button></div>")
          .Append("<p class=\"hint dim small\">Changes apply within a couple of seconds, no restart or reload. Events of the script file can be changed or turned off here; the file itself is not touched. A running event keeps the definition it started with.</p>")
          .Append("<p class=\"status\" id=\"status\" aria-live=\"polite\"></p></section></div><dialog id=\"editor\"></dialog></main></div><script>")
          .Append(EventsPageAssets.Script).Append("</script></body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// <c>GET /mods</c>: one card per mod (script file) with its pages and its endpoints listed apart. Links open in a new tab; pages
    /// run sandboxed (see WebUiServer.ModEndpoint). Public ones show their <c>/public/mods/...</c> address too.
    /// </summary>
    public static string ModsPage(IServerAdmin admin, bool publicApi, string? notice = null)
    {
        var o = admin.Overview();
        var mods = admin.Mods().OrderBy(m => m.Mod, StringComparer.OrdinalIgnoreCase).ToList();
        var sb = new StringBuilder(8 * 1024);
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
          .Append("<meta name=\"color-scheme\" content=\"dark light\"><script>try{var m=localStorage.getItem('gsTheme');if(m==='light'||m==='dark')document.documentElement.dataset.theme=m;}catch(e){}</script><title>Mods - ")
          .Append(E(o.Name)).Append(" - Sacred GameServer</title><style>").Append(Style).Append("</style></head><body><div class=\"app\">");
        sb.Append("<header class=\"top\"><div class=\"brand\"><span class=\"logo\">").Append(Icon("shield")).Append("</span><div><b>Sacred GameServer</b><small>")
          .Append(E(o.Name)).Append("</small></div></div>");
        Tabs(sb, "mods", mods.Count);
        sb.Append("</header><main class=\"modgrid\">");
        if (notice is not null) sb.Append("<div id=\"toast\" role=\"status\">").Append(E(notice)).Append("</div>");
        if (mods.Count > 0)
            sb.Append("<section class=\"card modbar\"><p class=\"dim small\">Scripts reload by themselves when a file changes (only that mod). A mod's <code>unload</code> handler saves its state first.</p>");
        if (mods.Count > 0)
        {
            Form(sb, "reload-mods", "<input type=\"hidden\" name=\"back\" value=\"mods\"><input type=\"hidden\" name=\"text\" value=\"\"><button class=\"ghost\" title=\"Every mod saves its state in its unload handler and is loaded again from disk, in a fresh Lua state\">Reload all mods</button>", null, null, false, inline: true);
            sb.Append("</section>");
        }
        if (mods.Count == 0)
            sb.Append("<section class=\"card\"><div class=\"empty\">").Append(Icon("users")).Append("<p>No mods installed.</p>")
              .Append("<small>Put .lua scripts into the scripts folder; they load without a restart.</small></div></section>");
        foreach (var mod in mods)
        {
            var pages = mod.Items.Where(m => m.Page).OrderBy(m => m.Path, StringComparer.OrdinalIgnoreCase).ToList();
            var endpoints = mod.Items.Where(m => !m.Page).OrderBy(m => m.Path, StringComparer.OrdinalIgnoreCase).ToList();
            sb.Append("<section class=\"card mod\"><h2>").Append(Icon("pulse")).Append(E(mod.Mod)).Append(".lua");
            // The description behind a (?): shown on hover, on keyboard focus and on tap (a tap focuses the button), no script needed.
            if (mod.Description is { Length: > 0 } about)
                sb.Append("<button type=\"button\" class=\"help\" aria-label=\"About ").Append(E(mod.Mod)).Append("\">?<span class=\"tip\" role=\"tooltip\">")
                  .Append(E(about)).Append("</span></button>");
            sb.Append("</h2>");
            if (mod.Error is { Length: > 0 } error) sb.Append("<p class=\"moderror\">Failed to load: ").Append(E(error)).Append("</p>");
            ModList(sb, "Pages", pages, publicApi);
            ModList(sb, "Endpoints", endpoints, publicApi);
            Form(sb, "reload-mods", $"<input type=\"hidden\" name=\"back\" value=\"mods\"><input type=\"hidden\" name=\"text\" value=\"{E(mod.Mod)}\"><button class=\"ghost\">Reload</button>", null, null, false, inline: true);
            sb.Append("</section>");
        }
        sb.Append("</main></div></body></html>");
        return sb.ToString();
    }

    private static void ModList(StringBuilder sb, string title, List<ModEndpointInfo> items, bool publicApi)
    {
        sb.Append("<h3>").Append(title).Append(" <span class=\"count\">").Append(items.Count).Append("</span></h3>");
        if (items.Count == 0) { sb.Append("<p class=\"dim small\">none</p>"); return; }
        sb.Append("<ul class=\"modlist\">");
        foreach (var m in items)
        {
            var path = "mods/" + Uri.EscapeDataString(m.Mod) + "/" + string.Join('/', m.Path.Split('/').Select(Uri.EscapeDataString));
            sb.Append("<li><a href=\"").Append(E(path)).Append("\" target=\"_blank\" rel=\"noopener noreferrer\"><b>/").Append(E(m.Path.Length == 0 ? "" : m.Path))
              .Append(m.Path.Length == 0 ? "(index)" : "").Append("</b></a>");
            if (m.Public) sb.Append(" <span class=\"pill ").Append(publicApi ? "ok" : "").Append("\">").Append(publicApi ? "public" : "public (API off)").Append("</span>");
            if (m.Description is { Length: > 0 } d) sb.Append("<small>").Append(E(d)).Append("</small>");
            if (m.Public && publicApi) sb.Append("<small class=\"dim\">/public/").Append(E(path)).Append("</small>");
            sb.Append("</li>");
        }
        sb.Append("</ul>");
    }

    private static void Bans(StringBuilder sb, IReadOnlyList<BanEntry> bans)
    {
        sb.Append("<section class=\"card\" id=\"bans\"><h2>").Append(Icon("ban")).Append("Bans <span class=\"count\">").Append(bans.Count).Append("</span></h2>");
        if (bans.Count == 0) sb.Append("<p class=\"dim small\">No banned addresses.</p>");
        foreach (var b in bans)
        {
            sb.Append("<div class=\"ban\"><div><b>").Append(E(b.Address)).Append("</b><small>").Append(E(b.Reason.Length > 0 ? b.Reason : "no reason given")).Append(" &middot; ")
              .Append(DateTimeOffset.FromUnixTimeMilliseconds(b.CreatedUnixMs).ToString("yyyy-MM-dd HH:mm")).Append(" UTC</small></div>");
            Form(sb, "unban", $"<input type=\"hidden\" name=\"address\" value=\"{E(b.Address)}\"><button class=\"ghost\">Lift ban</button>", null, null, false, inline: true);
            sb.Append("</div>");
        }
        Form(sb, "ban", "<input name=\"address\" type=\"text\" placeholder=\"Ban an IP address\" required aria-label=\"IP address\"><input name=\"reason\" type=\"text\" placeholder=\"Reason\" maxlength=\"100\" aria-label=\"Reason\"><button class=\"bad\">Ban</button>");
        sb.Append("</section>");
    }

    private static void Form(StringBuilder sb, string action, string inner, string? confirmTitle = null, string? confirmText = null, bool askReason = false, bool inline = false)
    {
        sb.Append("<form method=\"post\" action=\"actions/").Append(action).Append("\" class=\"").Append(inline ? "inline" : "row add").Append('"');
        if (confirmTitle is not null) sb.Append(" data-title=\"").Append(E(confirmTitle)).Append("\" data-text=\"").Append(E(confirmText)).Append('"');
        if (askReason) sb.Append(" data-ask-reason");
        sb.Append('>').Append(inner).Append("</form>");
    }

    private static void Pill(StringBuilder sb, string text, string kind) =>
        sb.Append("<span class=\"pill ").Append(kind).Append("\">").Append(E(text)).Append("</span>");

    private static string Initial(string name)
    {
        var t = name.Trim();
        return t.Length == 0 ? "?" : char.ToUpperInvariant(t[0]).ToString();
    }

    private static string Duration(double seconds)
    {
        var s = (long)seconds;
        var d = s / 86400; var h = s % 86400 / 3600; var m = s % 3600 / 60;
        return (d > 0 ? d + "d " : "") + (d > 0 || h > 0 ? h + "h " : "") + m + "m";
    }

    /// <summary>One chat line, the same markup the script builds for live events.</summary>
    private static void ChatLine(StringBuilder sb, ChatEntry e)
    {
        var time = DateTimeOffset.FromUnixTimeMilliseconds(e.TimeUnixMs).ToString("HH:mm");
        sb.Append("<div class=\"msg k").Append((int)e.Kind).Append("\" data-id=\"").Append(e.Id).Append("\" style=\"--h:").Append(e.FromSlot * 47 % 360).Append("\"><time>").Append(time).Append("</time>");
        switch (e.Kind)
        {
            case ChatKind.Public: sb.Append("<b>").Append(E(e.FromName)).Append("</b>"); break;
            case ChatKind.Private: sb.Append("<b>").Append(E(e.FromName)).Append("</b><em>&rarr; #").Append(e.ToSlot).Append("</em>"); break;
            case ChatKind.Broadcast: sb.Append("<b>Server</b>"); break;
            case ChatKind.ServerMessage: sb.Append("<b>Server</b><em>&rarr; #").Append(e.ToSlot).Append("</em>"); break;
            default: if (e.FromName.Length > 0) sb.Append("<b>").Append(E(e.FromName)).Append("</b>"); break;
        }
        sb.Append("<span class=\"text\">").Append(E(e.Text)).Append("</span></div>");
    }

    private const string Style = """
:root{--bg:#15110d;--bg2:#1c1712;--card:#241d16;--card2:#2c241b;--line:#3b3025;--text:#efe6d6;--dim:#a39683;--gold:#e0a944;--gold2:#f2c36b;--ok:#79b873;--warn:#e3a53f;--bad:#d4604d;--info:#6fa3d6;--shadow:0 6px 24px rgba(0,0,0,.35);--r:10px}
:root{color-scheme:dark}
@media (prefers-color-scheme:light){:root:not([data-theme=dark]){color-scheme:light;--bg:#f4efe6;--bg2:#ebe4d6;--card:#fffdf8;--card2:#f6f0e4;--line:#ddd2bf;--text:#2b2318;--dim:#7a6d5a;--gold:#a66d0c;--gold2:#c4891f;--ok:#2f7d32;--warn:#a96a00;--bad:#b3392a;--info:#2a6aa6;--shadow:0 4px 16px rgba(60,40,10,.12)}}
:root[data-theme=light]{color-scheme:light;--bg:#f4efe6;--bg2:#ebe4d6;--card:#fffdf8;--card2:#f6f0e4;--line:#ddd2bf;--text:#2b2318;--dim:#7a6d5a;--gold:#a66d0c;--gold2:#c4891f;--ok:#2f7d32;--warn:#a96a00;--bad:#b3392a;--info:#2a6aa6;--shadow:0 4px 16px rgba(60,40,10,.12)}
[hidden]{display:none!important}
*{box-sizing:border-box}html{-webkit-text-size-adjust:100%}
body{margin:0;background:radial-gradient(1200px 600px at 10% -10%,var(--bg2),var(--bg));background-attachment:fixed;color:var(--text);font:15px/1.5 system-ui,-apple-system,"Segoe UI",Roboto,"Helvetica Neue",Arial,sans-serif}
.i{width:1.15em;height:1.15em;fill:none;stroke:currentColor;stroke-width:1.8;stroke-linecap:round;stroke-linejoin:round;flex:none;vertical-align:-.2em}
.app{max-width:1280px;margin:0 auto;padding:1rem}
.top{display:flex;flex-wrap:wrap;align-items:center;gap:1rem 2rem;padding:.9rem 1.1rem;background:var(--card);border:1px solid var(--line);border-radius:var(--r);box-shadow:var(--shadow);margin-bottom:1rem}
.tabs{display:flex;gap:.4rem;flex-wrap:wrap}.tabs a{display:inline-flex;align-items:center;gap:.4rem;padding:.4rem .8rem;border-radius:999px;border:1px solid var(--line);color:var(--dim);text-decoration:none}
.tabs a[aria-current=page]{color:var(--text);border-color:var(--gold);background:var(--card2)}.tabs a:hover{color:var(--text)}
.help{position:relative;display:inline-flex;align-items:center;justify-content:center;width:1.3rem;height:1.3rem;min-height:0;margin-left:.4rem;padding:0;border-radius:50%;font-size:.75rem;line-height:1;color:var(--dim);background:var(--card2);border:1px solid var(--line);cursor:help;text-transform:none;letter-spacing:0}
.help .tip{display:none;position:absolute;left:50%;top:calc(100% + .4rem);transform:translateX(-50%);z-index:5;width:max-content;max-width:min(22rem,80vw);padding:.5rem .7rem;border-radius:8px;background:var(--card);border:1px solid var(--gold);color:var(--text);font-size:.85rem;font-weight:400;text-align:left;white-space:normal;box-shadow:var(--shadow)}
.help:hover .tip,.help:focus .tip,.help:focus-visible .tip{display:block}.moderror{color:var(--bad);font-size:.85rem;margin:.3rem 0}
.modgrid{display:grid;grid-template-columns:repeat(auto-fill,minmax(20rem,1fr));gap:1rem}.mod h3{margin:.8rem 0 .4rem;font-size:.85rem;color:var(--dim);text-transform:uppercase;letter-spacing:.06em}
.modlist{list-style:none;margin:0;padding:0;display:grid;gap:.45rem}.modlist li{display:flex;flex-wrap:wrap;align-items:baseline;gap:.2rem .5rem;padding:.45rem .6rem;background:var(--card2);border-radius:8px}
.modlist small{flex-basis:100%;color:var(--dim)}.modlist a{color:var(--gold2);text-decoration:none}.modlist a:hover{text-decoration:underline}
.brand{display:flex;align-items:center;gap:.7rem}.brand b{display:block;letter-spacing:.02em}.brand small{color:var(--dim)}
.logo{display:grid;place-items:center;width:2.4rem;height:2.4rem;border-radius:9px;background:linear-gradient(135deg,var(--gold2),var(--gold));color:#2a1c05}.logo .i{width:1.4rem;height:1.4rem;stroke-width:2}
.head{display:flex;flex-wrap:wrap;align-items:center;gap:.6rem 1.6rem;flex:1}.head h1{margin:0;font-size:1.2rem;color:var(--gold2);font-weight:650}
.stats{display:flex;flex-wrap:wrap;gap:.4rem 1.4rem}.stat{display:flex;align-items:center;gap:.55rem;color:var(--dim)}.stat b{display:block;color:var(--text);line-height:1.2}.stat small{display:block;font-size:.75rem;line-height:1.2}
.slots{display:grid;grid-template-columns:repeat(8,.5rem);gap:2px}.slots i{height:.5rem;border-radius:2px;background:var(--line)}.slots i.on{background:var(--ok)}.slots i.free{background:var(--card2);outline:1px solid var(--line)}.slots i.off{opacity:.25}
.pills{display:flex;flex-wrap:wrap;gap:.35rem;margin-left:auto}
.pill{padding:.12rem .6rem;border-radius:99px;background:var(--card2);border:1px solid var(--line);font-size:.78rem;color:var(--dim)}.pill.ok{color:var(--ok);border-color:color-mix(in srgb,var(--ok) 40%,var(--line))}.pill.warn{color:var(--warn);border-color:color-mix(in srgb,var(--warn) 40%,var(--line))}.pill.bad{color:var(--bad);border-color:color-mix(in srgb,var(--bad) 40%,var(--line))}.pill.info{color:var(--info);border-color:color-mix(in srgb,var(--info) 40%,var(--line))}
.grid{display:grid;grid-template-columns:minmax(0,1.1fr) minmax(0,1fr);gap:1rem;align-items:start}.col{display:grid;gap:1rem;min-width:0}
.card{background:var(--card);border:1px solid var(--line);border-radius:var(--r);box-shadow:var(--shadow);padding:1rem 1.1rem;min-width:0}
.card h2{display:flex;align-items:center;gap:.5rem;margin:0 0 .8rem;font-size:.82rem;text-transform:uppercase;letter-spacing:.09em;color:var(--dim);font-weight:650}
.count{background:var(--card2);border:1px solid var(--line);border-radius:99px;padding:0 .5rem;color:var(--text);letter-spacing:0}
.empty{display:grid;justify-items:center;gap:.2rem;padding:1.4rem .5rem;color:var(--dim);text-align:center}.empty .i{width:2rem;height:2rem;opacity:.6}.empty p{margin:.2rem 0 0;color:var(--text)}
.player{display:flex;align-items:center;gap:.75rem;padding:.6rem .2rem;border-top:1px solid var(--line)}.player:first-of-type{border-top:0}
.avatar{display:grid;place-items:center;width:2.4rem;height:2.4rem;border-radius:50%;font-weight:700;color:#fff;background:hsl(var(--h) 45% 38%);flex:none}
.who{flex:1;min-width:0}.who b{display:inline-block;max-width:100%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;vertical-align:bottom}.who small{display:block;color:var(--dim);font-size:.78rem;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.badge{display:inline-flex;align-items:center;gap:.2rem;margin-left:.5rem;padding:0 .45rem;border-radius:99px;background:color-mix(in srgb,var(--gold) 20%,transparent);color:var(--gold2);font-size:.72rem}.badge .i{width:.9em;height:.9em}
.ping{font-variant-numeric:tabular-nums;font-size:.8rem;padding:.1rem .5rem;border-radius:6px;background:var(--card2)}.ping.ok{color:var(--ok)}.ping.warn{color:var(--warn)}.ping.bad{color:var(--bad)}
.acts{display:flex;gap:.3rem}.inline{display:inline-flex;margin:0}
button,input,select{font:inherit;color:var(--text)}
input,select{background:var(--bg);border:1px solid var(--line);border-radius:8px;padding:.45rem .65rem;min-width:0}
input:focus,select:focus,button:focus-visible,summary:focus-visible{outline:2px solid var(--gold);outline-offset:1px;border-color:transparent}
button{display:inline-flex;align-items:center;justify-content:center;gap:.4rem;cursor:pointer;border:1px solid var(--line);background:var(--card2);border-radius:8px;padding:.45rem .85rem;transition:background .12s,border-color .12s,transform .05s}
button:hover{border-color:var(--gold)}button:active{transform:translateY(1px)}
button.primary{background:linear-gradient(180deg,var(--gold2),var(--gold));border-color:transparent;color:#2a1c05;font-weight:650}button.primary:hover{filter:brightness(1.08)}
button.ghost{background:transparent}button.bad{color:var(--bad);border-color:color-mix(in srgb,var(--bad) 45%,var(--line))}button.bad:hover{background:color-mix(in srgb,var(--bad) 14%,transparent);border-color:var(--bad)}
button.icon{padding:.4rem;width:2.1rem;height:2.1rem}
.toggles{display:grid;grid-template-columns:1fr 1fr;gap:.7rem;margin-bottom:.9rem}.toggles form{display:block}
.tile{display:grid;grid-template-columns:auto 1fr;grid-template-rows:auto auto;column-gap:.7rem;text-align:left;width:100%;padding:.75rem .9rem;justify-items:start}.tile .i{grid-row:span 2;align-self:center;width:1.5rem;height:1.5rem;color:var(--ok)}.tile b{line-height:1.2}.tile small{color:var(--dim);font-size:.75rem}
.tile.pending{opacity:.7;cursor:progress}.tile:disabled{cursor:progress}.tile.active{border-color:var(--warn);background:color-mix(in srgb,var(--warn) 12%,var(--card2))}.tile.active .i{color:var(--warn)}
.floodgrid{display:grid;grid-template-columns:repeat(auto-fill,minmax(10rem,1fr));gap:.6rem}.settings{display:grid;gap:.8rem}.f{display:grid;gap:.3rem}.f>span{font-size:.8rem;color:var(--dim)}.f.narrow input{max-width:8rem}.ctl{display:flex;gap:.5rem}.ctl select{flex:1}.check{display:flex;align-items:center;gap:.5rem;font-size:.85rem;color:var(--dim)}.check input{min-height:0}.settings .row.end{display:flex;justify-content:flex-end}
.small{font-size:.85rem}.dim{color:var(--dim)}
.add{display:flex;gap:.5rem;margin-top:.7rem}.add input{flex:1}
.ban{display:flex;align-items:center;justify-content:space-between;gap:.6rem;padding:.5rem 0;border-top:1px solid var(--line)}.ban small{display:block;color:var(--dim);font-size:.78rem}
details.danger{margin-top:1rem;border:1px dashed color-mix(in srgb,var(--bad) 50%,var(--line));border-radius:8px;padding:.5rem .8rem}details.danger summary{cursor:pointer;color:var(--bad);font-size:.85rem}details.danger .row{display:flex;gap:.5rem;flex-wrap:wrap;margin-top:.6rem}
.chat{position:sticky;top:1rem;display:flex;flex-direction:column;height:calc(100vh - 2rem);min-height:26rem;max-height:56rem}
.live{margin-left:auto;font-size:.72rem;text-transform:none;letter-spacing:0;color:var(--dim);padding:0 .5rem;border-radius:99px;border:1px solid var(--line)}.live.on{color:var(--ok)}.live.on:before{content:"";display:inline-block;width:.45rem;height:.45rem;border-radius:50%;background:var(--ok);margin-right:.35rem}
#chat{flex:1;overflow-y:auto;background:var(--bg);border:1px solid var(--line);border-radius:8px;padding:.5rem .7rem;scroll-behavior:smooth}
.msg{display:flex;gap:.55rem;align-items:baseline;padding:.15rem 0;word-break:break-word}.msg time{color:var(--dim);font-size:.72rem;font-variant-numeric:tabular-nums;flex:none}.msg b{color:hsl(var(--h) 60% 62%);flex:none}.msg em{color:var(--dim);font-style:normal;font-size:.8rem;flex:none}.msg .text{min-width:0}
@media (prefers-color-scheme:light){:root:not([data-theme=dark]) .msg b{color:hsl(var(--h) 55% 34%)}}
:root[data-theme=light] .msg b{color:hsl(var(--h) 55% 34%)}
.themebtn{gap:.45rem}.themebtn span{font-size:.85rem}
.msg.k4{color:var(--dim);font-size:.85rem}.msg.k4 b{color:var(--dim)}.msg.k2 b,.msg.k3 b{color:var(--gold2)}.msg.k2 .text,.msg.k3 .text{color:var(--gold2)}.msg.k1 .text{font-style:italic}
#latest{position:absolute;align-self:center;bottom:5.2rem;box-shadow:var(--shadow);background:var(--card2)}
.composer{display:flex;gap:.5rem;margin-top:.7rem}.composer input{flex:1}.composer select{max-width:11rem}
dialog{border:1px solid var(--line);border-radius:var(--r);background:var(--card);color:var(--text);box-shadow:var(--shadow);padding:1.2rem 1.3rem;max-width:26rem;width:calc(100% - 2rem)}dialog::backdrop{background:rgba(0,0,0,.55)}
dialog h3{margin:0 0 .3rem}dialog p{margin:.2rem 0 .9rem}dialog input{width:100%;margin-bottom:.9rem}.row.end{display:flex;justify-content:flex-end;gap:.5rem}
#toast{position:fixed;left:50%;bottom:1.2rem;transform:translateX(-50%);padding:.6rem 1.1rem;border-radius:99px;background:var(--card2);border:1px solid var(--ok);box-shadow:var(--shadow);animation:fade 3.6s forwards}
@keyframes fade{0%,85%{opacity:1}100%{opacity:0;visibility:hidden}}
@media (max-width:960px){.grid{grid-template-columns:1fr}.chat{position:static;height:30rem}.pills{margin-left:0}}
@media (max-width:520px){.toggles{grid-template-columns:1fr}.composer{flex-wrap:wrap}}
@media (pointer:coarse){button,input,select{min-height:2.75rem}button.icon{width:2.75rem;height:2.75rem}input,select,textarea{font-size:16px}.acts{gap:.45rem}.ctl{flex-wrap:wrap}.app{padding:.6rem .6rem calc(.6rem + env(safe-area-inset-bottom))}details.danger summary{padding:.5rem 0}}
@media (max-width:520px){.player{display:grid;grid-template-columns:auto 1fr auto;row-gap:.5rem}.player .acts{grid-column:1/-1;justify-content:flex-end}.top{padding:.8rem}.chat{height:min(34rem,calc(100dvh - 1.5rem))}.msg{flex-wrap:wrap;gap:.1rem .5rem}.msg .text{flex-basis:100%}.msg.k4 .text{flex-basis:auto}.composer select{flex:1 1 100%;max-width:none}.composer input{flex:1 1 60%}}
@media (prefers-reduced-motion:reduce){*{transition:none!important;animation-duration:0s!important;scroll-behavior:auto!important}}
""";

    // Confirm dialog (native <dialog>), recipient shortcut, state swap, chat stream. No libraries; text goes in through textContent.
    private const string Script = """
(function(){
'use strict';
var $=function(id){return document.getElementById(id);};
var chat=$('chat'),latest=$('latest'),live=$('live'),ask=$('ask');
function atBottom(){return chat.scrollTop+chat.clientHeight>=chat.scrollHeight-40;}
function toBottom(){chat.style.scrollBehavior='auto';chat.scrollTop=chat.scrollHeight;chat.style.scrollBehavior='';latest.hidden=true;}
toBottom();chat.addEventListener('scroll',function(){if(atBottom())latest.hidden=true;});latest.onclick=toBottom;
var t=$('toast');if(t){history.replaceState(null,'',location.pathname);}

// confirm / ask-for-reason dialog on forms that carry data-title
var pending=null;
document.addEventListener('submit',function(e){var f=e.target;if(!f.dataset||!f.dataset.title||f.dataset.ok)return;
e.preventDefault();pending=f;$('askTitle').textContent=f.dataset.title;$('askText').textContent=f.dataset.text||'';
var r=$('askReason');r.hidden=!('askReason' in f.dataset);r.value='';ask.showModal();if(!r.hidden)r.focus();});
ask.addEventListener('close',function(){var f=pending;pending=null;if(!f||ask.returnValue!=='ok')return;
var r=f.querySelector('input[name=reason]');if(r&&'askReason' in f.dataset)r.value=$('askReason').value;f.dataset.ok='1';f.requestSubmit();});
$('askReason').addEventListener('keydown',function(e){if(e.key==='Enter'){e.preventDefault();$('askOk').click();}});

// theme: auto (follows the system) -> light -> dark, remembered in this browser
var themeBtn=$('theme');
var themeIcons={auto:'<svg class="i" viewBox="0 0 24 24"><path d="M2 4h20v13H2zM8 21h8M12 17v4"/></svg>',light:'<svg class="i" viewBox="0 0 24 24"><path d="M12 17a5 5 0 1 0 0-10 5 5 0 0 0 0 10zM12 1v2M12 21v2M4.2 4.2l1.4 1.4M18.4 18.4l1.4 1.4M1 12h2M21 12h2M4.2 19.8l1.4-1.4M18.4 5.6l1.4-1.4"/></svg>',dark:'<svg class="i" viewBox="0 0 24 24"><path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/></svg>'};
var themeNames={auto:'Auto',light:'Light',dark:'Dark'};
var theme=(function(){try{var m=localStorage.getItem('gsTheme');return m==='light'||m==='dark'?m:'auto';}catch(e){return 'auto';}})();
function applyTheme(){if(theme==='auto')delete document.documentElement.dataset.theme;else document.documentElement.dataset.theme=theme;
try{if(theme==='auto')localStorage.removeItem('gsTheme');else localStorage.setItem('gsTheme',theme);}catch(e){}
themeBtn.innerHTML=themeIcons[theme]+'<span>'+themeNames[theme]+'</span>';themeBtn.title='Theme: '+themeNames[theme]+' (click to change)';}
themeBtn.addEventListener('click',function(){theme=theme==='auto'?'light':theme==='light'?'dark':'auto';applyTheme();});applyTheme();

// "message" button next to a player
document.addEventListener('click',function(e){var b=e.target.closest&&e.target.closest('[data-msg]');if(!b)return;$('chatTo').value=b.dataset.msg;$('chatText').focus();});

// chat
function line(e){var d=document.createElement('div');d.className='msg k'+e.kind;d.dataset.id=e.id;d.style.setProperty('--h',(e.fromSlot*47)%360);
var tm=document.createElement('time');tm.textContent=new Date(e.timeUnixMs).toLocaleTimeString([],{hour:'2-digit',minute:'2-digit'});d.append(tm);
function who(n){var b=document.createElement('b');b.textContent=n;d.append(b);}
function to(s){var m=document.createElement('em');m.textContent='→ #'+s;d.append(m);}
switch(e.kind){case 0:who(e.fromName);break;case 1:who(e.fromName);to(e.toSlot);break;case 2:who('Server');break;case 3:who('Server');to(e.toSlot);break;default:if(e.fromName)who(e.fromName);}
var x=document.createElement('span');x.className='text';x.textContent=e.text;d.append(x);return d;}
function add(e){var stick=atBottom();chat.append(line(e));while(chat.childElementCount>500)chat.firstElementChild.remove();if(stick)toBottom();else latest.hidden=false;}
var last=chat.lastElementChild?chat.lastElementChild.dataset.id:'0';
var es=new EventSource('api/chat/stream?after='+last);
es.addEventListener('chat',function(m){add(JSON.parse(m.data));});
es.onopen=function(){live.textContent='live';live.className='live on';};es.onerror=function(){live.textContent='reconnecting';live.className='live';};
$('chatForm').addEventListener('submit',function(e){e.preventDefault();var f=e.target;
fetch(f.action,{method:'POST',headers:{'Accept':'application/json'},body:new URLSearchParams(new FormData(f))}).then(function(r){if(r.ok){f.text.value='';f.text.focus();}});});

// Lock/pause flip at once (the markup of the other state is in data-alt), stay disabled until the server confirms, and the page
// polls every 250 ms meanwhile. The swap leaves #session alone while a flip is unconfirmed (max 5 s, then the server's state wins).
var fastUntil=0,flips={};
function fast(){fastUntil=Date.now()+5000;}
if(t)fast();
function sessionBusy(src){var busy=false,now=Date.now();Object.keys(flips).forEach(function(a){var f=src.querySelector('form[action="actions/'+a+'"] input[name=value]');
if(f&&f.value===flips[a].expected){delete flips[a];}else if(now-flips[a].since>5000){delete flips[a];toast('The server did not confirm the change.');}else busy=true;});return busy;}
function toast(text){var d=document.createElement('div');d.id='toast';d.setAttribute('role','status');d.textContent=text;document.body.append(d);setTimeout(function(){d.remove();},3600);}
document.addEventListener('submit',function(e){var f=e.target,a=f.getAttribute&&f.getAttribute('action');if(a!=='actions/lock'&&a!=='actions/pause')return;
e.preventDefault();var b=f.querySelector('button.tile'),h=f.querySelector('input[name=value]');if(b.disabled)return;
var body=new URLSearchParams(new FormData(f));var alt=b.dataset.alt;b.dataset.alt=b.innerHTML;b.innerHTML=alt;b.classList.toggle('active');b.classList.add('pending');b.disabled=true;
var key=a.slice(8);h.value=h.value==='1'?'0':'1';flips[key]={expected:h.value,since:Date.now()};
fetch(a,{method:'POST',headers:{'Accept':'application/json'},body:body}).then(function(r){if(!r.ok)throw new Error(r.status);fast();refresh();}).catch(function(){delete flips[key];toast('The change was refused.');refresh();});});

// swap the live blocks without disturbing an input the user is typing in or a form with unsaved edits
document.addEventListener('input',function(e){if(e.target.form)e.target.form.dataset.dirty='1';});
function refresh(){if(ask.open)return Promise.resolve();return fetch('fragment',{cache:'no-store'}).then(function(r){return r.ok?r.text():null;}).then(function(h){if(!h)return;
var doc=new DOMParser().parseFromString(h,'text/html');
['head','players','session','flood','bans'].forEach(function(id){var cur=$(id),src=doc.getElementById(id);if(id==='session'&&src&&sessionBusy(src))return;if(!cur||!src||cur.contains(document.activeElement)||cur.querySelector('form[data-dirty]'))return;if(cur.innerHTML!==src.innerHTML)cur.innerHTML=src.innerHTML;});
var sel=$('chatTo'),v=sel.value,opts=doc.querySelectorAll('#players [data-msg]');
var slots=Array.prototype.map.call(opts,function(o){return o.dataset.msg;});var have=Array.prototype.map.call(sel.options,function(o){return o.value;}).slice(1);
if(slots.join()!==have.join()){while(sel.options.length>1)sel.remove(1);Array.prototype.forEach.call(doc.querySelectorAll('#players .player'),function(p){var s=p.querySelector('[data-msg]').dataset.msg;sel.add(new Option('#'+s+' '+p.querySelector('.who b').textContent,s));});sel.value=slots.indexOf(v)>=0||v==='0'?v:'0';}
}).catch(function(){});}
function tick(){refresh().then(function(){setTimeout(tick,Date.now()<fastUntil?250:3000);});}
tick();
})();
""";
}
