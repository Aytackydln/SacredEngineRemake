using Lua;
using Lua.Standard;

namespace Sacred.GameServer.Scripting;

/// <summary>A player as scripts see it.</summary>
/// <remarks><see cref="Name"/> is the FIRSTCONTACT name and <see cref="Key"/>/<see cref="LobbyId"/> hold lobby identities: they never
/// reach Lua. Scripts get <see cref="Character"/> (the host strips the "account." prefix of lobby names) and an opaque per-server
/// <c>uid</c> derived from <see cref="Key"/>.</remarks>
public sealed record ScriptPlayer(int Slot, string Name, string Key, int Class, int Level, bool InGame, bool IsHost, int LobbyId, double OnlineSeconds = 0,
    string? Character = null);

/// <summary>A defeated creature: its object reference, its type (Items.pak type), level and name (the game's text: the creature's own
/// name, else its type's; null without --game-dir) when the server knows them.</summary>
public readonly record struct ScriptMonster(uint Reference, uint Type, int Level, string? Name = null);

/// <summary>What a script asked for with <c>server.drop_item</c>.</summary>
public readonly record struct ScriptLootRequest(int Level, int HeroClass, uint? Quality, Sacred.Core.Pak.Items.SacredItemCategory? Category, uint Seed);

/// <summary>An item a script dropped: reference, type, quality, generated name and category.</summary>
public sealed record ScriptDrop(uint Reference, uint Type, uint Quality, string Name, string Category);

/// <summary>What scripts can do on the server. Implemented by the game server; called on the game loop thread only.</summary>
public interface IScriptServer
{
    long NowMs { get; }
    IReadOnlyList<ScriptPlayer> Players();
    ScriptPlayer? Player(int slot);
    /// <summary>Server chat line to everybody.</summary>
    void Broadcast(string text);
    /// <summary>Private server chat line to one player.</summary>
    void Message(int slot, string text);
    /// <summary>Runs after the current script call returns (removing a player fires "leave" itself).</summary>
    void Kick(int slot, string reason);
    /// <summary>Generates equipment for the player and drops it next to its hero; null with <paramref name="error"/> on failure.</summary>
    ScriptDrop? DropItem(int slot, in ScriptLootRequest request, out string? error);
    /// <summary>The game's name of a creature type, null when unknown or the game's tables were not read.</summary>
    string? CreatureName(uint type) => null;
    /// <summary>The game's (base) name of an item type, null when unknown or the game's tables were not read.</summary>
    string? ItemName(uint type) => null;
    /// <summary>Drops a gold pile of <paramref name="amount"/> (1..<see cref="GoldPile.MaximumAmount"/>) next to the player's hero.</summary>
    ScriptDrop? DropGold(int slot, uint amount, out string? error);
    // ---- custom net messages (the net table; the defaults: not available) ----------------------------------------------------
    /// <summary>Registers a channel for scripts; returns its id. Throws ArgumentException for a bad name or an id collision.</summary>
    uint NetRegister(string name, bool serverOnly) => throw new NotSupportedException("custom net messages are not available");
    /// <summary>The scripts reloaded: forget the channels they registered.</summary>
    void NetReset() { }
    bool NetSupports(int slot) => false;
    /// <summary>Sends a message to the player in <paramref name="slot"/> (0: every player that supports them); the number of players reached.</summary>
    int NetSend(int slot, uint channel, bool json, byte[] data, out string? error) { error = "custom net messages are not available"; return 0; }
    /// <summary>Lua instructions one net handler may run.</summary>
    int NetHandlerBudget => 200_000;
}

