using System.Buffers.Binary;
using System.Text;
using Sacred.GameServer.Scripting;
using Sacred.GameServer.Wire;
using Sacred.Core.Pax.Hero.Inventory;
using Sacred.Network.Gameplay;

namespace Sacred.GameServer.Session;

/// <summary>
/// Server-side Lua scripts (Scripting/ScriptHost.cs) wired into the game loop. Events:
/// <list type="bullet">
/// <item>"join" after admission, "enter" when the player's world starts (0x017/0x018), "leave" when it is removed;</item>
/// <item>"chat" for public chat 0x005 [u32][UTF-16 text]; a handler returning true keeps the line from being relayed;</item>
/// <item>"kill" for 0x17D [creature ref][killer word] (GameServer.exe case 0x4C647E applies native event 0x208, death, to its
/// creature with that word): the word is the killing player's slot, or an object of a player (a summon), credited to its
/// owner; otherwise the sender. The creature's type and level come from the server's object world (compact values of its
/// 0x16E/0x16F state). A creature is counted once even if several players report it.</item>
/// </list>
/// Everything runs on the game loop thread; a kick asked for by a script runs after the script call.
/// </summary>
public sealed partial class GameServerHost : IScriptServer
{
    private ScriptHost? _scripts;
    private readonly List<(int Slot, string Reason)> _scriptKicks = new();
    private readonly uint[] _recentKills = new uint[512];
    private int _recentKillNext;
    private long _nextScriptFlushMs;
    private bool _inScriptKicks;

    /// <summary>The running script host (null when scripts are off).</summary>
    public ScriptHost? Scripts => _scripts;

    /// <summary>Loads the scripts and their data and starts watching the folder. Call before <see cref="Run"/>.</summary>
    public void StartScripts()
    {
        var options = _config.Scripts;
        _scripts = new ScriptHost(options, this);
        _scripts.Load();
        try { _scripts.Watch(); }
        catch (Exception e) when (e is not OutOfMemoryException) { ScriptFault("watching the scripts folder", e); }
        ExecuteScriptKicks();
    }

    /// <summary>Game loop (web admin): one mod, or every mod with null, runs its unload handlers and is loaded again from disk.</summary>
    private void ReloadMods(string? mod)
    {
        if (_scripts is null) { Log.Info("web admin: scripts are off, nothing to reload."); return; }
        try
        {
            Log.Info(mod is null ? "web admin: reloading every mod." : $"web admin: reloading mod {mod}.");
            if (!_scripts.Reload(mod)) Log.Info($"web admin: no mod {mod}.");
            ExecuteScriptKicks();
        }
        catch (Exception e) when (e is not OutOfMemoryException) { ScriptFault("reload", e); }
    }

    private void PumpScripts(long now)
    {
        if (_scripts is null) return;
        try
        {
            _scripts.Pump(now);
            PumpModRequests();
            PumpStoreRequests();
            ExecuteScriptKicks();
            if (now >= _nextScriptFlushMs) { _nextScriptFlushMs = now + 10_000; _scripts.FlushStores(); }
        }
        catch (Exception e) when (e is not OutOfMemoryException) { ScriptFault("tick", e); }
    }

    /// <summary>The last line of defence: whatever goes wrong around a script call is logged, the game goes on.</summary>
    private void ScriptFault(string where, Exception e) => Log.Info($"SCRIPT: {where} failed ({e.GetType().Name}: {e.Message}); the server continues.");

    private void StopScripts()
    {
        try { _scripts?.Shutdown(); }                                       // the mods' "unload" handlers (reason "shutdown") save their state
        catch (Exception e) when (e is not OutOfMemoryException) { ScriptFault("unloading the scripts", e); }
        _scripts?.Dispose(); _scripts?.FlushStores(wait: true);
        while (_modRequests.TryDequeue(out var pending)) pending.Done.TrySetResult(new Web.ModResponse(503, "text/plain; charset=utf-8", "server stopping"));
        while (_storeRequests.TryDequeue(out var pendingStore)) pendingStore.Done.TrySetResult(new Web.ModResponse(503, "text/plain; charset=utf-8", "server stopping"));
    }

