namespace Sacred.GameServer.Simulation;

/// <summary>What the server knows about an item a client is creating: from the 0x16F/0x16E state before it enters the world.</summary>
public readonly record struct ItemFacts(uint Reference, uint Type, int Level, int Rarity, ushort Sector, int X, int Y, ulong ContentHash, bool Purchase = false, uint Price = 0)
{
    public bool HasPosition => X != 0 || Y != 0;
}

/// <summary>
/// The item provenance witness (docs/authoritative-world-plan.md, M1): remembers the events that legitimately create items on a
/// client (kills, opened containers, completed trades, destroyed items that may come back, sectors taken over) and explains new
/// item objects with them. Pure and deterministic (time is passed in), fixed-size rings, no allocation after construction, so the
/// game loop can call it for every creation. The host (GameServerHost.Witness.cs) feeds it; tests drive it directly.
/// <para>What it cannot know: luck. A client that owns a sector rolls the loot itself, so the witness bounds outcomes (where,
/// when, how many, how strong) instead of reproducing them. Server-owned sectors (M6) close that gap.</para>
/// </summary>
public sealed class ItemWitness
{
    public const int MaxSlots = 16;
    private const int KillRing = 256, ContainerRing = 64, PerSlotRing = 128, SectorRing = 8, LateRing = 64;

    private struct Kill { public uint Reference; public ushort Sector; public int X, Y, Level, Left; public long AtMs; }
    private struct Container { public uint Reference; public ushort Sector; public int X, Y, Left; public byte Opener; public long AtMs; }
    private struct Receivable { public ulong Hash; public long AtMs; }
    private struct Remnant { public ulong Hash; public uint Type; public long AtMs; public bool Data; }
    private struct SectorGrant { public ushort Sector; public long AtMs; }
    private struct Late { public byte Slot; public uint Reference; public ushort Sector; public int X, Y; public long AtMs; }

    private readonly WitnessOptions _options;
    private readonly Kill[] _kills = new Kill[KillRing];
    private int _killNext;
    private readonly Container[] _containers = new Container[ContainerRing];
    private int _containerNext;
    private readonly Receivable[][] _received = new Receivable[MaxSlots + 1][];
    private readonly int[] _receivedNext = new int[MaxSlots + 1];
    private readonly Remnant[][] _remnants = new Remnant[MaxSlots + 1][];
    private readonly int[] _remnantNext = new int[MaxSlots + 1];
    private readonly SectorGrant[][] _sectors = new SectorGrant[MaxSlots + 1][];
    private readonly int[] _sectorNext = new int[MaxSlots + 1];
    private readonly Late[] _late = new Late[LateRing];
    private int _lateNext;
    private readonly bool[] _scratchUsed = new bool[PerSlotRing];
    private readonly long[] _worldBegin = new long[MaxSlots + 1], _worldEnd = new long[MaxSlots + 1];
    public const int StockRing = 256;
    public const int WorldStartRing = 512;
    private readonly uint[][] _worldItems = new uint[MaxSlots + 1][];
    private readonly int[] _worldItemNext = new int[MaxSlots + 1];
    private struct Stocked { public uint Reference; public uint Price; }
    private readonly Stocked[][] _stock = new Stocked[MaxSlots + 1][];
    private readonly int[] _stockNext = new int[MaxSlots + 1];
    private readonly long[] _questAt = new long[MaxSlots + 1];
    private readonly int[] _questLeft = new int[MaxSlots + 1];
    public const int PurchaseRing = 16;
    private struct Bought { public uint Reference; public uint Price; public long AtMs; }
    private readonly Bought[][] _bought = new Bought[MaxSlots + 1][];

    public ItemWitness(WitnessOptions options)
    {
        _options = options;
        for (var i = 0; i <= MaxSlots; i++)
        {
            _received[i] = new Receivable[PerSlotRing];
            _remnants[i] = new Remnant[PerSlotRing];
            _sectors[i] = new SectorGrant[SectorRing];
            _bought[i] = new Bought[PurchaseRing];
            _stock[i] = new Stocked[StockRing];
            _worldItems[i] = new uint[WorldStartRing];
        }
    }

    /// <summary>Counts per source of the explained creations, and the unexplained ones (index 0).</summary>
    public readonly int[] Explained = new int[16];
    /// <summary>Unexplained creations that a kill or container seen afterwards would have explained (the event came after the item).</summary>
    public int ExplainedLate { get; private set; }

    // ---- events -----------------------------------------------------------------------------------------------------

