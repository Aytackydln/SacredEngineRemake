using Lua;

namespace Sacred.GameServer.Scripting;

/// <summary>
/// For scripts that move items and gold between a hero and their own storage on a server that keeps the heroes (Closed Net with
/// <c>--authoritative</c>; <see cref="IScriptServer.HeroSavedByServer"/>):
/// <code>
/// server.authoritative(player)          -- true: the server writes this hero (its saves are the truth, not the client's)
/// server.hero_item(player, ref)         -- { ref, type, where = "backpack"|"stash"|"equipped"|"world"|"gone", records, age } or nil
/// server.hero_items(player)             -- the items in the hero's inventories and equipment, same fields
/// server.claim_item(player, ref)        -- takes a destroyed ("gone") item off the list, or consumes the item token a script minted for an item
///                                          that arrived in the hero (same type and records): true once
/// server.item_token(player, {data=hex export | type=, records=hex, label=}, ttl_seconds=120) -- id | nil, err: a one-time permission for the
///                                          player's client to create that item (see --auth-strict-items); revoke_item_token(id)
/// server.save_hero(player, fn)          -- saves the hero now; fn(ok, err) once the lobby stored it (or refused, or 30 s passed)
/// server.hero_upload_time(player)       -- unix seconds of the last save sent for this hero (also earlier sessions), or nil
/// </code>
/// <c>records</c> is the item's value records as hex (one 130-byte block per socketed rune/gem, then the item's own block), the bytes
/// Sacred Gear's item export carries after its type and count. Callbacks run on the game loop at a later pump, in any order.
/// </summary>
public sealed partial class ScriptHost
{
    private sealed class HeroSave
    {
        public required Task<string?> Task;
        public required LuaFunction? Function;
        public required string File;
        public required LuaState State;
        public int Errors;
    }

    private readonly List<HeroSave> _heroSaves = new();

    /// <summary>Hero saves started by scripts and not yet answered (tests, diagnostics).</summary>
    public int HeroSavesPending => _heroSaves.Count;