    // ---- hooks --------------------------------------------------------------------------------------------------------

    /// <summary>Route: returns true when a script consumed the message.</summary>
    private bool ScriptMessage(PlayerSession s, in GoldMessageView m)
    {
        try { return ScriptMessageCore(s, m); }
        catch (Exception e) when (e is not OutOfMemoryException) { ScriptFault($"message 0x{m.Opcode:X3}", e); return false; }
    }

    private bool ScriptMessageCore(PlayerSession s, in GoldMessageView m)
    {
        switch (m.Opcode)
        {
            case 0x005 when m.Payload.Length > 4 && _scripts!.Has("chat"):
            {
                var consumed = _scripts.FireChat(ScriptPlayerOf(s), ChatText(m.Payload[4..]));
                ExecuteScriptKicks();
                return consumed;
            }
            case 0x17D when m.Payload.Length >= 8 && (_scripts!.Has("kill") || _scripts.Has("death")):
                OnKill(s, BinaryPrimitives.ReadUInt32LittleEndian(m.Payload) & 0x7FFFFFFF, BinaryPrimitives.ReadUInt32LittleEndian(m.Payload[4..]));
                return false;
        }
        return false;
    }

    private void ScriptPlayerEvent(string name, PlayerSession s)
    {
        if (_scripts is null || s.Slot == 0 || !_scripts.Has(name)) return;
        try
        {
            _scripts.FirePlayer(name, ScriptPlayerOf(s));
            if (name != "leave") ExecuteScriptKicks();
        }
        catch (Exception e) when (e is not OutOfMemoryException) { ScriptFault(name, e); }
    }

    /// <summary>The hero moved into another sector (<see cref="PlayerSession.CurrentSector"/> is already updated).</summary>
    private void ScriptSectorChanged(PlayerSession s)
    {
        if (_scripts is null || !_scripts.Has("sector")) return;
        try { _scripts.FireSector(ScriptPlayerOf(s)); ExecuteScriptKicks(); }
        catch (Exception e) when (e is not OutOfMemoryException) { ScriptFault("sector", e); }
    }