    /// <summary>A creature died (0x17D). Unknown positions explain nothing (the creature was never seen).</summary>
    public void OnKill(uint reference, ushort sector, int x, int y, int level, long now)
    {
        now++;                                                              // 0 marks an empty ring entry
        for (var i = 0; i < KillRing; i++) if (_kills[i].Reference == reference && _kills[i].AtMs != 0 && now - _kills[i].AtMs <= _options.LootWindowMs) return;
        ref var k = ref _kills[_killNext];
        _killNext = (_killNext + 1) % KillRing;
        k = new Kill { Reference = reference, Sector = sector, X = x, Y = y, Level = level, Left = _options.LootPerKill, AtMs = now };
        LateMatch(x, y, _options.LootRange, now, _options.LootWindowMs);
    }

    /// <summary>A container was opened (0x18C) by <paramref name="opener"/>.</summary>
    public void OnContainer(uint reference, byte opener, ushort sector, int x, int y, long now)
    {
        now++;                                                              // 0 marks an empty ring entry
        for (var i = 0; i < ContainerRing; i++) if (_containers[i].Reference == reference && _containers[i].AtMs != 0) return;   // once per container
        ref var c = ref _containers[_containerNext];
        _containerNext = (_containerNext + 1) % ContainerRing;
        c = new Container { Reference = reference, Opener = opener, Sector = sector, X = x, Y = y, Left = _options.ContainerPerOpen, AtMs = now };
        LateMatch(x, y, _options.ContainerRange, now, _options.ContainerWindowMs);
    }

    /// <summary>A completed trade gave <paramref name="receiver"/> an item with this content.</summary>
    public void OnTradeReceive(byte receiver, ulong contentHash, long now)
    {
        now++;                                                              // 0 marks an empty ring entry
        if (receiver is 0 or > MaxSlots) return;
        var ring = _received[receiver];
        ring[_receivedNext[receiver]] = new Receivable { Hash = contentHash, AtMs = now };
        _receivedNext[receiver] = (_receivedNext[receiver] + 1) % PerSlotRing;
    }

    /// <summary>The player destroyed one of its items (0x167): its value records (and its data block) may come back.</summary>
    public void OnDestroyed(byte slot, uint type, ReadOnlySpan<byte> content, long now)
    {
        now++;                                                              // 0 marks an empty ring entry
        if (slot is 0 or > MaxSlots || content.Length < RecordBytes) return;
        var records = content.Length / RecordBytes - 1;
        for (var i = 0; i < records; i++) AddRemnant(slot, Fnv(content.Slice(i * RecordBytes, RecordBytes)), type, now, data: false);
        AddRemnant(slot, Fnv(content.Slice(records * RecordBytes, RecordBytes)), type, now, data: true);
    }

    /// <summary>The player took over a sector (0x020): its ground items are streamed by that player now.</summary>
    public void OnSectorOwned(byte slot, ushort sector, long now)
    {
        now++;                                                              // 0 marks an empty ring entry
        if (slot is 0 or > MaxSlots || sector == 0) return;
        _sectors[slot][_sectorNext[slot]] = new SectorGrant { Sector = sector, AtMs = now };
        _sectorNext[slot] = (_sectorNext[slot] + 1) % SectorRing;
    }

    /// <summary>The player's client began its world upload (0x016) before it was in the game: it builds the world around the hero now.
    /// Only the first begin of a session counts (the host calls this with the player not yet in the game).</summary>
    public void OnWorldStart(byte slot, long now)
    {
        if (slot is 0 or > MaxSlots) return;
        _worldBegin[slot] = now + 1;                                        // 0 marks "none"
        _worldEnd[slot] = 0;
    }

    /// <summary>The world start (0x017/0x018) of the player: the window closes after <see cref="WitnessOptions.WorldStartGraceMs"/>.</summary>
    public void OnWorldEntered(byte slot, long now)
    {
        if (slot is 0 or > MaxSlots || _worldBegin[slot] == 0 || _worldEnd[slot] != 0) return;
        _worldEnd[slot] = now + 1;
    }

    /// <summary>An item the world start source explained (created while the client builds the world): remembered, so that one of them that is put into
    /// the hero's own inventory while the window is still open can be told from the hero's baseline items (those are explained earlier, as Baseline).</summary>
    public void OnWorldStartItem(byte slot, uint reference)
    {
        if (slot is 0 or > MaxSlots) return;
        _worldItems[slot][_worldItemNext[slot]] = reference;
        _worldItemNext[slot] = (_worldItemNext[slot] + 1) % WorldStartRing;
    }

