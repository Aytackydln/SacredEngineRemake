using System.Text.Json;
using Sacred.GameServer.Web;

namespace Sacred.GameServer.Demo;

/// <summary>
/// Stub state for <c>--demo</c>: fake players, chat, bans and sector owners that the web UI shows instead of a live game, so any page can be
/// screenshotted without clients. Read from <c>--demo-file=&lt;json&gt;</c> or the built-in <c>Demo/demo-state.json</c> (an embedded resource).
/// Parsed by hand (no reflection) to stay Native AOT compatible. Sector owners are given as grid cells (x, y, width, height) because
/// sector ids differ per game version; <see cref="DemoAdmin"/> turns cells into ids with the map in use (the real one, or a made-up one
/// without --game-dir).
/// </summary>
public sealed class DemoState
{
    public string Name = "Demo Realm";
    public int MaxPlayers = 16;
    public List<DemoPlayer> Players = [];
    public List<DemoChat> Chat = [];
    public List<BanEntry> Bans = [];
    public List<DemoOwner> Owners = [];
    public List<DemoHeld> Unowned = [];

    public sealed record DemoPlayer(int Slot, string Name, int Class, int Level, long Xp, long Gold, string Address, uint Latency, double Online, double? AtX, double? AtY, int? SectorX, int? SectorY);
    public sealed record DemoChat(ChatKind Kind, int From, int To, string Text);
    public sealed record DemoOwner(int Slot, int X, int Y, int Width, int Height, int[] Also);
    public sealed record DemoHeld(int X, int Y, int[] Players);

    public static DemoState Default() => Parse(ReadDefault());

    private static string ReadDefault()
    {
        using var stream = typeof(DemoState).Assembly.GetManifestResourceStream("Sacred.GameServer.Demo.demo-state.json")
            ?? throw new InvalidOperationException("the built-in demo state is missing");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static DemoState Load(string? path) => path is null ? Default() : Parse(File.ReadAllText(path));

    public static DemoState Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        var s = new DemoState();
        if (root.TryGetProperty("name", out var n)) s.Name = n.GetString() ?? s.Name;
        if (root.TryGetProperty("maxPlayers", out var m)) s.MaxPlayers = Math.Clamp(m.GetInt32(), 1, 16);
        if (root.TryGetProperty("players", out var players))
        {
            var slot = 0;
            foreach (var p in players.EnumerateArray())
            {
                if (++slot > 16) break;
                var at = p.TryGetProperty("at", out var a) ? a : default;
                var cell = p.TryGetProperty("sector", out var c) ? c : default;
                s.Players.Add(new DemoPlayer(slot, Str(p, "name", "Player" + slot), Int(p, "class", 0), Int(p, "level", 1), Long(p, "xp", 0), Long(p, "gold", 0),
                    Str(p, "address", "127.0.0.1"), (uint)Int(p, "latency", 30), Dbl(p, "online", 600),
                    at.ValueKind == JsonValueKind.Array ? at[0].GetDouble() : null, at.ValueKind == JsonValueKind.Array ? at[1].GetDouble() : null,
                    cell.ValueKind == JsonValueKind.Array ? cell[0].GetInt32() : null, cell.ValueKind == JsonValueKind.Array ? cell[1].GetInt32() : null));
            }
        }
        if (root.TryGetProperty("chat", out var chat))
            foreach (var c in chat.EnumerateArray())
                s.Chat.Add(new DemoChat(KindOf(Str(c, "kind", "public")), Int(c, "from", 0), Int(c, "to", -1), Str(c, "text", "")));
        if (root.TryGetProperty("bans", out var bans))
            foreach (var b in bans.EnumerateArray())
                s.Bans.Add(new BanEntry(Str(b, "address", "?"), Str(b, "reason", ""), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (long)(Dbl(b, "ago", 3600) * 1000)));
        if (root.TryGetProperty("owners", out var owners))
            foreach (var o in owners.EnumerateArray())
            {
                var r = o.GetProperty("rect");
                s.Owners.Add(new DemoOwner(Int(o, "slot", 1), r[0].GetInt32(), r[1].GetInt32(), r[2].GetInt32(), r[3].GetInt32(), Ints(o, "also")));
            }
        if (root.TryGetProperty("unowned", out var unowned))
            foreach (var u in unowned.EnumerateArray())
            {
                var c = u.GetProperty("cell");
                s.Unowned.Add(new DemoHeld(c[0].GetInt32(), c[1].GetInt32(), Ints(u, "players")));
            }
        return s;
    }

    private static ChatKind KindOf(string kind) => kind.ToLowerInvariant() switch
    {
        "private" => ChatKind.Private, "broadcast" => ChatKind.Broadcast, "server" => ChatKind.ServerMessage, "system" => ChatKind.System, _ => ChatKind.Public,
    };
    private static string Str(JsonElement e, string name, string fallback) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
    private static int Int(JsonElement e, string name, int fallback) => e.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : fallback;
    private static long Long(JsonElement e, string name, long fallback) => e.TryGetProperty(name, out var v) && v.TryGetInt64(out var i) ? i : fallback;
    private static double Dbl(JsonElement e, string name, double fallback) => e.TryGetProperty(name, out var v) && v.TryGetDouble(out var d) ? d : fallback;
    private static int[] Ints(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetInt32()).ToArray() : [];
}