    private void OnKill(PlayerSession sender, uint victim, uint killerWord)
    {
        if (victim is >= 1 and <= MaxSlots) { OnHeroDeath(victim, killerWord); return; }
        foreach (var known in _recentKills) if (known == victim) return;
        _recentKills[_recentKillNext] = victim;
        _recentKillNext = (_recentKillNext + 1) % _recentKills.Length;
        var killer = killerWord is >= 1 and <= MaxSlots && _slots[killerWord] is { } p ? p
            : _world.TryGet(killerWord & 0x7FFFFFFF, out var owned) && owned.Owner is >= 1 and <= MaxSlots && _slots[owned.Owner] is { } owner ? owner
            : sender;
        uint type = 0, name = 0; var level = 0; int sector = 0, vx = 0, vy = 0;
        if (_world.TryGet(victim, out var entry))
        {
            var where = Position(entry);
            (sector, vx, vy) = (where.Sector, where.X, where.Y);
            var payload = entry.Payload;
            type = BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]);
            level = CompactLevel(payload[(entry.LongPrefix ? 24 : 8)..]);
            name = Data.NameService.InstanceName(payload[(entry.LongPrefix ? 24 : 8)..]);
        }
        _scripts!.FireKill(ScriptPlayerOf(killer), new ScriptMonster(victim, type, level, type == 0 ? null : Names?.Creature(type, name), sector, vx, vy));
        ExecuteScriptKicks();
    }

    /// <summary>
    /// 0x17D for a hero (reference = slot): the player died. The killer word is another player's slot or the killing object's
    /// reference (its type and level from the object world). Unverified with a real client: that hero deaths arrive as 0x17D.
    /// Several reports of one death within two seconds count once.
    /// </summary>
    private void OnHeroDeath(uint slot, uint killerWord)
    {
        if (_slots[slot] is not { } dead || dead.State < SessionState.SlotAssigned) return;
        var now = _clock.ElapsedMilliseconds;
        if (_lastHeroDeathMs[slot] != 0 && now - _lastHeroDeathMs[slot] < 2000) return;
        _lastHeroDeathMs[slot] = now;
        ScriptPlayer? killer = killerWord is >= 1 and <= MaxSlots && killerWord != slot && _slots[killerWord] is { } p ? ScriptPlayerOf(p) : null;
        ScriptMonster? monster = null;
        if (killer is null && killerWord != 0 && _world.TryGet(killerWord & 0x7FFFFFFF, out var entry))
        {
            var body = entry.Payload[(entry.LongPrefix ? 24 : 8)..];
            var type = BinaryPrimitives.ReadUInt32LittleEndian(entry.Payload[4..]);
            monster = new ScriptMonster(killerWord & 0x7FFFFFFF, type, CompactLevel(body), type == 0 ? null : Names?.Creature(type, Data.NameService.InstanceName(body)));
        }
        _scripts!.FireDeath(ScriptPlayerOf(dead), killer, monster);
        ExecuteScriptKicks();
    }

    /// <summary>
    /// A 0x16F state (an object with a position) the server's world did not have yet and that is an item: the "drop" event. The game
    /// has no drop message; a creature's loot, a chest's content or an item a player throws down all appear as such a state from
    /// the client that owns the sector, so mods that want creature loot correlate it with "kill".
    /// </summary>
    private void ScriptItemOnGround(PlayerSession reporter, uint reference)
    {
        if (_scripts is null || !_scripts.Has("drop") || reporter.Slot == 0) return;
        try
        {
            if (Item(reference) is not { } item || !_world.TryGet(reference, out var entry)) return;
            var (sector, x, y) = Position(entry);
            if (x == 0 && y == 0) return;                           // carried, not lying on the ground
            _scripts.FireDrop(ScriptPlayerOf(reporter), new ScriptItem(reference, item.Type, item.Level, item.Rarity, item.Sockets, item.Price, item.Name, sector, x, y));
            ExecuteScriptKicks();
        }
        catch (Exception e) when (e is not OutOfMemoryException) { ScriptFault("drop", e); }
    }

    private readonly long[] _lastHeroDeathMs = new long[MaxSlots + 1];

    /// <summary>Level word of a creature body with compact values (Gold 0x585C60: values at +445, level at +52), or 0.</summary>
    private static int CompactLevel(ReadOnlySpan<byte> body)
    {
        var values = GoldCreatureStateLayout.PrefixSize;
        if (body.Length < values + GoldCreatureValuesLayout.CompactWord56Offset + 2 ||
            BinaryPrimitives.ReadUInt32LittleEndian(body[GoldCreatureStateLayout.MarkerOffset..]) != GoldCreatureStateLayout.Marker ||
            BinaryPrimitives.ReadUInt32LittleEndian(body[values..]) != GoldCreatureValuesLayout.Marker) return 0;
        return BinaryPrimitives.ReadUInt16LittleEndian(body[(values + GoldCreatureValuesLayout.CompactWord56Offset)..]);
    }

    private static string ChatText(ReadOnlySpan<byte> utf16)
    {
        var length = 0;
        while (length + 1 < utf16.Length && length < ChatTextBytes && (utf16[length] | utf16[length + 1]) != 0) length += 2;
        return Encoding.Unicode.GetString(utf16[..length]);
    }

    private ScriptPlayer ScriptPlayerOf(PlayerSession s)
    {
        var level = 0; int x = 0, y = 0;
        if (_world.TryGet(s.Slot, out var hero))
        {
            level = CompactLevel(hero.Payload[(hero.LongPrefix ? 24 : 8)..]);
            (_, x, y) = Position(hero);
        }
        var key = s.LobbyIdentity is not (0 or -1) ? $"id:{s.LobbyIdentity}:{s.CharacterBlock}" : "name:" + s.Name;
        return new ScriptPlayer(s.Slot, s.Name, key, s.RosterEntry[RosterClass], level, s.InGame, s.IsHost, s.LobbyIdentity,
            s.JoinedMs == 0 ? 0 : (_clock.ElapsedMilliseconds - s.JoinedMs) / 1000.0, CharacterName(s), s.CurrentSector, x, y);
    }

    private void ExecuteScriptKicks()
    {
        if (_inScriptKicks || _scriptKicks.Count == 0) return;
        _inScriptKicks = true;
        try
        {
            for (var i = 0; i < _scriptKicks.Count; i++)
            {
                var (slot, reason) = _scriptKicks[i];
                if (_slots[slot] is not { } victim || victim.State == SessionState.Closed) continue;
                Log.Info($"SCRIPT: kicking #{slot} {victim.Name}{(reason.Length > 0 ? $" ({reason})" : "")}.");
                RemovePlayer(victim, ReasonLobbyKick, notifyVictim: true);
            }
            _scriptKicks.Clear();
        }
        finally { _inScriptKicks = false; }
    }

    // ---- IScriptServer ------------------------------------------------------------------------------------------------------

    long IScriptServer.NowMs => _clock.ElapsedMilliseconds;

    IReadOnlyList<ScriptPlayer> IScriptServer.Players()
    {
        var list = new List<ScriptPlayer>(PlayerCount);
        for (var slot = 1; slot <= MaxSlots; slot++)
            if (_slots[slot] is { } p && p.State >= SessionState.SlotAssigned) list.Add(ScriptPlayerOf(p));
        return list;
    }

    ScriptPlayer? IScriptServer.Player(int slot) => PlayerAt(slot) is { } p ? ScriptPlayerOf(p) : null;

    void IScriptServer.Broadcast(string text) => AdminSay(null, text);

    void IScriptServer.Message(int slot, string text) { if (PlayerAt(slot) is { } p) AdminSay(p, text); }

    void IScriptServer.Kick(int slot, string reason) { if (slot is >= 1 and <= MaxSlots) _scriptKicks.Add((slot, reason)); }

    ScriptHero? IScriptServer.Hero(int slot)
    {
        if (!KeepsHeroState || PlayerAt(slot) is not { } p || _live[slot] is not { } live) return null;
        var pairs = new List<(uint, uint)>();
        var stream = live.InventoryStream;
        if (stream.Length >= SacredSavedInventoryLayout.MinimumSize)
        {
            var count = BinaryPrimitives.ReadUInt32LittleEndian(stream[SacredSavedInventoryLayout.QuestCountOffset..]);
            for (var i = 0; i < count && i < 4096; i++)
            {
                var at = SacredSavedInventoryLayout.QuestRecordsOffset + i * SacredSavedInventoryLayout.QuestRecordSize;
                if (at + SacredSavedInventoryLayout.QuestRecordSize > stream.Length) break;
                pairs.Add((BinaryPrimitives.ReadUInt32LittleEndian(stream[at..]), BinaryPrimitives.ReadUInt32LittleEndian(stream[(at + 4)..])));
            }
        }
        return new ScriptHero(HeroOf(p, _clock.ElapsedMilliseconds), pairs.ToArray());
    }

    string? IScriptServer.CreatureName(uint type) => Names?.Creature(type);

    string? IScriptServer.ItemName(uint type) => Names?.Item(type);
}

