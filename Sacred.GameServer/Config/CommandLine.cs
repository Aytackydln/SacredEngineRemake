using System.Globalization;
using System.Text;

namespace Sacred.GameServer.Config;

public enum OptionKind { Flag, Int, Text, Path }

/// <summary>One command-line option. <see cref="Name"/> is the canonical kebab-case name; aliases are accepted too ('_' equals '-', case ignored).</summary>
public sealed record OptionSpec(string Group, string Name, OptionKind Kind, string Help, string Sample, long Min = 0, long Max = int.MaxValue,
    string[]? Aliases = null, bool WebStyle = false, bool Ignored = false)
{
    public IEnumerable<string> AllNames() { yield return Name; if (Aliases is not null) foreach (var a in Aliases) yield return a; }
}

/// <summary>
/// The command line is strict: every option the server understands is in <see cref="Options"/>, which the parser (<see cref="Validate"/>)
/// and the <c>--help</c> text (<see cref="HelpText"/>) share, so they cannot drift apart. Gameserver.cfg keys, webui.json and
/// environment variables keep their lenient behaviour (the original's cfg files carry keys this server does not use); only arguments
/// are refused, before any port is bound.
/// </summary>
public static class CommandLine
{
    private const int Port = 65535;

    public static readonly OptionSpec[] Options =
    [
        // ---- the original GameServer.exe's Gameserver.cfg keys, as options ----
        new("Original Gameserver.cfg keys", "network-port-tcp", OptionKind.Int, "game TCP port (default 2006)", "26006", 0, Port, ["port"]),
        new("Original Gameserver.cfg keys", "network-port-listen", OptionKind.Int, "UDP port the clients listen on for LAN announcements (default 2005)", "26005", 0, Port, ["listen-port"]),
        new("Original Gameserver.cfg keys", "network-password", OptionKind.Text, "game password (empty = none)", "secret", Aliases: ["password"]),
        new("Original Gameserver.cfg keys", "network-preconfig-mode", OptionKind.Int, "game mode: 0 none, 1 free, 2 player killer, 3 campaign, 4 underworld", "1", 0, 4),
        new("Original Gameserver.cfg keys", "network-preconfig-level", OptionKind.Int, "difficulty 0..5", "2", 0, 5),
        new("Original Gameserver.cfg keys", "network-preconfig-maxplayers", OptionKind.Int, "maximum players 1..16", "8", 1, 16),
        new("Original Gameserver.cfg keys", "ladder-export", OptionKind.Flag, "ladder export (accepted, not used)", "0"),
        new("Original Gameserver.cfg keys", "network-lobby", OptionKind.Text, "register the game at this lobby host (Open/Closed Internet; empty = none)", "lobby.example.org", Aliases: ["lobby-host"]),
        new("Original Gameserver.cfg keys", "network-lobbyport", OptionKind.Int, "lobby TCP port (default 7066)", "17066", 0, Port),
        new("Original Gameserver.cfg keys", "network-internet", OptionKind.Int, "accepted for Gameserver.cfg compatibility, not used", "0", 0, 2, Ignored: true),
        new("Original Gameserver.cfg keys", "network-ip-address", OptionKind.Text, "accepted for Gameserver.cfg compatibility, not used (see --public-address)", "10.0.0.1", Ignored: true),
        new("Original Gameserver.cfg keys", "network-noconfirmation", OptionKind.Int, "accepted for Gameserver.cfg compatibility, not used", "0", 0, 1, Ignored: true),
        new("Original Gameserver.cfg keys", "network-playerdata", OptionKind.Int, "accepted for Gameserver.cfg compatibility, not used", "1", 0, 1, Ignored: true),
        new("Original Gameserver.cfg keys", "network-session", OptionKind.Text, "accepted for Gameserver.cfg compatibility, not used (see --server-name)", "Realm", Ignored: true),

        // ---- data and identity ----
        new("Game data and identity", "game-dir", OptionKind.Path, "directory with Sacred.exe, tincat2.dll and the Pak/Bin data (required unless --accept-any-data)", "/srv/sacred", Aliases: ["game-directory"]),
        new("Game data and identity", "accept-any-data", OptionKind.Flag, "do not verify client version/data fingerprints (development only)", "1"),
        new("Game data and identity", "announce-version", OptionKind.Int, "client version to announce when no game data is read", "56504", 0, ushort.MaxValue, ["version"]),
        new("Game data and identity", "server-name", OptionKind.Text, "session name shown in the game list", "My realm", Aliases: ["name"]),
        new("Game data and identity", "public-address", OptionKind.Text, "IPv4 address to advertise (LAN announcement, lobby)", "203.0.113.5"),
        new("Game data and identity", "config", OptionKind.Path, "Gameserver.cfg path (default: the working or game directory)", "Gameserver.cfg"),

        // ---- networking ----
        new("Networking", "udp-port", OptionKind.Int, "Sacred Bild UDP transport (KCP) port (default 2105); 0 turns it off", "26105", 0, Port, ["network-port-udp"]),
        new("Networking", "lan-announce", OptionKind.Flag, "broadcast the game on the LAN (default 1)", "0"),
        new("Networking", "tick-ms", OptionKind.Int, "game loop tick in milliseconds, 5..100 (default 20)", "20", 5, 100),
        new("Networking", "max-password-tries", OptionKind.Int, "wrong passwords before a player is kicked, 1..100 (default 3)", "3", 1, 100),
        new("Networking", "max-connections-per-address", OptionKind.Int, "simultaneous connections per client address, 0 = no limit (default 8)", "8", 0),
        new("Networking", "trace", OptionKind.Flag, "log every received and sent message (opcode, sender, origin, first 32 bytes)", "1"),

        // ---- lobby ----
        new("Lobby", "lobby-channel", OptionKind.Int, "lobby channel of the game (GameServer.exe uses 5)", "5", 0, 127),
        new("Lobby", "closed-net", OptionKind.Flag, "the lobby is a Closed Internet lobby: fetch every player's hero from it (implied for channels other than 5)", "1"),

        // ---- authoritative mode ----
        new("Authoritative mode", "authoritative", OptionKind.Flag, "server-owned state, validated client values, hero saves (also turns flood protection on)", "1"),
        new("Authoritative mode", "auth-max-damage", OptionKind.Int, "largest damage value a client may report", "100000"),
        new("Authoritative mode", "auth-strict-attackers", OptionKind.Flag, "refuse damage from creatures the sender does not own", "1"),
        new("Authoritative mode", "auth-max-violations", OptionKind.Int, "violations before a player is kicked (0 = never)", "20"),
        new("Authoritative mode", "auth-lock-difficulty", OptionKind.Flag, "keep the session's difficulty fixed", "1"),
        new("Authoritative mode", "auth-save-interval", OptionKind.Int, "seconds between periodic hero saves (0 = off)", "60"),
        new("Authoritative mode", "auth-save-debounce-ms", OptionKind.Int, "milliseconds to wait after a trade or drop before saving", "500"),

        // ---- flood protection ----
        new("Flood protection", "flood-protection", OptionKind.Flag, "1/0 overrides the default (on with --authoritative)", "1"),
        new("Flood protection", "flood-messages-per-second", OptionKind.Int, "sustained messages per second per player", "600", 1),
        new("Flood protection", "flood-message-burst", OptionKind.Int, "message burst per player", "3000", 1),
        new("Flood protection", "flood-bytes-per-second", OptionKind.Int, "sustained received bytes per second per player", "2097152", 1),
        new("Flood protection", "flood-byte-burst", OptionKind.Int, "received byte burst per player", "8388608", 1),
        new("Flood protection", "flood-chat-per-second", OptionKind.Int, "sustained chat messages per second per player", "3", 1),
        new("Flood protection", "flood-chat-burst", OptionKind.Int, "chat burst per player", "8", 1),
        new("Flood protection", "flood-kick-after-drops", OptionKind.Int, "dropped messages within the strike window before a kick (0 = never kick)", "500"),
        new("Flood protection", "flood-strike-window", OptionKind.Int, "strike window in seconds", "10", 1),
        new("Flood protection", "flood-upload-grace", OptionKind.Int, "seconds of relaxed limits while a player uploads its world", "60"),

        // ---- web UI (value may also follow as the next argument; the same keys exist as --WebUi:Port=..., webui.json, SACRED_GS_WebUi__Port) ----
        new("Web UI", "webserver", OptionKind.Flag, "start the management web UI", "1", WebStyle: true),
        new("Web UI", "webui-enabled", OptionKind.Flag, "same as --webserver", "1", WebStyle: true),
        new("Web UI", "webui-hostname", OptionKind.Text, "address to bind (default 127.0.0.1)", "127.0.0.1", WebStyle: true),
        new("Web UI", "webui-port", OptionKind.Int, "web UI port (default 8080)", "18080", 0, Port, WebStyle: true),
        new("Web UI", "webui-password", OptionKind.Text, "HTTP Basic / Bearer password (required when not bound to loopback)", "secret", WebStyle: true),
        new("Web UI", "webui-cors-origins", OptionKind.Text, "origins allowed to call the JSON API (comma separated)", "https://example.org", WebStyle: true),
        new("Web UI", "webui-frame-ancestors", OptionKind.Text, "who may frame the page: none, self, * or origins", "self", WebStyle: true),
        new("Web UI", "webui-allowed-hosts", OptionKind.Text, "accepted Host headers (comma separated)", "localhost", WebStyle: true),
        new("Web UI", "webui-path-base", OptionKind.Text, "URL prefix behind a reverse proxy", "/sacred", WebStyle: true),
        new("Web UI", "webui-config", OptionKind.Path, "web UI settings file (default webui.json)", "webui.json", WebStyle: true),
    ];