    /// <summary>The item was put into the hero's inventory: true when it is a remembered world start item and the window is still open (a world
    /// object of the first sectors is not picked up by the hero while the world is being built).</summary>
    public bool WorldStartItemAnchored(byte slot, uint reference, long now)
    {
        if (slot is 0 or > MaxSlots || reference == 0) return false;
        var ring = _worldItems[slot];
        for (var i = 0; i < WorldStartRing; i++)
            if (ring[i] == reference) { ring[i] = 0; return InWorldStart(slot, now + 1); }
        return false;
    }

    /// <summary>A stock-like item (no position, nobody holds it) was created by the player's client: remembered until it enters a hero.</summary>
    public void OnStock(byte slot, uint reference, uint price)
    {
        if (slot is 0 or > MaxSlots) return;
        _stock[slot][_stockNext[slot]] = new Stocked { Reference = reference, Price = price };
        _stockNext[slot] = (_stockNext[slot] + 1) % StockRing;
    }

    /// <summary>The item entered a hero of this player: true (once) when it was remembered stock; its price is returned for the payment check.</summary>
    public bool TakeStock(byte slot, uint reference, out uint price)
    {
        price = 0;
        if (slot is 0 or > MaxSlots || reference == 0) return false;
        var ring = _stock[slot];
        for (var i = 0; i < StockRing; i++)
            if (ring[i].Reference == reference) { price = ring[i].Price; ring[i] = default; return true; }
        return false;
    }

    /// <summary>The player's client reported a quest event (0x145 quest pair, 0x1B8..0x1BE): it may create the quest's reward items now.</summary>
    public void OnQuestEvent(byte slot, long now)
    {
        if (slot is 0 or > MaxSlots) return;
        _questAt[slot] = now + 1;
        _questLeft[slot] = _options.QuestPerEvent;
    }

    private bool TakeQuest(byte slot, long now)
    {
        if (_questAt[slot] == 0 || _questLeft[slot] <= 0 || now - _questAt[slot] > _options.QuestWindowMs) return false;
        _questLeft[slot]--;
        return true;
    }

    /// <summary>A purchase was accepted provisionally: the hero gold must drop by its price soon.</summary>
    public void OnPurchase(byte slot, uint reference, uint price, long now)
    {
        if (slot is 0 or > MaxSlots) return;
        var ring = _bought[slot];
        var at = 0;
        for (var i = 0; i < PurchaseRing; i++) { if (ring[i].AtMs == 0) { at = i; break; } if (ring[i].AtMs < ring[at].AtMs) at = i; }
        ring[at] = new Bought { Reference = reference, Price = price, AtMs = now + 1 };
    }

    /// <summary>The hero gold dropped by <paramref name="amount"/>: pays for pending purchases (oldest first, each at least
    /// <see cref="WitnessOptions.PurchasePercent"/> percent of its price).</summary>
    public void OnGoldSpent(byte slot, long amount, long now)
    {
        if (slot is 0 or > MaxSlots) return;
        var ring = _bought[slot];
        while (amount > 0)
        {
            var best = -1;
            for (var i = 0; i < PurchaseRing; i++)
                if (ring[i].AtMs != 0 && amount * 100 >= (long)ring[i].Price * _options.PurchasePercent && (best < 0 || ring[i].AtMs < ring[best].AtMs)) best = i;
            if (best < 0) return;
            amount -= Math.Max(1, (long)ring[best].Price * _options.PurchasePercent / 100);
            ring[best] = default;
        }
    }

    /// <summary>Purchases nobody paid for within <see cref="WitnessOptions.PurchaseWindowMs"/>: their references go into
    /// <paramref name="refs"/> (at most its length) and are forgotten.</summary>
    public int TakeUnpaid(byte slot, long now, Span<uint> refs)
    {
        if (slot is 0 or > MaxSlots) return 0;
        var ring = _bought[slot];
        var n = 0;
        for (var i = 0; i < PurchaseRing && n < refs.Length; i++)
            if (ring[i].AtMs != 0 && now + 1 - ring[i].AtMs > _options.PurchaseWindowMs) { refs[n++] = ring[i].Reference; ring[i] = default; }
        return n;
    }

