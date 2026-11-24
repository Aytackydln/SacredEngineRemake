using Sacred.GameServer.Web;

namespace Sacred.GameServer.Session;

/// <summary>
/// Sector ownership for the admin map (<see cref="IServerAdmin.SectorOwnership"/>): about four times a second the game loop copies the
/// sector table (owner, players inside, initialized) and the players' sector and position into an immutable snapshot that the web
/// thread reads under a short lock, and only while a web page asks for it. The version only moves when the content changes, so the SSE stream sends nothing while the world rests.
/// Positions are the hero's last state the server mirrored (a position is only shown while it lies in the sector the client reported).
/// </summary>
public sealed partial class GameServerHost
{
    private const int SectorViewIntervalMs = 250;
    private readonly object _sectorViewGate = new();
    private SectorOwnership _sectorView = new(0, 0, [], []);
    private long _nextSectorViewMs;
    private int _sectorViewHash;

    private long _sectorViewWantedUntilMs;

    /// <summary>The newest snapshot. Calling it tells the game loop that somebody watches: it keeps the snapshot fresh for the next few seconds
    /// (and builds nothing, allocates nothing, while no page is open).</summary>
    public SectorOwnership SectorOwnership()
    {
        Volatile.Write(ref _sectorViewWantedUntilMs, _clock.ElapsedMilliseconds + 5000);
        lock (_sectorViewGate) return _sectorView;
    }

    private void PumpSectorView(long now)
    {
        if (now < _nextSectorViewMs || now > Volatile.Read(ref _sectorViewWantedUntilMs)) return;
        _nextSectorViewMs = now + SectorViewIntervalMs;
        var sectors = new List<SectorOwnerInfo>();
        var owned = new int[MaxSlots + 1];
        for (var id = 1; id < SectorTableSize; id++)
        {
            var owner = _sectorOwner[id];
            var mask = _sectorMask[id];
            if (owner == 0 && mask == 0) continue;
            var players = new List<int>(2);
            for (var slot = 1; slot <= MaxSlots; slot++) if ((mask & Bit((byte)slot)) != 0) players.Add(slot);
            if (owner is >= 1 and <= MaxSlots) owned[owner]++;
            sectors.Add(new SectorOwnerInfo(id, owner, players.ToArray(), _sectorInitialized[id]));
        }
        var people = new List<SectorPlayerInfo>(PlayerCount);
        for (var slot = 1; slot <= MaxSlots; slot++)
        {
            if (_slots[slot] is not { } p || p.State < SessionState.SlotAssigned) continue;
            int x = 0, y = 0;
            var has = false;
            if (p.CurrentSector != 0 && _world.TryGet(p.Slot, out var hero))
            {
                var (sector, hx, hy) = Position(hero);
                if (sector == p.CurrentSector && (hx != 0 || hy != 0)) { x = hx; y = hy; has = true; }
            }
            people.Add(new SectorPlayerInfo(slot, CharacterName(p), p.InGame, p.CurrentSector, x, y, has, owned[slot]));
        }
        var hash = new HashCode();
        foreach (var s in sectors) { hash.Add(s.Id); hash.Add(s.Owner); hash.Add(s.Initialized); foreach (var q in s.Players) hash.Add(q); hash.Add(-1); }
        foreach (var q in people) { hash.Add(q.Slot); hash.Add(q.Name); hash.Add(q.InGame); hash.Add(q.Sector); hash.Add(q.X); hash.Add(q.Y); hash.Add(q.HasPosition); hash.Add(q.Owned); }
        var value = hash.ToHashCode();
        lock (_sectorViewGate)
        {
            if (value == _sectorViewHash && _sectorView.Version != 0) return;
            _sectorViewHash = value;
            _sectorView = new SectorOwnership(_sectorView.Version + 1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sectors.ToArray(), people.ToArray());
        }
    }
}