/// <summary>
/// The Lua side of server scripts, on LuaCSharp (pure managed Lua 5.2, Native AOT safe). One <see cref="LuaState"/> holds every
/// <c>*.lua</c> file of the scripts folder; they register handlers on the <c>server</c> table:
/// <code>
/// server.on("join"|"enter"|"leave", function(player) end)
/// server.on("chat", function(player, text) return true end)   -- true: the line is not relayed
/// server.on("kill", function(player, monster) end)            -- monster = { ref, type, level, name } (name nil without --game-dir)
/// server.every(seconds, fn) / server.after(seconds, fn)
/// server.broadcast(text) / server.message(player or slot, text) / server.kick(player or slot, reason)
/// server.players() / server.player(slot) / server.time() / server.log(text)
/// server.save(key, value) / server.load(key [, default]) / server.delete(key) / server.keys([prefix])   -- the mod's own file
/// server.get(player or nil, key [, default]) / server.set(player or nil, key, value) / server.add(player or nil, key, n)
/// server.drop_item(player, { quality = 0..14, category = "ring", level = n, class = n, seed = n })  -- returns item or nil, error
/// server.drop_gold(player, amount)                    -- returns { ref, type, amount } or nil, error
/// server.random([m [, n]])
/// server.top(key, n) -> { { name, value }, ... }   (per-hero numbers of this mod, also offline heroes; names are safe)
/// server.creature_name(type) / server.item_name(type)  -- the game's names for a type number, or nil
/// server.endpoint(path, fn [, { public = true, description = "..." }])  -- GET /mods/{script}/{path} (ScriptEndpoints.cs)
/// server.page(path, html or fn [, { public = true, title = "..." }])  -- a sandboxed single-file HTML page at the same address
/// server.describe(text)  -- the mod's short description for the web UI (default: the first sentence of the file's leading comment)
/// server.file(name) / server.html(text) / server.render(template, values) / server.csv(rows [, columns]) / server.json(table)
/// Every mod (script file) has its own storage, <c>{data folder}/{mod}.json</c> (<see cref="ModStore"/>); the game server
/// keeps no state for mods.
/// </code>
/// Sandbox: basic (without dofile/loadfile/load), string, table, math and bit32 libraries only; no io, os, require or debug.
/// Every handler or timer call gets an instruction budget (<see cref="ScriptOptions.InstructionBudget"/>) enforced through a
/// count hook, so an endless loop is stopped with an error instead of freezing the server. Scripts are the server owner's
/// own code: the sandbox guards against mistakes, not against a hostile script author.
/// </summary>
public sealed partial class ScriptHost
{
    private const int HookInterval = 1000, MaxConsecutiveErrors = 20;
    private readonly ScriptOptions _options;
    private readonly IScriptServer _server;
    private readonly Dictionary<string, ModStore> _stores = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Handler>> _handlers = new(StringComparer.Ordinal);
    private readonly List<Timer> _timers = new();
    private readonly List<Timer> _newTimers = new();
    private readonly LuaValue[] _one = new LuaValue[1], _two = new LuaValue[2];
    private LuaState? _state;
    private long _budget, _nextBudget;
    private bool _exhausted;
    private string _currentFile = "";
    private FileSystemWatcher? _watcher;
    private volatile bool _changed;
    private long _reloadDueMs;

    private readonly Dictionary<string, ScriptEndpoints.Endpoint> _endpoints = new(StringComparer.Ordinal);
    private ScriptEndpoints.Endpoint[] _endpointList = [];

    /// <summary>Endpoints of the loaded scripts (any thread: an immutable array replaced on every change).</summary>
    public IReadOnlyList<ScriptEndpoints.Endpoint> Endpoints => Volatile.Read(ref _endpointList);

    /// <summary>Runs the endpoint <paramref name="mod"/>/<paramref name="path"/>; 404 when there is none (or it is not public and
    /// <paramref name="publicOnly"/> is set), 500 when the script fails.</summary>
    public ScriptResponse HandleRequest(string mod, string path, IReadOnlyList<KeyValuePair<string, string>> query, bool publicOnly)
    {
        if (_state is null || !_endpoints.TryGetValue(ScriptEndpoints.Key(mod, path), out var endpoint) || (publicOnly && !endpoint.Public))
            return new(404, "text/plain; charset=utf-8", "no such endpoint");
        var parameters = new LuaTable(0, query.Count);
        foreach (var (key, value) in query) parameters[key] = value;
        var request = new LuaTable(0, 2) { ["path"] = endpoint.Path, ["query"] = parameters };
        try
        {
            _currentFile = endpoint.File;
            if (endpoint.Html is { } html) return ScriptEndpoints.Page(html, _options.MaxResponseChars);
            var values = Run(_state.CallAsync(endpoint.Function!, One(request)));
            return endpoint.Page ? ScriptEndpoints.Page(values.Length > 0 && values[0].Type == LuaValueType.String ? values[0].Read<string>() : "", _options.MaxResponseChars)
                : ScriptEndpoints.Response(values, _options.MaxResponseChars);
        }
        catch (Exception e) when (Contained(e))
        {
            Errors++;
            Log.Info($"SCRIPT: {endpoint.File}: endpoint {endpoint.Mod}/{endpoint.Path}: {Describe(e)}");
            return new(500, "text/plain; charset=utf-8", "script error");
        }
        finally { _currentFile = ""; }
    }

    private sealed class Handler { public required LuaFunction Function; public required string File; public int Errors; }
    private sealed class Timer { public required LuaFunction Function; public required string File; public long DueMs; public long IntervalMs; public int Errors; }

    public ScriptHost(ScriptOptions options, IScriptServer server) { _options = options; _server = server; }

    /// <summary>The storage of <paramref name="mod"/> (script file name without .lua), opened on first use.</summary>
    public ModStore StoreOf(string mod)
    {
        mod = ScriptEndpoints.ModName(mod);
        if (!_stores.TryGetValue(mod, out var store))
            _stores[mod] = store = new ModStore(Path.Combine(_options.DataDirectory, mod + ".json"))
            {
                MaxChars = _options.MaxModStoreChars,
                BeforeGrow = growth =>
                {
                    var total = _stores.Values.Sum(s => s.Size) + growth;
                    if (total > _options.MaxTotalStoreChars)
                        throw new InvalidOperationException($"all mods' storage full: {total:N0} of {_options.MaxTotalStoreChars:N0} bytes");
                },
            };
        return store;
    }

    private ModStore CurrentStore => _currentFile.Length > 0 ? StoreOf(_currentFile) : throw new InvalidOperationException("storage is only available inside a script");

    /// <summary>Writes every changed mod file (temporary file + move).</summary>
    public void FlushStores(bool wait = false) { foreach (var store in _stores.Values) store.Flush(wait); }

