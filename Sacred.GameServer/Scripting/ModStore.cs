using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Sacred.GameServer.Scripting;

/// <summary>
/// One mod's own storage: a key-value file <c>{data folder}/{mod}.json</c>. Values are what Lua can hold: numbers, strings,
/// booleans and nested tables (lists and maps), kept here as <see cref="double"/>, <see cref="string"/>, <see cref="bool"/>,
/// <see cref="List{T}"/> and <see cref="Dictionary{TKey,TValue}"/> and written with <see cref="Utf8JsonWriter"/> (no reflection,
/// Native AOT safe). Per-hero values live under keys starting with <see cref="HeroPrefix"/>, each an object that also holds the
/// hero's character name for ladders. Files are written to a temporary file and moved over the old one. The game server itself
/// keeps no mod state: a mod that wants a ladder, a list of recent deaths or anything else stores it here.
/// </summary>
public sealed partial class ModStore
{
    public const string HeroPrefix = "@";
    public const string NameKey = "_name";
    public const string UnknownName = "unknown hero";
    private readonly string _path;
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _sizes = new(StringComparer.Ordinal);
    private bool _dirty;
    private Task _writing = Task.CompletedTask;

    /// <summary>Serialized size of everything stored in UTF-8 bytes (the compact JSON, close to the file size); kept up to date per key.</summary>
    public long Size { get; private set; }

    public ModStore(string path) { _path = path; Load(); }

    public string Path => _path;
    public int Count => _values.Count;
    /// <summary>Largest file a mod may keep (UTF-8 bytes of its JSON); saves beyond it fail with an error to the script.</summary>
    public long MaxChars { get; init; } = 8 * 1024 * 1024;
    /// <summary>Optional check across all mods (the host's total limit): gets the size change, throws to refuse it.</summary>
    public Action<long>? BeforeGrow { get; init; }

    // ---- plain keys --------------------------------------------------------------------------------------------------

    public bool TryGet(string key, out object? value) => _values.TryGetValue(key, out value);

    public void Set(string key, object? value)
    {
        if (value is null) { if (_values.Remove(key)) { Account(key, 0); _dirty = true; } return; }
        Account(key, Measure(key, value), check: true);
        _values[key] = value;
        _dirty = true;
    }

    /// <summary>Records the new size of <paramref name="key"/>; with <paramref name="check"/> refuses growth past the limits first.</summary>
    private void Account(string key, long size, bool check = false)
    {
        var old = _sizes.GetValueOrDefault(key);
        if (check && size > old)
        {
            if (Size - old + size > MaxChars)
                throw new InvalidOperationException($"mod storage full: {Size - old + size:N0} of {MaxChars:N0} bytes (delete old data or raise --scripts-store-max)");
            BeforeGrow?.Invoke(size - old);
        }
        Size += size - old;
        if (size == 0) _sizes.Remove(key); else _sizes[key] = size;
    }