/// <summary>Script rewards: items generated by the server and placed on the ground next to the player.</summary>
public sealed partial class GameServerHost
{
    private LootService? _loot;

    /// <summary>
    /// Generates one piece of equipment for the player and drops it at the hero's position as a new world object: a server
    /// reference from the pool, owned by that player (its client handles the pick-up like any of its own items), sent to every
    /// in-game player as 0x16F ([ref][type][x][y][0][sector] + the item state) through the object world's streamer.
    /// </summary>
    ScriptDrop? IScriptServer.DropItem(int slot, in ScriptLootRequest request, out string? error)
    {
        error = null;
        if (PlayerAt(slot) is not { } player) { error = "no such player"; return null; }
        if (!_world.TryGet(player.Slot, out var hero)) { error = "the player's hero is not in the world yet"; return null; }
        _loot ??= new LootService(_config.GameDirectory);
        var difficulty = (int)GoldSessionGameFlagsCodec.Difficulty(_gameFlags);
        var item = _loot.Roll(request.Level, request.HeroClass, difficulty, request.Quality, request.Category, request.Seed, out error);
        if (item is null) return null;
        var reference = DropNextToHero(player, hero, item.Type, item.Values, item.Category == Sacred.Core.Pak.Items.SacredItemCategory.Weapon, out error);
        if (reference == 0) return null;
        Log.Info($"SCRIPT: dropped {item.Name ?? "item"} (type {item.Type}, quality {item.Quality}, ref {reference}) for #{player.Slot} {player.Name}.");
        return new ScriptDrop(reference, item.Type, item.Quality, item.Name ?? "", item.Category.ToString());
    }

