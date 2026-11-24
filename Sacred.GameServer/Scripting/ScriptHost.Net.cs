using System.Text;
using System.Text.Json;
using Lua;

namespace Sacred.GameServer.Scripting;

/// <summary>
/// Custom net messages for scripts (the <c>net</c> table, Wire/NetOpsWire.cs, Session/GameServerHost.Net.cs):
/// <code>
/// local ch = net.channel("auction:bid" [, { server_only = true }])   -- the channel, or nil, message (bad name, id collision)
/// ch:on(function(player, payload) end)                -- a message for the server; payload is a table (JSON) or a byte string
/// ch:send(player | slot | "all", payload)             -- payload: string (bytes) or table (JSON) -> true | nil, message
/// net.supports(player | slot)                         -- the player's computer uses the custom message types (said HELLO)
/// net.transport(player | slot)                        -- "custom", "chat" (channels over the chat carrier only) or nil
/// </code>
/// <c>server_only</c> keeps clients from relaying through the channel. Byte strings hold one byte per character (Latin-1; a string with
/// characters above 255 is sent as UTF-8); use tables for text. Handlers get their own instruction budget (NETOPS_HANDLER_BUDGET) and the
/// usual error accounting: 20 errors in a row switch a handler off.
/// </summary>
public sealed partial class ScriptHost
{
    private sealed class NetChannelEntry
    {
        public required string Name { get; init; }
        public required uint Id { get; init; }
        public readonly List<Handler> Handlers = new();
    }

    private readonly Dictionary<uint, NetChannelEntry> _netChannels = new();

    private void ResetNet()
    {
        _netChannels.Clear();
        try { _server.NetReset(); } catch (Exception e) when (Contained(e)) { }
    }

    private LuaTable NetTable()
    {
        var table = new LuaTable();
        table["channel"] = Fn("channel", c =>
        {
            var name = c.GetArgument(0).ToString();
            var serverOnly = c.HasArgument(1) && c.GetArgument(1).Type == LuaValueType.Table && c.GetArgument(1).Read<LuaTable>()["server_only"].ToBoolean();
            uint id;
            try { id = _server.NetRegister(name, serverOnly); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException) { return c.Return(LuaValue.Nil, e.Message); }
            if (!_netChannels.TryGetValue(id, out var entry)) _netChannels[id] = entry = new NetChannelEntry { Name = name, Id = id };
            return c.Return(ChannelTable(entry));
        });
        table["supports"] = Fn("supports", c => c.Return(_server.NetSupports(SlotOf(c.GetArgument(0)))));
        table["transport"] = Fn("transport", c => c.Return(_server.NetTransport(SlotOf(c.GetArgument(0))) is { } how ? how : LuaValue.Nil));
        return table;
    }

    private LuaTable ChannelTable(NetChannelEntry entry)
    {
        var channel = new LuaTable(0, 4) { ["name"] = entry.Name, ["id"] = entry.Id };
        channel["on"] = Fn("on", c =>
        {
            var function = c.GetArgument<LuaFunction>(1);
            entry.Handlers.Add(new Handler { Function = function, File = _currentFile });
            return c.Return(c.GetArgument(0));
        });
        channel["send"] = Fn("send", c =>
        {
            var target = c.GetArgument(1);
            var slot = target.Type == LuaValueType.String && target.Read<string>() == "all" ? 0 : SlotOf(target);
            if (target.Type != LuaValueType.String && slot < 1) return c.Return(LuaValue.Nil, "to must be a player, a slot or 'all'");
            var payload = c.HasArgument(2) ? c.GetArgument(2) : LuaValue.Nil;
            byte[] data; bool json;
            switch (payload.Type)
            {
                case LuaValueType.String: data = RawBytes(payload.Read<string>()); json = false; break;
                case LuaValueType.Table:
                    try { data = JsonBytes(payload.Read<LuaTable>()); json = true; }
                    catch (ArgumentException e) { return c.Return(LuaValue.Nil, e.Message); }
                    break;
                default: return c.Return(LuaValue.Nil, "payload must be a string or a table");
            }
            var sent = _server.NetSend(slot, entry.Id, json, data, out var error);
            return sent > 0 ? c.Return(true) : c.Return(LuaValue.Nil, error ?? "not sent");
        });
        return channel;
    }

    private static byte[] RawBytes(string text)
    {
        foreach (var ch in text) if (ch > 0xFF) return Encoding.UTF8.GetBytes(text);
        return Encoding.Latin1.GetBytes(text);
    }

    private static byte[] JsonBytes(LuaTable table)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) ModStore.Write(writer, Model(table), 0);
        return stream.ToArray();
    }

    /// <summary>Whether a script listens on <paramref name="channel"/>.</summary>
    public bool HasNetHandler(uint channel) => _netChannels.TryGetValue(channel, out var entry) && entry.Handlers.Count > 0;

    /// <summary>Calls the handlers of a channel with a message a player sent to the server; false when the payload could not be read
    /// or nobody listens.</summary>
    public bool FireNet(uint channel, ScriptPlayer player, bool json, byte[] data)
    {
        if (_state is null || !_netChannels.TryGetValue(channel, out var entry) || entry.Handlers.Count == 0) return false;
        LuaValue payload;
        if (json)
        {
            try { using var document = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 32 }); payload = FromJson(document.RootElement, 0); }
            catch (Exception e) when (e is JsonException or ArgumentException) { return false; }
        }
        else payload = Encoding.Latin1.GetString(data);
        var who = PlayerTable(player);
        for (var i = 0; i < entry.Handlers.Count; i++)
        {
            var handler = entry.Handlers[i];
            _nextBudget = Math.Min(_options.InstructionBudget, _server.NetHandlerBudget);
            if (Call(handler.Function, handler.File, Two(who, payload), ref handler.Errors, out _)) continue;
            if (handler.Errors < MaxConsecutiveErrors) continue;
            Log.Info($"SCRIPT: {handler.File}: net channel '{entry.Name}' handler disabled after {MaxConsecutiveErrors} errors in a row.");
            entry.Handlers.RemoveAt(i--);
        }
        return true;
    }

    private static LuaValue FromJson(JsonElement element, int depth) => element.ValueKind switch
    {
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => element.GetString() ?? "",
        JsonValueKind.Array when depth < 32 => JsonList(element, depth),
        JsonValueKind.Object when depth < 32 => JsonMap(element, depth),
        _ => LuaValue.Nil,
    };

    private static LuaTable JsonList(JsonElement element, int depth)
    {
        var table = new LuaTable(element.GetArrayLength(), 0);
        var i = 1;
        foreach (var item in element.EnumerateArray()) table[i++] = FromJson(item, depth + 1);
        return table;
    }

    private static LuaTable JsonMap(JsonElement element, int depth)
    {
        var table = new LuaTable(0, 4);
        foreach (var property in element.EnumerateObject()) table[property.Name] = FromJson(property.Value, depth + 1);
        return table;
    }
}
