using System.Buffers.Binary;
using Sacred.Core.Pax.Hero.Inventory;
using Sacred.GameServer.Auth.Hero;
using Sacred.GameServer.Web;
using Sacred.Network.Gameplay;

namespace Sacred.GameServer.Session;

/// <summary>
/// Player data for the web UI and its public API (<see cref="IServerAdmin.Heroes"/>).
/// With <see cref="TrackHeroes"/> (set when the web UI runs) or authoritative mode the server keeps each player's hero state,
/// equipment changes and inventories (GameServerHost.HeroState.cs); about once a second the game loop turns them into immutable
/// <see cref="HeroInfo"/> records that the web thread reads under a short lock.
/// <para>Hero values (Gold 0x585C60 stream at body +445): the compact form (0x169/0x16C/0x16E/0x16F) has experience, skill ids and
/// levels, gold, level, remaining skill points and the six spent attribute points; the full form (0x16B) the whole sCharValHero
/// block with attributes and skill bonuses (Sacred.Core <c>SacredActorValuesLayout</c>).</para>
/// </summary>
public sealed partial class GameServerHost
{
    private const int HeroSnapshotIntervalMs = 1000;
    private readonly object _heroGate = new();
    private HeroInfo[] _heroSnapshot = [];
    private long _nextHeroSnapshotMs;

    /// <summary>Keep the players' hero state for <see cref="Heroes"/> even without authoritative mode (the web UI turns it on).</summary>
    public bool TrackHeroes { get; set; }

    /// <summary>The game's names for types, skills and combat arts (read from --game-dir at startup); null leaves names out.</summary>
    public Data.NameService? Names { get; set; }

    private bool KeepsHeroState => _config.Auth.Enabled || TrackHeroes;

    public IReadOnlyList<HeroInfo> Heroes() { lock (_heroGate) return _heroSnapshot; }

    /// <summary>The character part of the FIRSTCONTACT name ("account.character" on lobby servers).</summary>
    /// <remarks>Only lobby names carry the account prefix; a LAN name is the character name itself, dots included ("Mr.X").</remarks>
    private static string CharacterName(PlayerSession s) =>
        s.LobbyIdentity is not (0 or -1) && s.Name.IndexOf('.') is var dot and >= 0 && dot + 1 < s.Name.Length ? s.Name[(dot + 1)..] : s.Name;

    /// <summary>Admission done: remember when, tell the scripts. (Mods keep their own data, the server stores nothing for them.)</summary>
    private void OnPlayerJoined(PlayerSession s)
    {
        s.JoinedMs = _clock.ElapsedMilliseconds;
        if (_scripts is not null) ScriptPlayerEvent("join", s);
    }

    private void PumpHeroes(long now)
    {
        if (!KeepsHeroState || now < _nextHeroSnapshotMs) return;
        _nextHeroSnapshotMs = now + HeroSnapshotIntervalMs;
        var heroes = new List<HeroInfo>(PlayerCount);
        for (var slot = 1; slot <= MaxSlots; slot++)
            if (_slots[slot] is { } p && p.State >= SessionState.SlotAssigned) heroes.Add(HeroOf(p, now));
        lock (_heroGate) _heroSnapshot = heroes.ToArray();
    }

