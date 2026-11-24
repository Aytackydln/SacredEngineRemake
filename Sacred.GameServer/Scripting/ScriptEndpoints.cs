using System.Globalization;
using System.Text;
using System.Text.Json;
using Lua;

namespace Sacred.GameServer.Scripting;

/// <summary>An HTTP answer of a script endpoint.</summary>
public readonly record struct ScriptResponse(int Status, string ContentType, string Body, bool Page = false);

/// <summary>
/// HTTP endpoints of server scripts. A script registers <c>server.endpoint(path, fn, { public = true, description = "..." })</c>;
/// it is served at <c>/mods/{script file name without .lua}/{path}</c> by the web UI (admin password), and also at
/// <c>/public/mods/...</c> when marked public and the public API is on. The handler gets <c>{ path, query = { name = value } }</c>
/// and returns a string (plus an optional content type and status: CSV, plain text, HTML ...) or a table, sent as JSON.
/// Runs on the game loop thread like every other handler, with the same instruction budget.
/// </summary>
public static class ScriptEndpoints
{
    /// <summary>An endpoint or a page (<see cref="Page"/>): a handler, or for a static page its <see cref="Html"/>.</summary>
    public sealed record Endpoint(string Mod, string Path, LuaFunction? Function, string File, bool Public, string? Description, bool Page = false, string? Html = null);

    /// <summary>A mod page: text/html, sent by the web UI inside a sandbox (opaque origin) with a strict Content-Security-Policy.</summary>
    public static ScriptResponse Page(string html, int maxChars) =>
        html.Length > maxChars ? TooLarge : new(200, "text/html; charset=utf-8", html, Page: true);

    /// <summary>
    /// <c>server.render(template, values)</c>: <c>{{name}}</c> becomes the HTML-escaped value, <c>{{json name}}</c> the value as JSON
    /// that is safe inside a script element (System.Text.Json escapes &lt;, &gt;, &amp; and quotes). Unknown names become empty.
    /// Values may be nested: <c>{{player.name}}</c>.
    /// </summary>
    public static string Render(string template, LuaTable values)
    {
        var sb = new StringBuilder(template.Length + 256);
        var position = 0;
        while (position < template.Length)
        {
            var open = template.IndexOf("{{", position, StringComparison.Ordinal);
            if (open < 0) break;
            var close = template.IndexOf("}}", open + 2, StringComparison.Ordinal);
            if (close < 0) break;
            sb.Append(template, position, open - position);
            var token = template.Substring(open + 2, close - open - 2).Trim();
            var json = token.StartsWith("json ", StringComparison.Ordinal);
            var value = Lookup(values, json ? token[5..].Trim() : token);
            if (json) sb.Append(value.Type == LuaValueType.Table ? Json(value.Read<LuaTable>()) : JsonScalar(value));
            else sb.Append(System.Net.WebUtility.HtmlEncode(value.Type switch
            {
                LuaValueType.Nil => "",
                LuaValueType.Number => value.Read<double>().ToString(CultureInfo.InvariantCulture),
                _ => value.ToString(),
            }));
            position = close + 2;
        }
        sb.Append(template, position, template.Length - position);
        return sb.ToString();
    }

    private static LuaValue Lookup(LuaTable values, string path)
    {
        LuaValue current = values;
        foreach (var part in path.Split('.'))
        {
            if (current.Type != LuaValueType.Table) return LuaValue.Nil;
            current = current.Read<LuaTable>()[part];
        }
        return current;
    }

