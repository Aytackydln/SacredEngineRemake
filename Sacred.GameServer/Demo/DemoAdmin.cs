using System.Text.Json;
using Sacred.GameServer.Session;
using Sacred.GameServer.Web;

namespace Sacred.GameServer.Demo;

/// <summary>
/// <c>--demo</c>: the web UI's view of the server with the live game replaced by stub state (<see cref="DemoState"/>): fake players, heroes,
/// chat, bans and sector owners. Everything that is not game state (mods and their pages, mod storage, flood settings, the real map with
/// --game-dir) comes from the real host, so mod pages show whatever the scripts and <c>script-data</c> of the working directory hold.
/// Commands from the web UI do nothing except reloading mods. The server is bound to loopback only (see Program).
/// </summary>
public sealed class DemoAdmin : IServerAdmin
{
    private readonly GameServerHost _host;
    private readonly DemoState _state;
    private readonly long _startMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private readonly Lazy<(byte[] Json, byte[] Png)> _fakeMap = new(DemoMap.Build);
    private volatile SectorOwnership? _sectors;
    private readonly bool _realMap;

    public DemoAdmin(GameServerHost host, DemoState state, bool realMap)
    {
        _host = host; _state = state; _realMap = realMap;
        Task.Run(Resolve);
    }

    private async Task Resolve()
    {
        try
        {
            var map = await AreaMapRequest(false, CancellationToken.None).ConfigureAwait(false);
            if (map.Status != 200) return;
            using var doc = JsonDocument.Parse(map.Body);
            var cell = new Dictionary<(int, int), int>();
            foreach (var s in doc.RootElement.GetProperty("sectors").EnumerateArray()) cell[(s.GetProperty("x").GetInt32(), s.GetProperty("y").GetInt32())] = s.GetProperty("id").GetInt32();
            _sectors = Build(cell, doc.RootElement.TryGetProperty("tile", out var t) ? t.GetDouble() : 53.66563034057617);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or IOException) { Log.Info("Demo: sector owners not built: " + e.Message); }
    }

    private SectorOwnership Build(Dictionary<(int, int), int> cell, double tile)
    {
        var owners = new Dictionary<int, (int Owner, SortedSet<int> Players)>();
        foreach (var o in _state.Owners)
            for (var y = o.Y; y < o.Y + o.Height; y++)
                for (var x = o.X; x < o.X + o.Width; x++)
                    if (cell.TryGetValue((x, y), out var id)) owners[id] = (o.Slot, new SortedSet<int>([o.Slot, .. o.Also]));
        foreach (var h in _state.Unowned)
            if (cell.TryGetValue((h.X, h.Y), out var id)) owners[id] = (0, new SortedSet<int>(h.Players));
        var sectors = owners.OrderBy(k => k.Key).Select(k => new SectorOwnerInfo(k.Key, k.Value.Owner, k.Value.Players.ToArray(), true)).ToArray();
        var people = new List<SectorPlayerInfo>();
        foreach (var p in _state.Players)
        {
            int sector = 0, x = 0, y = 0;
            var has = false;
            if (p.AtX is { } ax && p.AtY is { } ay)
            {
                cell.TryGetValue(((int)Math.Floor(ax), (int)Math.Floor(ay)), out sector);
                x = (int)(ax * 64 * tile); y = (int)(ay * 64 * tile); has = true;
            }
            else if (p.SectorX is { } sx && p.SectorY is { } sy) cell.TryGetValue((sx, sy), out sector);
            people.Add(new SectorPlayerInfo(p.Slot, p.Name, true, sector, x, y, has, sectors.Count(s => s.Owner == p.Slot)));
        }
        return new SectorOwnership(1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sectors, people.ToArray());
    }

    public SectorOwnership SectorOwnership() => _sectors ?? new SectorOwnership(0, 0, [], []);

    public Task<BinaryResponse> AreaMapRequest(bool image, CancellationToken cancellationToken)
    {
        if (_realMap) return _host.AreaMapRequest(image, cancellationToken);
        var (json, png) = _fakeMap.Value;
        return Task.FromResult(image ? new BinaryResponse(200, "image/png", png) : new BinaryResponse(200, "application/json; charset=utf-8", json));
    }

    private double Uptime => (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _startMs) / 1000.0;

    public ServerOverview Overview()
    {
        var o = _host.Overview();
        return o with { Name = _state.Name, Players = _state.Players.Count, MaxPlayers = _state.MaxPlayers, UptimeSeconds = Uptime + 86400 * 3, WorldAvailable = true };
    }

    public IReadOnlyList<PlayerInfo> Players() =>
        _state.Players.Select(p => new PlayerInfo(p.Slot, p.Name, p.Address, "tcp", p.Slot == 1, true, true, true, p.Latency, p.Online)).ToArray();

    public IReadOnlyList<HeroInfo> Heroes() =>
        _state.Players.Select(p => new HeroInfo(p.Slot, p.Name, "demo", p.Class, p.Level, p.Xp, p.Gold, 0, [], new int[6], null, [], 0, [], [], [], true, p.Online)).ToArray();

    public long ChatHead => _state.Chat.Count;

    public IReadOnlyList<ChatEntry> ChatSince(long afterId, int max)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var list = new List<ChatEntry>();
        for (var i = (int)Math.Max(0, afterId); i < _state.Chat.Count && list.Count < max; i++)
        {
            var c = _state.Chat[i];
            var from = _state.Players.FirstOrDefault(p => p.Slot == c.From)?.Name ?? (c.Kind is ChatKind.System ? "" : "Server");
            list.Add(new ChatEntry(i + 1, now - (_state.Chat.Count - i) * 45_000, c.Kind, c.From, from, c.Kind == ChatKind.Private ? c.To : -1, c.Text));
        }
        return list;
    }

    public IReadOnlyList<BanEntry> Bans() => _state.Bans;
    public FloodSettings Flood() => _host.Flood();
    public IReadOnlyList<ModEndpointInfo> ModEndpoints() => _host.ModEndpoints();
    public IReadOnlyList<ModInfo> Mods() => ((IServerAdmin)_host).Mods();
    public Task<ModResponse> ModRequest(string mod, string path, IReadOnlyList<KeyValuePair<string, string>> query, bool publicOnly, CancellationToken cancellationToken) =>
        _host.ModRequest(mod, path, query, publicOnly, cancellationToken);
    public Task<ModResponse> ModStoreRequest(string mod, string? key, string? body, CancellationToken cancellationToken) => _host.ModStoreRequest(mod, key, body, cancellationToken);
    public bool Submit(in AdminCommand command) => command.Kind != AdminCommandKind.ReloadMods || _host.Submit(command);
    public CancellationToken StopRequested => ((IServerAdmin)_host).StopRequested;
}
