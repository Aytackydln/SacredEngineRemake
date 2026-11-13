namespace Sacred.GameplayHost.Scripting;
/// <summary>Additional SetVar/AddVar writes to Gold's 150-word interpreter array at +0x7750.
/// The optional baseline must come from established reset/loaded state; mutations cannot invent previous values.</summary>
public sealed class WorldScriptVariableGlobals
{
    public const int ValueCount = Sacred.Core.GameBin.Scripts.SacredScriptVariableGlobalsLayout.Count;
    private const string Prefix = "atmo_rg";
    private readonly List<WorldScriptVariableGlobalMutation> _mutations = [];
    public WorldScriptVariableGlobals() => Mutations = _mutations.AsReadOnly();
    public WorldScriptVariableGlobals(IReadOnlyList<int> initial) : this()
    {
        if(initial.Count!=ValueCount)throw new ArgumentException("Expected original 150-word initialization.",nameof(initial));
        InitialValues=Array.AsReadOnly(initial.ToArray());
    }
    public IReadOnlyList<int>? InitialValues { get; }
    public int[] Apply() => Apply(InitialValues ?? throw new InvalidDataException("Variable globals require original reset initialization."));
    public IReadOnlyList<WorldScriptVariableGlobalMutation> Mutations { get; }
    public int UnresolvedIndexCount => _mutations.Count(mutation => mutation.Index is null);
    /// <summary>Track final-value certainty independently of destination certainty.
    /// Unknown writes join every possible target with its unchanged value; later
    /// known assignments can establish a value again. No destination is invented.</summary>
    public WorldScriptVariableGlobalAnalysis Analyze() => Analyze(InitialValues ??
        throw new InvalidDataException("Variable globals require original reset initialization."));

    public WorldScriptVariableGlobalAnalysis Analyze(IReadOnlyList<int> initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        if (initial.Count != ValueCount) throw new ArgumentException("Native variable globals require 150 established values.", nameof(initial));
        var values = initial.Select(value => (int?)value).ToArray();
        foreach (var mutation in _mutations)
        {
            if (mutation.Index is { } index)
                values[index] = mutation.Increment
                    ? values[index] is { } previous ? unchecked(previous + 1) : null
                    : mutation.Value;
            else
                for (var i = 0; i < values.Length; i++)
                    if (mutation.Increment || values[i] != mutation.Value) values[i] = null;
        }
        return new(values);
    }
    /// <summary>Apply using independently established scratch for each unresolved occurrence.
    /// The same name can read different stale suffixes on successive calls.</summary>
    public int[] ApplyWithNameStorage(IReadOnlyList<int> initial,
        Func<int, WorldScriptVariableGlobalMutation, ReadOnlyMemory<byte>?> nameStorage)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(nameStorage);
        if (initial.Count != ValueCount) throw new ArgumentException("Native variable globals require 150 established values.", nameof(initial));
        var result = initial.ToArray();
        for (var occurrence = 0; occurrence < _mutations.Count; occurrence++)
        {
            var mutation = _mutations[occurrence];
            var index = mutation.Index;
            if (index is null)
            {
                var storage = nameStorage(occurrence, mutation) ??
                    throw new InvalidDataException($"Variable mutation {occurrence} ('{mutation.Name}') requires native name-buffer storage.");
                index = WorldScriptVariableNameStorage.ResolveIndex(mutation.Name, storage.Span);
            }
            result[index.Value] = mutation.Increment ? unchecked(result[index.Value] + 1) : mutation.Value;
        }
        return result;
    }
    internal void Record(string name, int value, bool increment)
    {
        // Preserve the original inequality in 0x49B2B0/0x49C930: matching prefixes skip this write.
        if (name.Length >= Prefix.Length && WorldScriptNameComparer.Instance.Equals(name[..Prefix.Length], Prefix)) return;
        int? index = name.Length < Prefix.Length ? null : Math.Clamp(Atoi(name.AsSpan(Prefix.Length)), 0, ValueCount - 1);
        _mutations.Add(new(name, index, increment ? 1 : value, increment));
    }
    /// <summary>Consecutive original variable callers share their suffix storage.
    /// A long copied name establishes it even when its matching prefix skips the write.</summary>
    internal int? RecordWithCallerIndex(string name, int value, bool increment, int? inheritedIndex)
    {
        Record(name, value, increment);
        if (name.Length >= Prefix.Length)
            return Math.Clamp(Atoi(name.AsSpan(Prefix.Length)), 0, ValueCount - 1);
        if (inheritedIndex is { } index)
            _mutations[^1] = _mutations[^1] with { Index = index };
        return inheritedIndex;
    }
    /// <summary>Gold 0x49D450 zeroes the entire 36-byte destination scratch row.
    /// A short name therefore has a known zero suffix at +7, unlike SetVar/AddVar.</summary>
    internal void RecordRandom(string name, int value)
    {
        if (name.Length < Prefix.Length) _mutations.Add(new(name, 0, value, false));
        else Record(name, value, false);
    }
    /// <summary>Apply once to an established initial array. Short names cause native reads after their NUL;
    /// without known name-buffer storage, their affected index stays unresolved.
    /// The name-only resolver is retained for explicit legacy fixtures; actual caller history
    /// requires ApplyWithNameStorage, since repeated names can inherit different suffixes.</summary>
    public int[] Apply(IReadOnlyList<int> initial, Func<string, int?>? resolveShortNameIndex = null)
    {
        ArgumentNullException.ThrowIfNull(initial);
        if (initial.Count != ValueCount) throw new ArgumentException("Native variable globals require 150 established values.", nameof(initial));
        var result = initial.ToArray();
        foreach (var mutation in _mutations)
        {
            var index = mutation.Index ?? resolveShortNameIndex?.Invoke(mutation.Name) ??
                throw new InvalidDataException($"Variable '{mutation.Name}' requires established native name-buffer suffix bytes for its global write.");
            if ((uint)index >= ValueCount) throw new InvalidDataException("Resolved variable global index exceeds 149.");
            result[index] = mutation.Increment ? unchecked(result[index] + 1) : mutation.Value;
        }
        return result;
    }
    private static int Atoi(ReadOnlySpan<char> text)
    {
        var offset = 0;
        while (offset < text.Length && (text[offset] == ' ' || text[offset] is >= '\t' and <= '\r')) offset++;
        var negative = offset < text.Length && text[offset] == '-';
        if (offset < text.Length && text[offset] is '+' or '-') offset++;
        var value = 0;
        while (offset < text.Length && text[offset] is >= '0' and <= '9') value = unchecked(value * 10 + text[offset++] - '0');
        return negative ? unchecked(-value) : value;
    }
}