    private static long Measure(string key, object? value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) { writer.WriteStartObject(); writer.WritePropertyName(key); Write(writer, value, 0); writer.WriteEndObject(); }
        return stream.Length;
    }

    public IEnumerable<string> Keys(string prefix) => _values.Keys.Where(k => !k.StartsWith(HeroPrefix, StringComparison.Ordinal) && k.StartsWith(prefix, StringComparison.Ordinal))
        .OrderBy(k => k, StringComparer.Ordinal);

    // ---- per hero ------------------------------------------------------------------------------------------------------

    public bool TryGetHero(string owner, string key, out object? value)
    {
        value = null;
        return _values.TryGetValue(HeroPrefix + owner, out var map) && map is Dictionary<string, object?> hero && hero.TryGetValue(key, out value);
    }

    public void SetHero(string owner, string? character, string key, object? value)
    {
        if (!_values.TryGetValue(HeroPrefix + owner, out var map) || map is not Dictionary<string, object?> hero)
            _values[HeroPrefix + owner] = hero = new Dictionary<string, object?>(StringComparer.Ordinal);
        var name = character is { Length: > 0 } ? DisplayName(character) : null;
        var previousName = hero.GetValueOrDefault(NameKey);
        var hadValue = hero.TryGetValue(key, out var previous);
        if (name is not null) hero[NameKey] = name;
        if (value is null) hero.Remove(key); else hero[key] = value;
        try { Account(HeroPrefix + owner, Measure(HeroPrefix + owner, hero), check: true); }
        catch
        {
            if (hadValue) hero[key] = previous; else hero.Remove(key);                // refused: leave the hero as it was
            if (name is not null) { if (previousName is null) hero.Remove(NameKey); else hero[NameKey] = previousName; }
            throw;
        }
        _dirty = true;
    }

    /// <summary>The heroes with the highest number under <paramref name="key"/>, with their character names (never the owner key).</summary>
    public List<(string Owner, string Name, double Value)> Top(string key, int count)
    {
        var rows = new List<(string, string, double)>();
        foreach (var (stored, map) in _values)
        {
            if (!stored.StartsWith(HeroPrefix, StringComparison.Ordinal) || map is not Dictionary<string, object?> hero || hero.GetValueOrDefault(key) is not double value) continue;
            rows.Add((stored[HeroPrefix.Length..], hero.GetValueOrDefault(NameKey) is string name ? DisplayName(name) : UnknownName, value));
        }
        rows.Sort((a, b) => b.Item3.CompareTo(a.Item3));
        if (rows.Count > count) rows.RemoveRange(count, rows.Count - count);
        return rows;
    }

    /// <summary>A name that is safe to show: never an owner key. The game server already strips the "account." prefix of lobby
    /// names (and only of lobby names), so dots in character names stay ("Mr.X").</summary>
    public static string DisplayName(string name) =>
        name.Length == 0 || name.StartsWith("id:", StringComparison.Ordinal) || name.StartsWith("name:", StringComparison.Ordinal) ||
        name.StartsWith(HeroPrefix, StringComparison.Ordinal) ? UnknownName : name;

    // ---- file ------------------------------------------------------------------------------------------------------------

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(_path), new JsonDocumentOptions { MaxDepth = 64 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var value = FromJson(property.Value);
                _values[property.Name] = value;
                Account(property.Name, Measure(property.Name, value));
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Info($"SCRIPT: mod data '{_path}' not read ({e.Message}); starting empty, the old file is kept as .bad.");
            try { File.Copy(_path, _path + ".bad", overwrite: true); } catch (IOException) { }
            _values.Clear();
        }
    }

    public static object? FromJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => element.EnumerateArray().Select(FromJson).ToList(),
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => FromJson(p.Value), StringComparer.Ordinal),
        _ => null,
    };

    /// <summary>Creates the data folder with a <c>.gitignore</c> of "*" in it, so a server started inside a git checkout never gets
    /// mod data or the uid secret committed, wherever the folder is.</summary>
    public static void EnsureFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        var ignore = System.IO.Path.Combine(folder, ".gitignore");
        if (!File.Exists(ignore)) File.WriteAllText(ignore, "# Server data (mod storage, uid secret): never commit.\n*\n");
    }

    /// <summary>The whole store as JSON (also used to check the size limit).</summary>
    public string Serialize()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, SkipValidation = false }))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in _values.OrderBy(v => v.Key, StringComparer.Ordinal)) { writer.WritePropertyName(key); Write(writer, value, 0); }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    public static void Write(Utf8JsonWriter writer, object? value, int depth)
    {
        switch (value)
        {
            case double d when double.IsFinite(d): writer.WriteNumberValue(d); break;
            case string s: writer.WriteStringValue(s); break;
            case bool b: writer.WriteBooleanValue(b); break;
            case List<object?> list when depth < 32:
                writer.WriteStartArray();
                foreach (var item in list) Write(writer, item, depth + 1);
                writer.WriteEndArray();
                break;
            case Dictionary<string, object?> map when depth < 32:
                writer.WriteStartObject();
                foreach (var (key, item) in map) { writer.WritePropertyName(key); Write(writer, item, depth + 1); }
                writer.WriteEndObject();
                break;
            default: writer.WriteNullValue(); break;
        }
    }

    /// <summary>Writes the store if it changed: serialized on the calling (game loop) thread, which the size limit keeps short,
    /// written to disk (temporary file + move) on a worker thread. A write still running skips this flush; the next one catches up.
    /// <paramref name="wait"/> (shutdown) waits for the write.</summary>
    public void Flush(bool wait = false)
    {
        if (!wait && DurablePending) return;                                // a durable write is out: its outcome decides (ModStore.Durable.cs)
        if (!_writing.IsCompleted) { if (!wait) return; _writing.Wait(); }
        if (_dirty)
        {
            var text = Serialize();
            _dirty = false;
            _writing = Task.Run(() => WriteFile(text));
        }
        if (wait) _writing.Wait();
    }

    private void WriteFile(string text)
    {
        var temporary = _path + ".tmp";
        try
        {
            var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_path));
            if (directory is not null) EnsureFolder(directory);
            File.WriteAllText(temporary, text);
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _dirty = true;                                                   // try again at the next flush
            Log.Info($"SCRIPT: mod data '{_path}' not written: {e.Message}");
        }
    }

    internal static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
