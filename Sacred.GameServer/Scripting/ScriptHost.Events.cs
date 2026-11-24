using Lua;

namespace Sacred.GameServer.Scripting;

/// <summary>
/// Hooks between mods, the same bus as Sacred Gear's client mods (gear.emit / gear.on / gear.events), so a bounty board here and
/// the one in the game publish the same <c>bounty.*</c> events with the same payload tables:
/// <code>
/// server.events.declare("bounty.completed", { doc = "A bounty is done", args = "id, title, target, player" })
/// server.emit("bounty.completed", { id = "b1", title = "Orcs", target = 10, source = "server", player = "Aria" })
/// server.on("bounty.completed", function(ev) end)         -- subscribe: any mod, in any load order
/// server.on("bounty.*", function(event, ev) end)          -- every event of one mod: the event name comes first
/// server.events.list([prefix]) -> { { name, mod, doc, args, listeners }, ... }   server.events.declared(name) -> bool
/// </code>
/// Names are <c>&lt;mod&gt;.&lt;what&gt;</c> (a dot; the server's own events have none, so they cannot be emitted). A handler's
/// error is logged and does not reach the publisher or the other handlers. Emits may nest 8 levels. Subscriptions and declarations
/// belong to the mod's file: they go when the mod unloads or reloads (<see cref="UnloadFile"/>), whatever the order, and
/// subscribers do not depend on a publisher being loaded.
/// </summary>
public sealed partial class ScriptHost
{
    private sealed record Declared(string File, string Doc, string Args);

    private readonly Dictionary<string, Declared> _declared = new(StringComparer.Ordinal);
    private int _emitDepth;
    private const int MaxEmitDepth = 8;

    /// <summary>The hooks declared by loaded mods (name, mod, doc, args, listeners), by name. Diagnostics and tests.</summary>
    public IReadOnlyList<(string Name, string Mod, string Doc, string Args, int Listeners)> DeclaredEvents(string? prefix = null) =>
        _declared.Where(p => prefix is null || p.Key.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => (p.Key, ModName(p.Value.File), p.Value.Doc, p.Value.Args, _handlers.TryGetValue(p.Key, out var l) ? l.Count : 0)).ToArray();

    private static bool ValidEventName(string name)
    {
        var dot = name.IndexOf('.');
        if (dot <= 0 || dot == name.Length - 1) return false;
        foreach (var ch in name) if (!(char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-' or '.')) return false;
        return true;
    }

    /// <summary>Calls the handlers of a custom event: the exact name, then each <c>prefix.*</c> up the dots.</summary>
    private void Emit(string name, LuaValue payload)
    {
        if (_emitDepth >= MaxEmitDepth) throw new LuaRuntimeException(default, "events nested too deeply");
        _emitDepth++;
        try
        {
            Fire(name, payload, default, 1);
            for (var dot = name.IndexOf('.'); dot >= 0; dot = name.IndexOf('.', dot + 1))
                Fire(name[..(dot + 1)] + "*", name, payload, 2);
        }
        finally { _emitDepth--; }
    }

    private void AddEvents(LuaTable server)
    {
        server["emit"] = Fn("emit", c =>
        {
            var name = c.GetArgument(0).ToString();
            if (!ValidEventName(name) || name.Contains('*')) throw new LuaRuntimeException(default, "custom event names have a dot (\"bounty.completed\")");
            // a handler's Call resets the file and gives itself a fresh instruction budget: the emitting script keeps its own
            var file = _currentFile; var budget = _budget; var exhausted = _exhausted; var next = _nextBudget;
            try { Emit(name, c.HasArgument(1) ? c.GetArgument(1) : LuaValue.Nil); }
            finally { _currentFile = file; _budget = budget; _exhausted = exhausted; _nextBudget = next; }
            return c.Return();
        });
        var events = new LuaTable(0, 3);
        events["declare"] = Fn("declare", c =>
        {
            var name = c.GetArgument(0).ToString();
            if (!ValidEventName(name) || name.Contains('*')) throw new LuaRuntimeException(default, "declare a custom event name with a dot, not a pattern");
            var info = c.HasArgument(1) ? c.GetArgument(1) : LuaValue.Nil;
            string doc = "", args = "";
            if (info.Type == LuaValueType.Table)
            {
                var t = info.Read<LuaTable>();
                doc = t["doc"].Type == LuaValueType.String ? t["doc"].Read<string>() : "";
                args = t["args"].Type == LuaValueType.String ? t["args"].Read<string>() : "";
            }
            else if (info.Type == LuaValueType.String) doc = info.Read<string>();
            if (_declared.TryGetValue(name, out var old) && old.File != _currentFile)
                throw new LuaRuntimeException(default, $"{name} is declared by mod {ModName(old.File)}");
            _declared[name] = new Declared(_currentFile, doc, args);
            return c.Return();
        });
        events["declared"] = Fn("declared", c => c.Return(_declared.ContainsKey(c.GetArgument(0).ToString())));
        events["list"] = Fn("list", c =>
        {
            var prefix = c.HasArgument(0) && c.GetArgument(0).Type == LuaValueType.String ? c.GetArgument(0).Read<string>() : null;
            var result = new LuaTable();
            var i = 1;
            foreach (var d in DeclaredEvents(prefix))
                result[i++] = new LuaTable(0, 5) { ["name"] = d.Name, ["mod"] = d.Mod, ["doc"] = d.Doc, ["args"] = d.Args, ["listeners"] = d.Listeners };
            return c.Return(result);
        });
        server["events"] = events;
    }

    /// <summary>A mod unloads: its declarations go with its handlers (those are dropped by <see cref="UnloadFile"/>).</summary>
    private void DropDeclarations(string? file)
    {
        if (file is null) { _declared.Clear(); return; }
        foreach (var name in _declared.Where(p => p.Value.File == file).Select(p => p.Key).ToArray()) _declared.Remove(name);
    }
}