    /// <summary>A player left: its own rings are cleared (a new player in the slot starts empty).</summary>
    public void Forget(byte slot)
    {
        if (slot is 0 or > MaxSlots) return;
        Array.Clear(_received[slot]); Array.Clear(_remnants[slot]); Array.Clear(_sectors[slot]);
        _worldBegin[slot] = _worldEnd[slot] = 0;
        Array.Clear(_bought[slot]);
        _questAt[slot] = 0; _questLeft[slot] = 0;
        Array.Clear(_stock[slot]); _stockNext[slot] = 0;
        Array.Clear(_worldItems[slot]); _worldItemNext[slot] = 0;
        _receivedNext[slot] = _remnantNext[slot] = _sectorNext[slot] = 0;
    }

    // ---- classification ---------------------------------------------------------------------------------------------

    public const int RecordBytes = 130;

    /// <summary>Explains a creation by <paramref name="slot"/> and consumes what explained it (a loot budget, a received item,
    /// destroyed records). <paramref name="content"/> is the item's value records followed by its 130-byte data block.
    /// Unexplained creations are remembered for a late match (diagnostics only).</summary>
    public ItemSource Classify(byte slot, in ItemFacts item, ReadOnlySpan<byte> content, long now)
    {
        now++;                                                              // 0 marks an empty ring entry
        var source = ClassifyCore(slot, item, content, now);
        Explained[(int)source]++;
        if (source == ItemSource.Unexplained && item.HasPosition)
        {
            _late[_lateNext] = new Late { Slot = slot, Reference = item.Reference, Sector = item.Sector, X = item.X, Y = item.Y, AtMs = now };
            _lateNext = (_lateNext + 1) % LateRing;
        }
        return source;
    }

    private ItemSource ClassifyCore(byte slot, in ItemFacts item, ReadOnlySpan<byte> content, long now)
    {
        if (slot is 0 or > MaxSlots) return ItemSource.Unexplained;
        if (_options.Accepts(ItemSource.Trade) && TakeReceived(slot, item.ContentHash, now)) return ItemSource.Trade;
        if (_options.Accepts(ItemSource.Rebuild) && TakeRemnants(slot, item.Type, content, now)) return ItemSource.Rebuild;
        if (item.HasPosition)
        {
            if (_options.Accepts(ItemSource.Kill) && TakeKill(item, now)) return ItemSource.Kill;
            if (_options.Accepts(ItemSource.Container) && TakeContainer(item, now)) return ItemSource.Container;
            if (_options.Accepts(ItemSource.Sector) && InOwnedSector(slot, item.Sector, now)) return ItemSource.Sector;
        }
        if (_options.Accepts(ItemSource.WorldStart) && InWorldStart(slot, now)) return ItemSource.WorldStart;
        if (_options.Accepts(ItemSource.Quest) && TakeQuest(slot, now)) return ItemSource.Quest;
        if (!item.HasPosition && !item.Purchase && _options.Accepts(ItemSource.Stock)) return ItemSource.Stock;     // judged when it enters a hero (TakeStock)
        if (item.Purchase && _options.Accepts(ItemSource.Purchase)) return ItemSource.Purchase;     // paid for later (OnGoldSpent / TakeUnpaid)
        return ItemSource.Unexplained;
    }

    private bool TakeReceived(byte slot, ulong hash, long now)
    {
        var ring = _received[slot];
        for (var i = 0; i < PerSlotRing; i++)
        {
            ref var r = ref ring[i];
            if (r.AtMs == 0 || r.Hash != hash || now - r.AtMs > _options.TradeWindowMs) continue;
            r = default;
            return true;
        }
        return false;
    }

    /// <summary>Every value record of the new item must be one the player destroyed, and its data block either one too or the data of
    /// a destroyed item of the same type (socketing changes the data block). Without records the data block must match exactly.</summary>
    private bool TakeRemnants(byte slot, uint type, ReadOnlySpan<byte> content, long now)
    {
        if (content.Length < RecordBytes || content.Length % RecordBytes != 0) return false;
        var ring = _remnants[slot];
        var used = _scratchUsed.AsSpan();
        used.Clear();
        var records = content.Length / RecordBytes - 1;
        for (var r = 0; r < records; r++)
        {
            var hash = Fnv(content.Slice(r * RecordBytes, RecordBytes));
            var at = Find(ring, used, hash, data: false, type: 0, now);
            if (at < 0) return false;
            used[at] = true;
        }
        var dataHash = Fnv(content.Slice(records * RecordBytes, RecordBytes));
        var exact = Find(ring, used, dataHash, data: true, type: 0, now);
        if (exact >= 0) used[exact] = true;
        else
        {
            if (records == 0) return false;
            var sameType = -1;
            for (var i = 0; i < PerSlotRing && sameType < 0; i++)
                if (!used[i] && ring[i].Data && ring[i].Type == type && Fresh(ring[i].AtMs, now, _options.RebuildWindowMs)) sameType = i;
            if (sameType < 0) return false;
            used[sameType] = true;
        }
        for (var i = 0; i < PerSlotRing; i++) if (used[i]) ring[i] = default;
        return true;
    }