    private HeroInfo HeroOf(PlayerSession s, long now)
    {
        var live = _live[s.Slot];
        ReadOnlySpan<byte> body = default;
        var full = false;
        if (live is not null && (live.FullIsNewer || live.HeroBody.IsEmpty) && !live.HeroFullBody.IsEmpty) { body = live.HeroFullBody; full = true; }
        else if (live is not null && !live.HeroBody.IsEmpty) body = live.HeroBody;
        else if (_world.TryGet(s.Slot, out var mirrored)) body = mirrored.Payload[(mirrored.LongPrefix ? 24 : 8)..];

        long experience = 0, gold = 0; int level = 0, points = 0, combos = 0;
        var skills = new List<SkillInfo>(8); var arts = new List<ArtInfo>();
        var spent = new int[6]; int[]? attributes = null;
        var values = GoldCreatureStateLayout.PrefixSize;
        if (body.Length > values + GoldCreatureValuesLayout.CompactMinimumSize &&
            BinaryPrimitives.ReadUInt32LittleEndian(body[GoldCreatureStateLayout.MarkerOffset..]) == GoldCreatureStateLayout.Marker &&
            BinaryPrimitives.ReadUInt32LittleEndian(body[values..]) == GoldCreatureValuesLayout.Marker)
        {
            ReadOnlySpan<byte> ids, levels, bonuses = default, allocations;
            int listAt;
            if (full && body.Length >= values + GoldCreatureValuesLayout.FullMinimumSize)
            {
                var block = body.Slice(values + GoldCreatureValuesLayout.FullBlock08Offset, GoldCreatureValuesLayout.FullBlock08Size);
                experience = BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);
                attributes = new int[6];
                for (var i = 0; i < 6; i++) attributes[i] = BinaryPrimitives.ReadUInt16LittleEndian(block[(8 + i * 2)..]);
                allocations = block.Slice(20, 6); ids = block.Slice(28, 8); levels = block.Slice(36, 8); bonuses = block.Slice(44, 8);
                points = BinaryPrimitives.ReadUInt16LittleEndian(block[58..]);
                gold = BinaryPrimitives.ReadUInt32LittleEndian(block[62..]);
                level = BinaryPrimitives.ReadUInt16LittleEndian(block[78..]);
                listAt = values + GoldCreatureValuesLayout.FullCount22Offset;
            }
            else
            {
                experience = BinaryPrimitives.ReadUInt32LittleEndian(body[(values + GoldCreatureValuesLayout.CompactWord0COffset)..]);
                ids = body.Slice(values + GoldCreatureValuesLayout.CompactBlock24Offset, 8);
                levels = body.Slice(values + GoldCreatureValuesLayout.CompactBlock2COffset, 8);
                gold = BinaryPrimitives.ReadUInt32LittleEndian(body[(values + GoldCreatureValuesLayout.CompactWord46Offset)..]);
                level = BinaryPrimitives.ReadUInt16LittleEndian(body[(values + GoldCreatureValuesLayout.CompactWord56Offset)..]);
                listAt = values + GoldCreatureValuesLayout.CompactCount22Offset;
                allocations = default;
            }
            for (var i = 0; i < 8; i++) if (ids[i] != 0) skills.Add(new SkillInfo(ids[i], levels[i], bonuses.IsEmpty ? 0 : bonuses[i], Names?.Skill(ids[i])));
            if (listAt + 2 <= body.Length)
            {
                var count22 = BinaryPrimitives.ReadUInt16LittleEndian(body[listAt..]);
                var at = listAt + 2;
                for (var i = 0; i < count22 && at + 22 <= body.Length; i++, at += 22)
                {
                    var id = BinaryPrimitives.ReadUInt16LittleEndian(body[(at + 4)..]);
                    arts.Add(new ArtInfo((int)BinaryPrimitives.ReadUInt32LittleEndian(body[at..]), id, body[at + 6], Names?.CombatArt(id)));
                }
                if (at + 2 <= body.Length)
                {
                    combos = BinaryPrimitives.ReadUInt16LittleEndian(body[at..]);
                    var suffix = at + 2 + combos * 38;
                    if (!full && suffix + GoldCreatureValuesLayout.CompactSuffixSize <= body.Length)
                    {
                        points = BinaryPrimitives.ReadUInt16LittleEndian(body[suffix..]);
                        allocations = body.Slice(suffix + 4, 6);
                    }
                }
            }
            for (var i = 0; i < 6 && i < allocations.Length; i++) spent[i] = allocations[i];
        }