    /// <summary>Section keys of the <c>--WebUi:Key=value</c> spelling.</summary>
    private static readonly string[] WebSectionKeys = ["Enabled", "Hostname", "Port", "Password", "CorsOrigins", "FrameAncestors", "AllowedHosts", "PathBase"];

    private static readonly Dictionary<string, OptionSpec> ByName = BuildIndex();

    private static Dictionary<string, OptionSpec> BuildIndex()
    {
        var map = new Dictionary<string, OptionSpec>(StringComparer.Ordinal);
        foreach (var spec in Options) foreach (var name in spec.AllNames()) map.Add(name, spec);
        return map;
    }

    public static OptionSpec? Find(string name) => ByName.GetValueOrDefault(Normalize(name));

    private static string Normalize(string name) => name.Replace('_', '-').ToLowerInvariant();

    /// <summary>
    /// Checks the arguments. Returns null when they are fine, else the complete error message (one or two lines) for the console.
    /// A bare <c>quiet</c> (the original GameServer.exe's switch for "no dialog") is accepted. Nothing is bound or read here.
    /// </summary>
    public static string? Validate(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("quiet", StringComparison.OrdinalIgnoreCase) || arg is "-h" or "--help" or "-?" or "/?") continue;
            if (!arg.StartsWith("--", StringComparison.Ordinal))
                return $"ERROR: unexpected argument '{arg}'. Options look like --name=value; see --help.";
            var body = arg[2..];