    private static string JsonScalar(LuaValue value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(writer, value, 0);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string Key(string mod, string path) => mod.ToLowerInvariant() + "/" + Normalize(path);

    public static string Normalize(string path) => path.Trim().Trim('/').ToLowerInvariant();

    public static string ModName(string file) => System.IO.Path.GetFileNameWithoutExtension(file).ToLowerInvariant();

    /// <summary>Content types a script may answer with; anything else is sent as text/plain. HTML stays inert: the web UI sends
    /// script answers with a Content-Security-Policy of default-src none.</summary>
    private static readonly string[] AllowedTypes = ["application/json", "text/plain", "text/csv", "text/html"];

    public static ScriptResponse TooLarge => new(500, "text/plain; charset=utf-8", "response too large");

    /// <summary>
    /// Turns a handler's return values into a response: status 200..499 only (anything else is a script error, 500), no body for
    /// 204/304, a whitelisted content type, and at most <paramref name="maxChars"/> characters of body (500 "response too large").
    /// </summary>
    public static ScriptResponse Response(ReadOnlySpan<LuaValue> values, int maxChars)
    {
        if (values.Length == 0 || values[0].Type == LuaValueType.Nil) return new(204, "text/plain; charset=utf-8", "");
        var status = 200;
        if (values.Length > 2 && values[2].Type == LuaValueType.Number)
        {
            var asked = values[2].Read<double>();
            if (!(asked >= 200 && asked <= 499) || asked != Math.Floor(asked)) return new(500, "text/plain; charset=utf-8", "script returned an invalid status");
            status = (int)asked;
        }
        string type, body;
        if (values[0].Type == LuaValueType.Table)
        {
            type = "application/json";
            body = Json(values[0].Read<LuaTable>(), maxChars);
            if (body.Length > maxChars) return TooLarge;
        }
        else
        {
            var text = values[0].Type == LuaValueType.String ? values[0].Read<string>() : values[0].ToString();
            if (text.Length > maxChars) return TooLarge;
            var asked = values.Length > 1 && values[1].Type == LuaValueType.String ? values[1].Read<string>() : "text/plain";
            var media = asked.Split(';', 2)[0].Trim().ToLowerInvariant();
            type = Array.IndexOf(AllowedTypes, media) >= 0 ? media : "text/plain";
            body = text;
        }
        if (status is 204 or 304) body = "";
        return new(status, type + "; charset=utf-8", body);
    }

    /// <summary>A Lua table as JSON: a table with only a sequence part (or nothing) is an array, anything else an object. Depth is limited.</summary>
    public static string Json(LuaTable table, int maxChars = int.MaxValue)
    {
        using var stream = new CappedStream(maxChars);
        try { using (var writer = new Utf8JsonWriter(stream)) Write(writer, table, 0); }
        catch (CappedStream.FullException) { return new string(' ', maxChars + 1); }   // the caller sees "too large"
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    /// <summary>A memory stream that refuses to grow past a limit, so a huge table never becomes a huge buffer.</summary>
    private sealed class CappedStream(int limit) : MemoryStream
    {
        public sealed class FullException : Exception;
        public override void Write(byte[] buffer, int offset, int count) { if (Length + count > limit) throw new FullException(); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { if (Length + buffer.Length > limit) throw new FullException(); base.Write(buffer); }
        public override void WriteByte(byte value) { if (Length + 1 > limit) throw new FullException(); base.WriteByte(value); }
    }

    private static void Write(Utf8JsonWriter writer, LuaValue value, int depth)
    {
        switch (value.Type)
        {
            case LuaValueType.Nil: writer.WriteNullValue(); break;
            case LuaValueType.Boolean: writer.WriteBooleanValue(value.Read<bool>()); break;
            case LuaValueType.Number:
                var number = value.Read<double>();
                if (double.IsFinite(number)) writer.WriteNumberValue(number); else writer.WriteNullValue();
                break;
            case LuaValueType.String: writer.WriteStringValue(value.Read<string>()); break;
            case LuaValueType.Table when depth < 20: Write(writer, value.Read<LuaTable>(), depth + 1); break;
            default: writer.WriteStringValue(value.ToString()); break;
        }
    }

    private static void Write(Utf8JsonWriter writer, LuaTable table, int depth)
    {
        if (table.HashMapCount == 0)                                         // also the empty table: an empty list
        {
            writer.WriteStartArray();
            var length = table.ArrayLength;                               // not per iteration: it is not O(1)
            for (var i = 1; i <= length; i++) { Write(writer, table[i], depth); if (writer.BytesPending > 65536) writer.Flush(); }
            writer.WriteEndArray();
            return;
        }
        writer.WriteStartObject();
        var key = LuaValue.Nil;
        while (table.TryGetNext(key, out var pair))
        {
            key = pair.Key;
            writer.WritePropertyName(pair.Key.Type == LuaValueType.Number
                ? pair.Key.Read<double>().ToString(CultureInfo.InvariantCulture) : pair.Key.ToString());
            Write(writer, pair.Value, depth);
            if (writer.BytesPending > 65536) writer.Flush();                // reach the size cap early, not after buffering it all
        }
        writer.WriteEndObject();
    }

    /// <summary><c>server.csv(rows [, columns])</c>: rows are tables; with columns, a header line and those fields in that order,
    /// otherwise the sequence part of each row. Fields with commas, quotes or line breaks are quoted.</summary>
    public static string Csv(LuaTable rows, LuaTable? columns)
    {
        var sb = new StringBuilder();
        var names = new List<string>();
        if (columns is not null) { var count = columns.ArrayLength; for (var i = 1; i <= count; i++) names.Add(columns[i].ToString()); }
        if (names.Count > 0) { AppendLine(sb, names); }
        var fields = new List<string>();
        var rowCount = rows.ArrayLength;
        for (var r = 1; r <= rowCount; r++)
        {
            if (rows[r].Type != LuaValueType.Table) continue;
            var row = rows[r].Read<LuaTable>();
            fields.Clear();
            if (names.Count > 0) foreach (var name in names) fields.Add(Text(row[name]));
            else { var count = row.ArrayLength; for (var i = 1; i <= count; i++) fields.Add(Text(row[i])); }
            AppendLine(sb, fields);
        }
        return sb.ToString();
    }

    private static string Text(LuaValue value) => value.Type switch
    {
        LuaValueType.Nil => "",
        LuaValueType.Number => value.Read<double>().ToString(CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static void AppendLine(StringBuilder sb, List<string> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var field = fields[i];
            if (field.AsSpan().IndexOfAny(",\"\r\n") >= 0) sb.Append('"').Append(field.Replace("\"", "\"\"")).Append('"');
            else sb.Append(field);
        }
        sb.Append("\r\n");
    }
}