    private int Find(Remnant[] ring, ReadOnlySpan<bool> used, ulong hash, bool data, uint type, long now)
    {
        for (var i = 0; i < PerSlotRing; i++)
        {
            ref var r = ref ring[i];
            if (!used[i] && r.AtMs != 0 && r.Hash == hash && r.Data == data && (type == 0 || r.Type == type) && Fresh(r.AtMs, now, _options.RebuildWindowMs)) return i;
        }
        return -1;
    }

    private bool TakeKill(in ItemFacts item, long now)
    {
        var best = -1; long bestDistance = long.MaxValue;
        for (var i = 0; i < KillRing; i++)
        {
            ref var k = ref _kills[i];
            if (k.AtMs == 0 || k.Left <= 0 || !Fresh(k.AtMs, now, _options.LootWindowMs) || (k.X == 0 && k.Y == 0)) continue;
            if (_options.LootLevelMargin > 0 && item.Level > k.Level + _options.LootLevelMargin) continue;
            var d = Distance2(k.X, k.Y, item.X, item.Y);
            if (d > (long)_options.LootRange * _options.LootRange || d >= bestDistance) continue;
            best = i; bestDistance = d;
        }
        if (best < 0) return false;
        _kills[best].Left--;
        return true;
    }

    private bool TakeContainer(in ItemFacts item, long now)
    {
        for (var i = 0; i < ContainerRing; i++)
        {
            ref var c = ref _containers[i];
            if (c.AtMs == 0 || c.Left <= 0 || !Fresh(c.AtMs, now, _options.ContainerWindowMs) || (c.X == 0 && c.Y == 0)) continue;
            if (Distance2(c.X, c.Y, item.X, item.Y) > (long)_options.ContainerRange * _options.ContainerRange) continue;
            c.Left--;
            return true;
        }
        return false;
    }

    /// <summary>Between the begin of the world upload and the world start plus the grace (or the maximum after the begin).</summary>
    private bool InWorldStart(byte slot, long now)
    {
        var begin = _worldBegin[slot];
        if (begin == 0 || now < begin) return false;
        if (_worldEnd[slot] != 0) return now - _worldEnd[slot] <= _options.WorldStartGraceMs;
        return now - begin <= _options.WorldStartMaxMs;
    }

    private bool InOwnedSector(byte slot, ushort sector, long now)
    {
        if (sector == 0) return false;
        foreach (var g in _sectors[slot]) if (g.Sector == sector && Fresh(g.AtMs, now, _options.SectorWindowMs)) return true;
        return false;
    }

    /// <summary>An event that came after unexplained items it would have explained: counted (and the items forgotten).</summary>
    private void LateMatch(int x, int y, int range, long now, int window)
    {
        if (x == 0 && y == 0) return;
        for (var i = 0; i < LateRing; i++)
        {
            ref var l = ref _late[i];
            if (l.AtMs == 0 || now - l.AtMs > window) continue;
            if (Distance2(l.X, l.Y, x, y) > (long)range * range) continue;
            ExplainedLate++;
            Log.Info($"AUTH: witness: the unexplained item 0x{l.Reference:X} of player {l.Slot} is near an event {now - l.AtMs} ms later (kill or container after the item).");
            l = default;
        }
    }

    private void AddRemnant(byte slot, ulong hash, uint type, long now, bool data)
    {
        _remnants[slot][_remnantNext[slot]] = new Remnant { Hash = hash, Type = type, AtMs = now, Data = data };
        _remnantNext[slot] = (_remnantNext[slot] + 1) % PerSlotRing;
    }

    private static bool Fresh(long at, long now, int window) => at != 0 && now - at <= window;

    private static long Distance2(int ax, int ay, int bx, int by)
    {
        long dx = ax - (long)bx, dy = ay - (long)by;
        return dx * dx + dy * dy;
    }

    /// <summary>FNV-1a 64 of a byte span.</summary>
    public static ulong Fnv(ReadOnlySpan<byte> data)
    {
        var hash = 14695981039346656037UL;
        foreach (var b in data) { hash ^= b; hash *= 1099511628211UL; }
        return hash;
    }
}