    private void AddHeroes(LuaTable server)
    {
        server["authoritative"] = Fn("authoritative", c => c.Return(SlotOf(c.GetArgument(0)) is var slot and > 0 && _server.HeroSavedByServer(slot)));
        server["hero_item"] = Fn("hero_item", c =>
        {
            var slot = SlotOf(c.GetArgument(0));
            var reference = c.HasArgument(1) && c.GetArgument(1).Type == LuaValueType.Number ? c.GetArgument<double>(1) : -1;
            if (slot < 1 || reference is < 1 or > 0x7FFFFFFF) return c.Return(LuaValue.Nil);
            return c.Return(_server.HeroItem(slot, (uint)reference) is { } item ? ItemTable(item) : LuaValue.Nil);
        });
        server["hero_items"] = Fn("hero_items", c =>
        {
            var slot = SlotOf(c.GetArgument(0));
            var items = slot > 0 ? _server.HeroItems(slot) : [];
            var table = new LuaTable(items.Count, 0);
            for (var i = 0; i < items.Count; i++) table[i + 1] = ItemTable(items[i]);
            return c.Return(table);
        });
        server["claim_item"] = Fn("claim_item", c =>
        {
            var slot = SlotOf(c.GetArgument(0));
            var reference = c.HasArgument(1) && c.GetArgument(1).Type == LuaValueType.Number ? c.GetArgument<double>(1) : -1;
            return c.Return(slot > 0 && reference is >= 1 and <= 0x7FFFFFFF && _server.ClaimGoneItem(slot, (uint)reference));
        });
        server["item_token"] = Fn("item_token", c =>
        {
            var slot = SlotOf(c.GetArgument(0));
            if (slot < 1 || !c.HasArgument(1) || c.GetArgument(1).Type != LuaValueType.Table) return c.Return(LuaValue.Nil, "a player and a table {data=hex export} or {type=, records=hex} are needed");
            var spec = c.GetArgument<LuaTable>(1);
            uint type; string records;
            if (spec["data"].Type == LuaValueType.String)
            {
                if (!ExportParts(spec["data"].Read<string>(), out type, out records)) return c.Return(LuaValue.Nil, "the item data is not a Sacred Gear export");
            }
            else if (spec["type"].Type == LuaValueType.Number && spec["records"].Type == LuaValueType.String)
            {
                type = (uint)spec["type"].Read<double>(); records = spec["records"].Read<string>().ToLowerInvariant();
            }
            else return c.Return(LuaValue.Nil, "{data=hex export} or {type=, records=hex} is needed");
            var ttl = c.HasArgument(2) && c.GetArgument(2).Type == LuaValueType.Number ? c.GetArgument<double>(2) : 120;
            var label = spec["label"].Type == LuaValueType.String ? spec["label"].Read<string>() : _currentFile;
            var id = _server.MintItemToken(slot, type, records, ttl, label);
            return id > 0 ? c.Return((double)id) : c.Return(LuaValue.Nil, "no token (the server does not keep this hero, or bad arguments)");
        });
        server["revoke_item_token"] = Fn("revoke_item_token", c =>
            c.Return(c.HasArgument(0) && c.GetArgument(0).Type == LuaValueType.Number && _server.RevokeItemToken((long)c.GetArgument<double>(0))));
        server["save_hero"] = Fn("save_hero", c =>
        {
            var slot = SlotOf(c.GetArgument(0));
            if (slot < 1) return c.Return(LuaValue.Nil, "a player or a slot is needed");
            var function = c.HasArgument(1) && c.GetArgument(1).Type == LuaValueType.Function ? c.GetArgument(1).Read<LuaFunction>() : null;
            Task<string?> task;
            try { task = _server.SaveHeroNow(slot); }
            catch (Exception e) when (e is not OutOfMemoryException) { task = Task.FromResult<string?>(e.Message); }
            _heroSaves.Add(new HeroSave { Task = task, Function = function, File = _currentFile, State = _state! });
            return c.Return(true);
        });
        server["hero_upload_time"] = Fn("hero_upload_time", c =>
            c.Return(SlotOf(c.GetArgument(0)) is var slot and > 0 && _server.HeroUploadTime(slot) is { } t ? t : LuaValue.Nil));
    }

    /// <summary>Type and value records (hex) of a Sacred Gear export: u32 type, u16 count, count x 130-byte records (260 hex), ...</summary>
    internal static bool ExportParts(string hex, out uint type, out string records)
    {
        type = 0; records = "";
        if (hex.Length < 12 || hex.Any(ch => !Uri.IsHexDigit(ch))) return false;
        hex = hex.ToLowerInvariant();
        var count = Convert.ToInt32(hex.Substring(8, 2), 16) + Convert.ToInt32(hex.Substring(10, 2), 16) * 256;
        if (count is < 1 or > 9 || hex.Length < 12 + count * 260) return false;
        type = 0;
        for (var b = 3; b >= 0; b--) type = type << 8 | Convert.ToUInt32(hex.Substring(b * 2, 2), 16);
        records = hex.Substring(12, count * 260);
        return true;
    }

    private static LuaTable ItemTable(ScriptHeroItem item) => new(0, 5)
    {
        ["ref"] = item.Reference, ["type"] = item.Type, ["where"] = item.Where, ["records"] = item.Records, ["age"] = item.AgeSeconds,
    };

    /// <summary>Game loop: answers finished hero saves.</summary>
    private void PumpHeroSaves()
    {
        for (var i = 0; i < _heroSaves.Count; i++)
        {
            var save = _heroSaves[i];
            if (!save.Task.IsCompleted) continue;
            _heroSaves.RemoveAt(i--);
            var error = save.Task.IsCompletedSuccessfully ? save.Task.Result : save.Task.Exception?.GetBaseException().Message ?? "save failed";
            if (save.Function is null || !ReferenceEquals(save.State, _state)) continue;
            _two[0] = error is null; _two[1] = error is null ? LuaValue.Nil : error;
            Call(save.Function, save.File, _two, ref save.Errors, out _);
        }
    }
}
