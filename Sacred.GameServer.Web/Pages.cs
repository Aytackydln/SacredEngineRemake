using System.Net;
using System.Text;

namespace Sacred.GameServer.Web;

/// <summary>
/// Server side rendering of the management page. Everything is self contained (inline CSS, script and SVG icons, system fonts):
/// no external request, so it works on a LAN without Internet. Dynamic values (player names and chat text come from the network)
/// are always HTML encoded. The blocks <c>#head</c>, <c>#players</c>, <c>#bans</c> and <c>#session</c> are what <c>/fragment</c>
/// returns and the page swaps in every few seconds; chat arrives through the SSE stream.
/// </summary>
internal static class Pages
{
    private static readonly string[] Modes = ["Default", "Free game", "Player killer", "Campaign", "Underworld campaign"];
    private static readonly string[] Levels = ["Any level", "Bronze", "Silver", "Gold", "Platinum", "Niobium"];

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");

    // 24x24 stroke icons (Assets/icons.txt, name TAB markup of the paths): no icon font.
    private static readonly Dictionary<string, string> Icons = Assets.Text(EmbeddedResource_Assets.icons_txt).Split('\n')
        .Select(l => l.Split('\t', 2)).Where(a => a.Length == 2).ToDictionary(a => a[0], a => a[1]);

    private static string Icon(string name) => "<svg class=\"i\" viewBox=\"0 0 24 24\" aria-hidden=\"true\">" + (Icons.TryGetValue(name, out var paths) ? paths : "") + "</svg>";

    public static string Index(IServerAdmin admin, string? notice)
    {
        var o = admin.Overview();
        var players = admin.Players();
        var sb = new StringBuilder(24 * 1024);
        sb.Append("<div class=\"app\">");
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
        sb.Append(Assets.Text(EmbeddedResource_Assets.confirm_dialog_html));
        if (notice is not null) sb.Append("<div id=\"toast\" role=\"status\">").Append(E(notice)).Append("</div>");
        return Assets.Page(E(o.Name) + " - Sacred GameServer", sb.ToString(), ["app.css"], ["app.js"]);
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
          .Append(mods).Append("</span></a></nav>");
    }

    /// <summary>
    /// <c>GET /mods</c>: one card per mod (script file) with its pages and its endpoints listed apart. Links open in a new tab; pages
    /// run sandboxed (see WebUiServer.ModEndpoint). Public ones show their <c>/public/mods/...</c> address too.
    /// </summary>
    public static string ModsPage(IServerAdmin admin, bool publicApi)
    {
        var o = admin.Overview();
        var mods = admin.Mods().OrderBy(m => m.Mod, StringComparer.OrdinalIgnoreCase).ToList();
        var sb = new StringBuilder(8 * 1024);
        sb.Append("<div class=\"app\">");
        sb.Append("<header class=\"top\"><div class=\"brand\"><span class=\"logo\">").Append(Icon("shield")).Append("</span><div><b>Sacred GameServer</b><small>")
          .Append(E(o.Name)).Append("</small></div></div>");
        Tabs(sb, "mods", mods.Count);
        sb.Append("</header><main class=\"modgrid\">");
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
            sb.Append("</section>");
        }
        sb.Append("</main></div>");
        return Assets.Page("Mods - " + E(o.Name) + " - Sacred GameServer", sb.ToString(), ["app.css"], []);
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
}