            if (body.StartsWith("webui:", StringComparison.OrdinalIgnoreCase))                 // --WebUi:Port=8080 / --WebUi:Port 8080
            {
                var eq = body.IndexOf('=');
                var key = (eq < 0 ? body : body[..eq])["webui:".Length..];
                var first = key.Split(':')[0];
                if (!WebSectionKeys.Contains(first, StringComparer.OrdinalIgnoreCase))
                    return Unknown(arg, "--webui-" + first.ToLowerInvariant());
                if (eq < 0 && i + 1 < args.Length && !args[i + 1].StartsWith('-')) i++;
                continue;
            }

            var cut = body.IndexOfAny(['=', ':']);
            var name = cut < 0 ? body : body[..cut];
            var hasValue = cut >= 0;
            var value = hasValue ? body[(cut + 1)..] : "";
            var spec = Find(name);
            if (spec is null) return Unknown(arg, name);
            if (!hasValue && spec.WebStyle && spec.Kind != OptionKind.Flag && i + 1 < args.Length && !args[i + 1].StartsWith('-')) { value = args[++i]; hasValue = true; }
            else if (!hasValue && spec.WebStyle && spec.Kind == OptionKind.Flag && i + 1 < args.Length && args[i + 1] is "true" or "false" or "0" or "1") value = args[++i];
            var problem = CheckValue(spec, hasValue, value);
            if (problem is not null) return $"ERROR: option '--{name}' {problem}";
        }
        return null;
    }

    private static string? CheckValue(OptionSpec spec, bool hasValue, string value)
    {
        switch (spec.Kind)
        {
            case OptionKind.Flag:
                if (!hasValue || value.Length == 0) return null;                                // bare switch = on
                if (spec.WebStyle && value.Equals("true", StringComparison.OrdinalIgnoreCase)) return null;
                if (spec.WebStyle && value.Equals("false", StringComparison.OrdinalIgnoreCase)) return null;
                return value is "0" or "1" ? null : $"takes 0 or 1, not '{value}'.";
            case OptionKind.Int:
                if (!hasValue) return "needs a value, for example --" + spec.Name + "=" + spec.Sample + ".";
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) return $"needs a number, not '{value}'.";
                return number < spec.Min || number > spec.Max ? $"must be between {spec.Min} and {spec.Max}, not {number}." : null;
            case OptionKind.Path:
                return hasValue && value.Length > 0 ? null : "needs a value, for example --" + spec.Name + "=" + spec.Sample + ".";
            default:
                return hasValue ? null : "needs a value, for example --" + spec.Name + "=" + spec.Sample + ".";
        }
    }

    private static string Unknown(string arg, string name)
    {
        var shown = arg.Contains('=') ? arg[..arg.IndexOf('=')] : arg;
        var text = $"ERROR: unknown option '{shown}'";
        return Suggest(name) is { } near ? $"{text}; did you mean --{near}?" : text + "; see --help.";
    }

    /// <summary>The closest known option: one whose name parts contain all parts of the typo (tcp-port finds network-port-tcp), else the smallest edit distance.</summary>
    public static string? Suggest(string typo)
    {
        var wanted = Normalize(typo);
        if (wanted.Length == 0) return null;
        var parts = wanted.Split('-', StringSplitOptions.RemoveEmptyEntries);
        string? best = null;
        foreach (var spec in Options)
        {
            var have = spec.Name.Split('-');
            if (parts.Length > 0 && parts.All(p => have.Contains(p)) && (best is null || spec.Name.Length < best.Length)) best = spec.Name;
        }
        if (best is not null) return best;
        var distance = int.MaxValue;
        foreach (var name in ByName.Keys)
        {
            var d = Distance(wanted, name);
            if (d < distance) { distance = d; best = ByName[name].Name; }
        }
        return distance <= Math.Max(2, wanted.Length / 3) ? best : null;
    }

    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }

    /// <summary>The <c>--help</c> text, generated from <see cref="Options"/>.</summary>
    public static string HelpText()
    {
        var text = new StringBuilder();
        text.AppendLine("SacredGameServer - headless drop-in replacement for Sacred's GameServer.exe");
        text.AppendLine();
        text.AppendLine("Reads Gameserver.cfg (the original's file, KEY:VALUE; unknown keys are ignored) and then the command line, which is strict:");
        text.AppendLine("an unknown option or a bad value is refused (exit code 2) before anything is bound. A bare 'quiet' is accepted for GameServer.exe");
        text.AppendLine("compatibility. Options are --name=value (the same keys as in Gameserver.cfg, '-' or '_', any case).");
        foreach (var group in Options.Select(o => o.Group).Distinct())
        {
            text.AppendLine();
            text.AppendLine(group + ":");
            foreach (var spec in Options.Where(o => o.Group == group))
            {
                var usage = spec.Kind == OptionKind.Flag ? $"--{spec.Name}" : $"--{spec.Name}=VALUE";
                var alias = spec.Aliases is { Length: > 0 } ? $" (also {string.Join(", ", spec.Aliases.Select(a => "--" + a))})" : "";
                text.AppendLine($"  {usage,-38} {spec.Help}{alias}");
            }
        }
        text.AppendLine();
        text.AppendLine("  --help, -h                             this text");
        text.AppendLine("  quiet                                  accepted and ignored (GameServer.exe: start without the dialog)");
        return text.ToString();
    }
}