    /// <summary>Stores an item object of <paramref name="type"/> with <paramref name="values"/> at the hero's position, owned by
    /// the player, and streams it; returns its reference, or 0 with <paramref name="error"/>.</summary>
    private uint DropNextToHero(PlayerSession player, ObjectMirror.Entry hero, uint type, ReadOnlySpan<byte> values, bool weaponExtension, out string? error)
    {
        error = null;
        var reference = TakeReference(player.Slot);
        if (reference == 0) { error = "no free object reference"; return 0; }

        var (sector, x, y) = Position(hero);
        var basis = new GoldObjectBaseState(reference, type, 0, sector, x, y, 0, 0, 0, 0, player.Slot, 0);
        var physical = GoldPhysicalConstructionWriter.Write(basis, new GoldPhysicalVector(0, 1, 0));
        var weapon = weaponExtension ? new byte[GoldItemStateLayout.WeaponExtensionSize] : [];
        if (weapon.Length != 0) BinaryPrimitives.WriteUInt32LittleEndian(weapon, GoldItemStateLayout.WeaponMarker);
        var body = GoldItemState.Create(physical, [], values, 0, weapon).ToPayload();
        var payload = new byte[24 + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, reference);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), type);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8), x);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12), y);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20), sector);
        body.CopyTo(payload, 24);
        if (!_world.Store(0x16F, player.Slot, payload, 0)) { error = "the object world is full"; return 0; }
        StreamObject(reference);
        return reference;
    }

    /// <summary>Sector and position of an object from its mirrored state (24-byte prefix, or the base block of an 8-byte one).</summary>
    private static (ushort Sector, int X, int Y) Position(ObjectMirror.Entry entry)
    {
        var p = entry.Payload;
        if (entry.LongPrefix) return ((ushort)BinaryPrimitives.ReadUInt32LittleEndian(p[20..]), BinaryPrimitives.ReadInt32LittleEndian(p[8..]), BinaryPrimitives.ReadInt32LittleEndian(p[12..]));
        var body = p[8..];
        return body.Length < GoldObjectStateLayout.Size ? ((ushort)0, 0, 0)
            : (BinaryPrimitives.ReadUInt16LittleEndian(body[GoldObjectStateLayout.SectorOffset..]), BinaryPrimitives.ReadInt32LittleEndian(body[GoldObjectStateLayout.XOffset..]),
               BinaryPrimitives.ReadInt32LittleEndian(body[GoldObjectStateLayout.YOffset..]));
    }
}