        var equipment = new List<EquippedItem>();
        for (var i = 0; i < GoldCreatureStateLayout.EquipmentCount; i++)
        {
            var reference = live is { EquipmentKnown: true } ? live.Equipment[i]
                : body.Length >= GoldCreatureStateLayout.EquipmentOffset + GoldCreatureStateLayout.EquipmentCount * 4
                    ? BinaryPrimitives.ReadUInt32LittleEndian(body[(GoldCreatureStateLayout.EquipmentOffset + i * 4)..]) : 0;
            if (reference != 0 && Item(reference) is { } item) equipment.Add(new EquippedItem(i, item));
        }
        var character = CharacterName(s);
        return new HeroInfo(s.Slot, character, s.Name, s.RosterEntry[RosterClass], level, experience, gold, points, skills.ToArray(), spent, attributes,
            arts.ToArray(), combos, equipment.ToArray(), live is null ? [] : Stored(live.InventoryStream), live is null ? [] : Stored(live.SharedInventoryStream), s.InGame,
            s.JoinedMs == 0 ? 0 : (now - s.JoinedMs) / 1000.0);
    }

    private StoredItem[] Stored(ReadOnlySpan<byte> inventory)
    {
        if (inventory.Length < SacredSavedInventoryLayout.CellsOffset + SacredSavedInventoryLayout.CellCount * SacredSavedInventoryLayout.CellSize) return [];
        var list = new List<StoredItem>();
        for (var cell = 0; cell < SacredSavedInventoryLayout.CellCount; cell++)
        {
            var at = inventory[(SacredSavedInventoryLayout.CellsOffset + cell * SacredSavedInventoryLayout.CellSize)..];
            var reference = BinaryPrimitives.ReadUInt32LittleEndian(at);
            if (reference == 0 || Item(reference) is not { } item) continue;
            list.Add(new StoredItem(cell, at[5], at[6], item));
        }
        return list.ToArray();
    }

    /// <summary>An item from the object world: type, level (+25), rarity tier (+6 low nibble), sockets (value records), price (+8),
    /// name (its own name word, base state +37, else the type's).</summary>
    private ItemInfo? Item(uint reference)
    {
        if (!_world.TryGet(reference, out var entry)) return null;
        var body = entry.Payload[(entry.LongPrefix ? 24 : 8)..];
        if (!SavedObjects.ReadLiveItem(body, out var records, out _)) return null;
        var data = body.Slice(GoldItemStateLayout.RecordsOffset + records * GoldItemStateLayout.ItemDataSize, GoldItemStateLayout.ItemDataSize);
        var type = BinaryPrimitives.ReadUInt32LittleEndian(data);
        return new ItemInfo(reference, type, data[25], data[6] & 15, records, BinaryPrimitives.ReadUInt32LittleEndian(data[8..]),
            Names?.Item(type, Data.NameService.InstanceName(body)));
    }
}

/// <summary>Script HTTP endpoints: the web thread queues a request, the game loop runs the Lua handler (PumpScripts) and
/// completes it. At most <see cref="ModQueueLimit"/> requests wait; more are answered 503 at once.</summary>
public sealed partial class GameServerHost
{
    private const int ModQueueLimit = 64, ModRequestsPerTick = 16;
    private readonly System.Collections.Concurrent.ConcurrentQueue<ModWork> _modRequests = new();
    private int _modQueued;

    private sealed record ModWork(string Mod, string Path, IReadOnlyList<KeyValuePair<string, string>> Query, bool PublicOnly, TaskCompletionSource<ModResponse> Done);

    public IReadOnlyList<ModInfo> Mods()
    {
        if (_scripts is null) return [];
        var items = ModEndpoints();
        return _scripts.Mods.Select(m => new ModInfo(m.Mod, m.Description, m.Error, items.Where(i => i.Mod == m.Mod).ToArray())).ToArray();
    }

    public IReadOnlyList<ModEndpointInfo> ModEndpoints() =>
        _scripts?.Endpoints.Select(e => new ModEndpointInfo(e.Mod, e.Path, e.Public, e.Description, e.Page)).OrderBy(e => e.Mod).ThenBy(e => e.Path).ToArray() ?? [];

    public Task<ModResponse> ModRequest(string mod, string path, IReadOnlyList<KeyValuePair<string, string>> query, bool publicOnly, CancellationToken cancellationToken)
    {
        if (_scripts is null) return Task.FromResult(new ModResponse(404, "text/plain; charset=utf-8", "scripts are off"));
        if (Interlocked.Increment(ref _modQueued) > ModQueueLimit)
        {
            Interlocked.Decrement(ref _modQueued);
            return Task.FromResult(new ModResponse(503, "text/plain; charset=utf-8", "busy"));
        }
        var work = new ModWork(mod, path, query, publicOnly, new TaskCompletionSource<ModResponse>(TaskCreationOptions.RunContinuationsAsynchronously));
        _modRequests.Enqueue(work);
        return work.Done.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
    }

    /// <summary>Game loop: answer queued script endpoint requests.</summary>
    private void PumpModRequests()
    {
        for (var i = 0; i < ModRequestsPerTick && _modRequests.TryDequeue(out var work); i++)
        {
            Interlocked.Decrement(ref _modQueued);
            var answer = _scripts?.HandleRequest(work.Mod, work.Path, work.Query, work.PublicOnly) ?? new Scripting.ScriptResponse(404, "text/plain; charset=utf-8", "scripts are off");
            work.Done.TrySetResult(new ModResponse(answer.Status, answer.ContentType, answer.Body, answer.Page));
            ExecuteScriptKicks();
        }
    }
}