    /// <summary>Loaded files and their load errors (diagnostics, web UI).</summary>
    public IReadOnlyList<(string File, string? Error)> Files => _files;
    private readonly List<(string File, string? Error)> _files = new();
    private readonly Dictionary<string, string> _descriptions = new(StringComparer.Ordinal);

    /// <summary>One installed mod: name (file without .lua), description, load error.</summary>
    public sealed record ModEntry(string Mod, string? Description, string? Error);
    private ModEntry[] _mods = [];

    /// <summary>Every script file of the last load (any thread: an immutable array replaced on every load).</summary>
    public IReadOnlyList<ModEntry> Mods => Volatile.Read(ref _mods);

    /// <summary>The first sentence of the comment block a script starts with (lines beginning with "--"), at most 200 characters.</summary>
    public static string? LeadingComment(string source)
    {
        var text = new System.Text.StringBuilder();
        foreach (var raw in source.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 && text.Length == 0) continue;
            if (!line.StartsWith("--", StringComparison.Ordinal) || line.StartsWith("--[[", StringComparison.Ordinal)) break;
            var content = line.TrimStart('-').Trim();
            if (content.Length == 0) { if (text.Length > 0) break; continue; }
            if (text.Length > 0) text.Append(' ');
            text.Append(content);
        }
        if (text.Length == 0) return null;
        var all = text.ToString();
        var end = all.IndexOf(". ", StringComparison.Ordinal);
        var sentence = end > 0 ? all[..(end + 1)] : all;
        return sentence.Length > 200 ? sentence[..197] + "..." : sentence;
    }
    public long Errors { get; private set; }
    public int HandlerCount => _handlers.Values.Sum(h => h.Count);

    /// <summary>(Re)creates the Lua state and runs every script file, then fires "start".</summary>
    public void Load()
    {
        _handlers.Clear(); _timers.Clear(); _newTimers.Clear(); _files.Clear(); _endpoints.Clear(); _descriptions.Clear();
        ResetNet();
        _state = CreateState();
        var files = Directory.Exists(_options.Directory)
            ? Directory.GetFiles(_options.Directory, "*.lua").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray() : [];
        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            string? error = null;
            try
            {
                var source = File.ReadAllText(path);
                if (LeadingComment(source) is { } comment) _descriptions[ScriptEndpoints.ModName(name)] = comment;
                _currentFile = name;
                Run(() => _state.DoStringAsync(source, name));
            }
            catch (Exception e) when (Contained(e)) { error = Describe(e); }
            _files.Add((name, error));
            Log.Info(error is null ? $"SCRIPT: loaded {name}." : $"SCRIPT: {name} failed to load: {error}");
        }
        _currentFile = "";
        Volatile.Write(ref _endpointList, _endpoints.Values.ToArray());
        Volatile.Write(ref _mods, _files.Select(f => new ModEntry(ScriptEndpoints.ModName(f.File), _descriptions.GetValueOrDefault(ScriptEndpoints.ModName(f.File)), f.Error)).ToArray());
        Fire("start");
        Log.Info($"SCRIPT: {files.Length} file(s), {HandlerCount} handler(s), {_timers.Count + _newTimers.Count} timer(s).");
    }

    /// <summary>Watches the folder: a changed, added or removed script reloads everything half a second later.</summary>
    public void Watch()
    {
        if (_watcher is not null || !Directory.Exists(_options.Directory)) return;
        _watcher = new FileSystemWatcher(_options.Directory, "*.lua") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
        _watcher.Changed += (_, _) => _changed = true;
        _watcher.Created += (_, _) => _changed = true;
        _watcher.Deleted += (_, _) => _changed = true;
        _watcher.Renamed += (_, _) => _changed = true;
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Requests a reload at the next pump (admin command).</summary>
    public void RequestReload() => _changed = true;

    /// <summary>Game loop: timers and pending reloads.</summary>
    public void Pump(long nowMs)
    {
        if (_changed) { _changed = false; _reloadDueMs = nowMs + 500; }
        if (_reloadDueMs != 0 && nowMs >= _reloadDueMs) { _reloadDueMs = 0; Log.Info("SCRIPT: reloading."); Load(); }
        if (_newTimers.Count > 0) { _timers.AddRange(_newTimers); _newTimers.Clear(); }
        for (var i = _timers.Count - 1; i >= 0; i--)
        {
            var timer = _timers[i];
            if (nowMs < timer.DueMs) continue;
            if (timer.IntervalMs > 0) timer.DueMs = nowMs + timer.IntervalMs; else _timers.RemoveAt(i);
            if (!Call(timer.Function, timer.File, default, ref timer.Errors, out _)) { if (timer.Errors >= MaxConsecutiveErrors) _timers.Remove(timer); }
        }
    }

    public void Dispose() { _watcher?.Dispose(); _watcher = null; }

    // ---- events ---------------------------------------------------------------------------------------------------------

    public bool Has(string name) => _handlers.TryGetValue(name, out var list) && list.Count > 0;

    /// <summary>Calls every handler of <paramref name="name"/>; true when one returned true (for "chat": swallow the line).</summary>
    public bool Fire(string name, LuaValue first = default, LuaValue second = default, int count = 0)
    {
        if (!_handlers.TryGetValue(name, out var list) || list.Count == 0) return false;
        ReadOnlySpan<LuaValue> args = count switch { 0 => default, 1 => One(first), _ => Two(first, second) };
        var consumed = false;
        for (var i = 0; i < list.Count; i++)
        {
            var handler = list[i];
            if (Call(handler.Function, handler.File, args, ref handler.Errors, out var result)) consumed |= result.ToBoolean();
            else if (handler.Errors >= MaxConsecutiveErrors)
            {
                Log.Info($"SCRIPT: {handler.File}: '{name}' handler disabled after {MaxConsecutiveErrors} errors in a row.");
                list.RemoveAt(i--);
            }
        }
        return consumed;
    }

    public bool FirePlayer(string name, ScriptPlayer player) => Has(name) && Fire(name, PlayerTable(player), default, 1);
    public bool FireChat(ScriptPlayer player, string text) => Has("chat") && Fire("chat", PlayerTable(player), text, 2);
    public bool FireKill(ScriptPlayer player, ScriptMonster monster)
    {
        if (!Has("kill")) return false;
        var table = new LuaTable(0, 4) { ["ref"] = monster.Reference, ["type"] = monster.Type, ["level"] = monster.Level };
        if (monster.Name is { } name) table["name"] = name;
        return Fire("kill", PlayerTable(player), table, 2);
    }

    /// <summary>A player's hero died; <paramref name="killer"/> is another player or null, <paramref name="monster"/> the creature that
    /// killed it when known.</summary>
    public bool FireDeath(ScriptPlayer player, ScriptPlayer? killer, ScriptMonster? monster)
    {
        if (!Has("death")) return false;
        var by = new LuaTable(0, 4);
        if (killer is not null) by["player"] = PlayerTable(killer);
        if (monster is { } m) { by["ref"] = m.Reference; by["type"] = m.Type; by["level"] = m.Level; if (m.Name is { } name) by["name"] = name; }
        return Fire("death", PlayerTable(player), by, 2);
    }

    private ReadOnlySpan<LuaValue> One(LuaValue a) { _one[0] = a; return _one; }
    private ReadOnlySpan<LuaValue> Two(LuaValue a, LuaValue b) { _two[0] = a; _two[1] = b; return _two; }

    private bool Call(LuaFunction function, string file, ReadOnlySpan<LuaValue> args, ref int errors, out LuaValue result)
    {
        result = LuaValue.Nil;
        if (_state is null) return false;
        try
        {
            _currentFile = file;
            _budget = Math.Max(1, (_nextBudget > 0 ? _nextBudget : _options.InstructionBudget) / HookInterval); _exhausted = false; _nextBudget = 0;   // every entry call gets a fresh budget
            var values = Run(_state.CallAsync(function, args));
            result = values.Length > 0 ? values[0] : LuaValue.Nil;
            errors = 0;
            return true;
        }
        catch (Exception e) when (Contained(e))
        {
            errors++; Errors++;
            Log.Info($"SCRIPT: {file}: {Describe(e)}");
            return false;
        }
        finally { _currentFile = ""; }
    }

    /// <summary>Runs one Lua entry with a fresh instruction budget. Our functions never await, so the call completes synchronously.</summary>
    private T Run<T>(ValueTask<T> task)
    {
        if (!task.IsCompleted) throw new InvalidOperationException("a script call did not finish synchronously");
        return task.Result;
    }

    private void Run(Func<ValueTask<LuaValue[]>> start)
    {
        _budget = Math.Max(1, _options.InstructionBudget / HookInterval); _exhausted = false;
        Run(start());
    }

    /// <summary>A script must never take the server down: every failure of a script call (Lua errors, the budget, and any .NET
    /// exception a server function or the interpreter throws) is logged and contained. Only out-of-memory is not swallowed.</summary>
    private static bool Contained(Exception e) => e is not OutOfMemoryException;

    private static bool IsScriptError(Exception e) => e is LuaRuntimeException or LuaParseException or LuaCompileException or LuaStackOverflowException
        or LuaCanceledException or ScriptBudgetException or InvalidCastException;

    private static string Describe(Exception e) => e switch
    {
        ScriptBudgetException => e.Message,
        LuaRuntimeException { InnerException: ScriptBudgetException inner } => inner.Message,
        _ when e.InnerException is ScriptBudgetException inner => inner.Message,
        _ when IsScriptError(e) => e.Message,
        _ => $"{e.GetType().Name}: {e.Message}",
    };

    // ---- the Lua state ----------------------------------------------------------------------------------------------------

    private LuaState CreateState()
    {
        var state = LuaState.Create();
        state.OpenBasicLibrary(); state.OpenStringLibrary(); state.OpenTableLibrary(); state.OpenMathLibrary(); state.OpenBitwiseLibrary();
        foreach (var name in (ReadOnlySpan<string>)["dofile", "loadfile", "load", "collectgarbage"]) state.Environment[name] = LuaValue.Nil;
        // string.rep can build a huge string in one instruction: cap its result (the budget cannot see it).
        var strings = state.Environment["string"].Read<LuaTable>();
        var rep = strings["rep"].Read<LuaFunction>();
        strings["rep"] = new LuaFunction("rep", async (c, ct) =>
        {
            var length = c.HasArgument(0) ? c.GetArgument(0).ToString().Length : 0;
            var count = c.HasArgument(1) && c.GetArgument(1).Type == LuaValueType.Number ? c.GetArgument<double>(1) : 0;
            var separator = c.HasArgument(2) ? c.GetArgument(2).ToString().Length : 0;
            if (count > 0 && (length + (double)separator) * count > _options.MaxStringChars)
                throw new ScriptBudgetException($"string.rep result larger than {_options.MaxStringChars:N0} characters");
            var results = await c.State.CallAsync(rep, c.Arguments, ct).ConfigureAwait(false);
            return c.Return(results);
        });
        state.Environment["print"] = Fn("print", c =>
        {
            var parts = new string[c.ArgumentCount];
            for (var i = 0; i < parts.Length; i++) parts[i] = c.GetArgument(i).ToString();
            Log.Info($"SCRIPT {_currentFile}: {string.Join('\t', parts)}");
            return c.Return();
        });
        // The budget: every HookInterval instructions; once exhausted it keeps failing until the entry call returns, so a pcall
        // around an endless loop cannot swallow it for long.
        state.SetHook(Fn("budget", c =>
        {
            if (_exhausted || --_budget < 0)
            {
                _exhausted = true;
                throw new ScriptBudgetException(BudgetMessage);
            }
            return c.Return();
        }), "", HookInterval);
        // pcall/xpcall must not swallow the budget: once it is exhausted they rethrow instead of returning false.
        foreach (var name in (ReadOnlySpan<string>)["pcall", "xpcall"])
        {
            var original = state.Environment[name].Read<LuaFunction>();
            state.Environment[name] = new LuaFunction(name, async (c, ct) =>
            {
                if (_exhausted) throw new ScriptBudgetException(BudgetMessage);
                var results = await c.State.CallAsync(original, c.Arguments, ct).ConfigureAwait(false);
                if (_exhausted) throw new ScriptBudgetException(BudgetMessage);
                return c.Return(results);
            });
        }
        state.Environment["server"] = ServerTable();
        state.Environment["net"] = NetTable();
        return state;
    }

    private string BudgetMessage => $"stopped: more than {_options.InstructionBudget:N0} instructions in one call (endless loop?)";

    private static LuaFunction Fn(string name, Func<LuaFunctionExecutionContext, int> body) =>
        new(name, (context, _) => new ValueTask<int>(body(context)));

    private LuaTable ServerTable()
    {
        var server = new LuaTable();
        server["on"] = Fn("on", c =>
        {
            var name = c.GetArgument<string>(0);
            var function = c.GetArgument<LuaFunction>(1);
            if (!_handlers.TryGetValue(name, out var list)) _handlers[name] = list = new();
            list.Add(new Handler { Function = function, File = _currentFile });
            return c.Return();
        });
        server["every"] = Fn("every", c => AddTimer(c, repeat: true));
        server["after"] = Fn("after", c => AddTimer(c, repeat: false));
        server["broadcast"] = Fn("broadcast", c => { _server.Broadcast(c.GetArgument(0).ToString()); return c.Return(); });
        server["message"] = Fn("message", c => { if (SlotOf(c.GetArgument(0)) is var slot and > 0) _server.Message(slot, c.GetArgument(1).ToString()); return c.Return(); });
        server["kick"] = Fn("kick", c => { if (SlotOf(c.GetArgument(0)) is var slot and > 0) _server.Kick(slot, c.HasArgument(1) ? c.GetArgument(1).ToString() : ""); return c.Return(); });
        server["log"] = Fn("log", c => { Log.Info($"SCRIPT {_currentFile}: {c.GetArgument(0)}"); return c.Return(); });
        server["time"] = Fn("time", c => c.Return(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0));
        server["players"] = Fn("players", c =>
        {
            var players = _server.Players();
            var table = new LuaTable(players.Count, 0);
            for (var i = 0; i < players.Count; i++) table[i + 1] = PlayerTable(players[i]);
            return c.Return(table);
        });
        server["player"] = Fn("player", c => c.Return(_server.Player((int)c.GetArgument<double>(0)) is { } p ? PlayerTable(p) : LuaValue.Nil));
        server["get"] = Fn("get", c =>
        {
            var key = c.GetArgument(1).ToString();
            var store = CurrentStore;
            var found = OwnerOf(c.GetArgument(0)) is { } owner ? store.TryGetHero(owner, key, out var value) : store.TryGet(key, out value);
            return c.Return(found ? ToLua(value) : c.HasArgument(2) ? c.GetArgument(2) : LuaValue.Nil);
        });
        server["set"] = Fn("set", c =>
        {
            var key = c.GetArgument(1).ToString();
            var value = Model(c.HasArgument(2) ? c.GetArgument(2) : LuaValue.Nil);
            if (OwnerOf(c.GetArgument(0)) is { } owner) CurrentStore.SetHero(owner, CharacterOf(c.GetArgument(0)), key, value);
            else CurrentStore.Set(key, value);
            return c.Return();
        });
        server["add"] = Fn("add", c =>
        {
            var key = c.GetArgument(1).ToString();
            var amount = c.HasArgument(2) ? c.GetArgument<double>(2) : 1;
            var store = CurrentStore;
            double total;
            if (OwnerOf(c.GetArgument(0)) is { } owner)
            {
                total = (store.TryGetHero(owner, key, out var value) && value is double d ? d : 0) + amount;
                store.SetHero(owner, CharacterOf(c.GetArgument(0)), key, total);
            }
            else
            {
                total = (store.TryGet(key, out var value) && value is double d ? d : 0) + amount;
                store.Set(key, total);
            }
            return c.Return(total);
        });
        server["save"] = Fn("save", c =>
        {
            var key = c.GetArgument(0).ToString();
            if (key.StartsWith(ModStore.HeroPrefix, StringComparison.Ordinal)) throw new ArgumentException("keys starting with @ are the per-hero values");
            var value = Model(c.HasArgument(1) ? c.GetArgument(1) : LuaValue.Nil);
            if (value is not null && Size(value) > _options.MaxStoreChars) throw new ArgumentException($"value larger than {_options.MaxStoreChars:N0} bytes");
            CurrentStore.Set(key, value);
            return c.Return();
        });
        server["load"] = Fn("load", c => c.Return(CurrentStore.TryGet(c.GetArgument(0).ToString(), out var value) && value is not null
            ? ToLua(value) : c.HasArgument(1) ? c.GetArgument(1) : LuaValue.Nil));
        server["delete"] = Fn("delete", c => { CurrentStore.Set(c.GetArgument(0).ToString(), null); return c.Return(); });
        server["keys"] = Fn("keys", c =>
        {
            var keys = CurrentStore.Keys(c.HasArgument(0) ? c.GetArgument(0).ToString() : "").ToList();
            var table = new LuaTable(keys.Count, 0);
            for (var k = 0; k < keys.Count; k++) table[k + 1] = keys[k];
            return c.Return(table);
        });
        server["creature_name"] = Fn("creature_name", c => c.Return(TypeOf(c) is { } t && _server.CreatureName(t) is { } n ? n : LuaValue.Nil));
        server["item_name"] = Fn("item_name", c => c.Return(TypeOf(c) is { } t && _server.ItemName(t) is { } n ? n : LuaValue.Nil));
        server["drop_item"] = Fn("drop_item", c =>
        {
            var target = c.GetArgument(0);
            var slot = SlotOf(target);
            var options = c.HasArgument(1) && c.GetArgument(1).Type == LuaValueType.Table ? c.GetArgument(1).Read<LuaTable>() : null;
            var player = _server.Player(slot);
            if (player is null) return c.Return(LuaValue.Nil, "no such player");
            double Number(string key, double fallback) => options?[key] is { Type: LuaValueType.Number } n ? n.Read<double>() : fallback;
            Sacred.Core.Pak.Items.SacredItemCategory? category = null;
            if (options?["category"] is { Type: LuaValueType.String } name)
            {
                if (!LootService.Categories.TryGetValue(name.Read<string>(), out var known)) return c.Return(LuaValue.Nil, "unknown category " + name.Read<string>());
                category = known;
            }
            uint? quality = options?["quality"] is { Type: LuaValueType.Number } q ? (uint)Math.Clamp(q.Read<double>(), 0, 14) : null;
            var request = new ScriptLootRequest((int)Number("level", Math.Max(1, player.Level)), (int)Number("class", player.Class), quality, category,
                (uint)Number("seed", Random.Shared.Next()));
            var drop = _server.DropItem(slot, request, out var error);
            if (drop is null) return c.Return(LuaValue.Nil, error ?? "failed");
            return c.Return(new LuaTable(0, 5) { ["ref"] = drop.Reference, ["type"] = drop.Type, ["quality"] = drop.Quality, ["name"] = drop.Name, ["category"] = drop.Category });
        });
        server["drop_gold"] = Fn("drop_gold", c =>
        {
            var slot = SlotOf(c.GetArgument(0));
            if (_server.Player(slot) is null) return c.Return(LuaValue.Nil, "no such player");
            var requested = c.HasArgument(1) && c.GetArgument(1).Type == LuaValueType.Number ? Math.Floor(c.GetArgument(1).Read<double>()) : double.NaN;
            if (!(requested >= 1)) return c.Return(LuaValue.Nil, "amount must be at least 1");
            var amount = (uint)Math.Min(requested, GoldPile.MaximumAmount);
            var drop = _server.DropGold(slot, amount, out var error);
            if (drop is null) return c.Return(LuaValue.Nil, error ?? "failed");
            return c.Return(new LuaTable(0, 3) { ["ref"] = drop.Reference, ["type"] = drop.Type, ["amount"] = amount });
        });
        server["endpoint"] = Fn("endpoint", c => Register(c, page: false));
        server["page"] = Fn("page", c => Register(c, page: true));
        server["describe"] = Fn("describe", c =>
        {
            var text = c.GetArgument(0).ToString().Trim();
            if (_currentFile.Length > 0) _descriptions[ScriptEndpoints.ModName(_currentFile)] = text.Length > 200 ? text[..197] + "..." : text;
            return c.Return();
        });
        server["file"] = Fn("file", c => c.Return(ReadScriptFile(c.GetArgument(0).ToString())));
        server["html"] = Fn("html", c => c.Return(System.Net.WebUtility.HtmlEncode(c.HasArgument(0) ? c.GetArgument(0).ToString() : "")));
        server["render"] = Fn("render", c => c.Return(ScriptEndpoints.Render(c.GetArgument(0).ToString(),
            c.HasArgument(1) && c.GetArgument(1).Type == LuaValueType.Table ? c.GetArgument(1).Read<LuaTable>() : new LuaTable())));
        server["csv"] = Fn("csv", c => c.Return(ScriptEndpoints.Csv(c.GetArgument<LuaTable>(0),
            c.HasArgument(1) && c.GetArgument(1).Type == LuaValueType.Table ? c.GetArgument(1).Read<LuaTable>() : null)));
        server["json"] = Fn("json", c => c.Return(ScriptEndpoints.Json(c.GetArgument<LuaTable>(0))));
        server["top"] = Fn("top", c =>
        {
            var key = c.GetArgument(0).ToString();
            var count = c.HasArgument(1) ? Math.Clamp((int)c.GetArgument<double>(1), 1, 1000) : 10;
            var rows = CurrentStore.Top(key, count);
            var table = new LuaTable(rows.Count, 0);
            for (var k = 0; k < rows.Count; k++) table[k + 1] = new LuaTable(0, 2) { ["name"] = rows[k].Name, ["value"] = rows[k].Value };
            return c.Return(table);
        });
        server["random"] = Fn("random", c =>
        {
            if (!c.HasArgument(0)) return c.Return(Random.Shared.NextDouble());
            var low = c.HasArgument(1) ? (int)c.GetArgument<double>(0) : 1;
            var high = (int)c.GetArgument<double>(c.HasArgument(1) ? 1 : 0);
            return c.Return(high < low ? low : Random.Shared.Next(low, high + 1));
        });
        return server;
    }

    private int AddTimer(LuaFunctionExecutionContext c, bool repeat)
    {
        var seconds = Math.Max(0.05, c.GetArgument<double>(0));
        var interval = (long)(seconds * 1000);
        _newTimers.Add(new Timer { Function = c.GetArgument<LuaFunction>(1), File = _currentFile, DueMs = _server.NowMs + interval, IntervalMs = repeat ? interval : 0 });
        return c.Return();
    }

    /// <summary>First argument as a type number (a number, or a table with "type" such as a monster or item), else null.</summary>
    private static uint? TypeOf(LuaFunctionExecutionContext c)
    {
        var value = c.HasArgument(0) ? c.GetArgument(0) : LuaValue.Nil;
        if (value.Type == LuaValueType.Table) value = value.Read<LuaTable>()["type"];
        return value.Type == LuaValueType.Number && value.Read<double>() is var d && d >= 0 && d <= uint.MaxValue ? (uint)d : null;
    }

    private static int SlotOf(LuaValue value) => value.Type switch
    {
        LuaValueType.Number => (int)value.Read<double>(),
        LuaValueType.Table when value.Read<LuaTable>()["slot"] is { Type: LuaValueType.Number } slot => (int)slot.Read<double>(),
        _ => 0,
    };

    /// <summary>The per-hero owner key of a player table, or null for the mod's plain keys.</summary>
    private static string? OwnerOf(LuaValue value) =>
        value.Type == LuaValueType.Table && value.Read<LuaTable>()["uid"] is { Type: LuaValueType.String } uid ? uid.Read<string>() : null;

    private static string? CharacterOf(LuaValue value) =>
        value.Type == LuaValueType.Table && value.Read<LuaTable>()["character"] is { Type: LuaValueType.String } name ? name.Read<string>() : null;

    /// <summary>A Lua value as storage model: numbers, strings, booleans, lists (a table with only a sequence part) and maps.</summary>
    private static object? Model(LuaValue value, int depth = 0) => value.Type switch
    {
        LuaValueType.Number => value.Read<double>(),
        LuaValueType.Boolean => value.Read<bool>(),
        LuaValueType.String => value.Read<string>(),
        LuaValueType.Table when depth < 32 => ModelTable(value.Read<LuaTable>(), depth + 1),
        LuaValueType.Nil => null,
        _ => throw new ArgumentException($"a {value.Type} cannot be stored"),
    };

    private static object ModelTable(LuaTable table, int depth)
    {
        if (table.HashMapCount == 0)
        {
            var length = table.ArrayLength;
            var list = new List<object?>(length);
            for (var i = 1; i <= length; i++) list.Add(Model(table[i], depth));
            return list;
        }
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        var key = LuaValue.Nil;
        while (table.TryGetNext(key, out var pair))
        {
            key = pair.Key;
            map[pair.Key.Type == LuaValueType.Number ? ModStore.Format(pair.Key.Read<double>()) : pair.Key.ToString()] = Model(pair.Value, depth);
        }
        return map;
    }

    private static LuaValue ToLua(object? value) => value switch
    {
        double d => d, bool b => b, string s => s,
        List<object?> list => ListTable(list),
        Dictionary<string, object?> map => MapTable(map),
        _ => LuaValue.Nil,
    };

    private static LuaTable ListTable(List<object?> list)
    {
        var table = new LuaTable(list.Count, 0);
        for (var i = 0; i < list.Count; i++) table[i + 1] = ToLua(list[i]);
        return table;
    }

    private static LuaTable MapTable(Dictionary<string, object?> map)
    {
        var table = new LuaTable(0, map.Count);
        foreach (var (key, item) in map) table[key] = ToLua(item);
        return table;
    }

    private static long Size(object value)
    {
        using var stream = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream)) ModStore.Write(writer, value, 0);
        return stream.Length;
    }

    /// <summary>server.endpoint / server.page while a script loads: path, HTML string or handler, options.</summary>
    private int Register(LuaFunctionExecutionContext c, bool page)
    {
        var path = ScriptEndpoints.Normalize(c.GetArgument(0).ToString());
        var source = c.GetArgument(1);
        var options = c.HasArgument(2) && c.GetArgument(2).Type == LuaValueType.Table ? c.GetArgument(2).Read<LuaTable>() : null;
        var mod = ScriptEndpoints.ModName(_currentFile);
        if (mod.Length == 0) throw new InvalidOperationException("endpoints and pages are registered while the script loads");
        var function = source.Type == LuaValueType.Function ? source.Read<LuaFunction>() : null;
        var html = page && source.Type == LuaValueType.String ? source.Read<string>() : null;
        if (function is null && html is null) throw new ArgumentException(page ? "a page needs HTML text or a function" : "an endpoint needs a function");
        var description = options?["description"] is { Type: LuaValueType.String } d ? d.Read<string>() : options?["title"] is { Type: LuaValueType.String } t ? t.Read<string>() : null;
        var endpoint = new ScriptEndpoints.Endpoint(mod, path, function, _currentFile, options?["public"].ToBoolean() == true, description, page, html);
        _endpoints[ScriptEndpoints.Key(mod, path)] = endpoint;
        Volatile.Write(ref _endpointList, _endpoints.Values.ToArray());
        return c.Return();
    }

    /// <summary>server.file(name): a text file next to the scripts (pages, templates). Plain file names only, no folders, 2 MB.</summary>
    private string ReadScriptFile(string name)
    {
        if (name.Length == 0 || name.Length > 100 || name[0] == '.' || name.AsSpan().IndexOfAny("/\\:") >= 0 || name.Contains(".."))
            throw new ArgumentException("server.file takes a plain file name from the scripts folder");
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (extension is not (".html" or ".htm" or ".css" or ".js" or ".txt" or ".json" or ".csv" or ".svg"))
            throw new ArgumentException("server.file reads .html, .htm, .css, .js, .txt, .json, .csv or .svg files");
        var path = Path.Combine(Path.GetFullPath(_options.Directory), name);
        var info = new FileInfo(path);
        if (!info.Exists) throw new ArgumentException("no file " + name + " in the scripts folder");
        if (info.Length > 2 * 1024 * 1024) throw new ArgumentException(name + " is larger than 2 MB");
        return File.ReadAllText(path);
    }

    /// <summary>A player for Lua: no account name, no lobby id, no owner key. <c>uid</c> is an HMAC of the hero's identity with this
    /// server's own secret: stable across sessions on this server, useless anywhere else.</summary>
    private LuaTable PlayerTable(ScriptPlayer p)
    {
        var character = ModStore.DisplayName(p.Character ?? p.Name);
        return new(0, 9)
        {
            ["slot"] = p.Slot, ["name"] = character, ["character"] = character, ["uid"] = Uid(p.Key), ["class"] = p.Class, ["level"] = p.Level,
            ["in_game"] = p.InGame, ["host"] = p.IsHost, ["online"] = p.OnlineSeconds,
        };
    }

    private byte[]? _uidSecret;

    /// <summary>The opaque per-server identity of a hero: HMAC-SHA256(server secret, owner key), 16 bytes as hex. The secret is
    /// created once in the data folder (<c>.uid-secret</c>), so ids stay stable across restarts of this server.</summary>
    public string Uid(string key)
    {
        _uidSecret ??= LoadSecret();
        var hash = System.Security.Cryptography.HMACSHA256.HashData(_uidSecret, System.Text.Encoding.UTF8.GetBytes(key));
        return Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }

    private byte[] LoadSecret()
    {
        var path = Path.Combine(_options.DataDirectory, ".uid-secret");
        try
        {
            if (File.Exists(path) && Convert.FromHexString(File.ReadAllText(path).Trim()) is { Length: >= 32 } existing) return existing;
            var created = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            ModStore.EnsureFolder(_options.DataDirectory);
            // Owner-only on Unix (0600); Windows gives the file the folder's ACL.
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(path, options)) stream.Write(System.Text.Encoding.ASCII.GetBytes(Convert.ToHexString(created)));
            return created;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            Log.Info($"SCRIPT: uid secret not stored ({e.Message}); player uids change at the next start.");
            return System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        }
    }
}

/// <summary>A script call ran out of its instruction budget.</summary>
public sealed class ScriptBudgetException(string message) : Exception(message);
